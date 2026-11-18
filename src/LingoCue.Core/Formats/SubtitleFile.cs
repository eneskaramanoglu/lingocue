using System.Text;

namespace LingoCue.Core.Formats;

public enum LineEnding
{
    CrLf,
    Lf,
}

public sealed record SubtitleWriteOptions
{
    /// <summary>Default: no BOM. Premiere Pro reads both; some older players need a BOM for non-ASCII text.</summary>
    public bool Utf8Bom { get; init; }

    /// <summary>CRLF is the conservative default for SRT (Windows tools, Premiere).</summary>
    public LineEnding LineEnding { get; init; } = LineEnding.CrLf;

    public static SubtitleWriteOptions Default { get; } = new();
}

public sealed record SubtitleReadResult(SubtitleDocument Document, ISubtitleFormat Format, Encoding Encoding);

public sealed class SubtitleFormatException : Exception
{
    public SubtitleFormatException()
    {
    }

    public SubtitleFormatException(string message)
        : base(message)
    {
    }

    public SubtitleFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Reads and writes subtitle files, handling text encoding.</summary>
public static class SubtitleFile
{
    private static readonly Lazy<Encoding> s_turkishAnsi = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1254);
    });

    /// <summary>Windows-1254, the legacy encoding of many Turkish SRT files.</summary>
    public static Encoding TurkishAnsi => s_turkishAnsi.Value;

    public static async Task<SubtitleReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var (text, encoding) = Decode(bytes);
        var format = SubtitleFormats.Detect(text, path)
            ?? throw new SubtitleFormatException($"Unrecognized subtitle format: {Path.GetFileName(path)}");
        return new SubtitleReadResult(format.Parse(text), format, encoding);
    }

    public static async Task WriteAsync(
        string path, SubtitleDocument document, ISubtitleFormat? format = null, SubtitleWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        format ??= SubtitleFormats.ForExtension(path) ?? throw new SubtitleFormatException($"Unknown subtitle extension: {Path.GetExtension(path)}");
        var bytes = Encode(format.Write(document), options ?? SubtitleWriteOptions.Default);
        await AtomicFile.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    /// <summary>
    /// BOM (UTF-8/16/32) wins; otherwise strict UTF-8; otherwise Windows-1254 (Turkish ANSI).
    /// Pure ASCII decodes the same under all of them.
    /// </summary>
    public static (string Text, Encoding Encoding) Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return (Encoding.UTF8.GetString(bytes[3..]), new UTF8Encoding(true));
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
        {
            return (Encoding.UTF32.GetString(bytes[4..]), Encoding.UTF32);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return (Encoding.Unicode.GetString(bytes[2..]), Encoding.Unicode);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return (Encoding.BigEndianUnicode.GetString(bytes[2..]), Encoding.BigEndianUnicode);
        }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes), new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            return (TurkishAnsi.GetString(bytes), TurkishAnsi);
        }
    }

    public static byte[] Encode(string text, SubtitleWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (options.LineEnding == LineEnding.CrLf)
        {
            normalized = normalized.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        var body = Encoding.UTF8.GetBytes(normalized);
        return options.Utf8Bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }
}

/// <summary>Writes to a temp file next to the target and then replaces it, so a crash never leaves a half-written file.</summary>
public static class AtomicFile
{
    public static async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public static Task WriteAllTextAsync(string path, string text, CancellationToken cancellationToken = default)
        => WriteAllBytesAsync(path, new UTF8Encoding(false).GetBytes(text), cancellationToken);
}
