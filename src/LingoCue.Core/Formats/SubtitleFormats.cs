namespace LingoCue.Core.Formats;

/// <summary>All supported subtitle formats and how to pick one.</summary>
public static class SubtitleFormats
{
    public static SrtFormat Srt { get; } = new();

    public static VttFormat Vtt { get; } = new();

    public static AssFormat Ass { get; } = new();

    public static IReadOnlyList<ISubtitleFormat> All { get; } = [Srt, Vtt, Ass];

    public static IEnumerable<string> AllExtensions => All.SelectMany(f => f.Extensions);

    public static ISubtitleFormat? ForExtension(string pathOrExtension)
    {
        ArgumentNullException.ThrowIfNull(pathOrExtension);
        var extension = pathOrExtension.StartsWith('.') ? pathOrExtension : Path.GetExtension(pathOrExtension);
        return All.FirstOrDefault(f => f.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Content wins over the extension (renamed files are common); falls back to the extension.</summary>
    public static ISubtitleFormat? Detect(string content, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Vtt.LooksLike(content))
        {
            return Vtt;
        }

        if (Ass.LooksLike(content))
        {
            return Ass;
        }

        if (Srt.LooksLike(content))
        {
            return Srt;
        }

        return path is null ? null : ForExtension(path);
    }
}
