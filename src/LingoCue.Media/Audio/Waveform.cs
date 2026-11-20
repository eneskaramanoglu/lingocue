using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LingoCue.Core.Platform;
using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Audio;

/// <summary>
/// Peak levels of an audio track for drawing: one byte (0–255, absolute peak) per
/// <see cref="BucketsPerSecond"/>-th of a second, mono mix.
/// </summary>
public sealed class Waveform(byte[] peaks, int bucketsPerSecond)
{
    public const int DefaultBucketsPerSecond = 100;

    public IReadOnlyList<byte> Peaks { get; } = peaks;

    public int BucketsPerSecond { get; } = bucketsPerSecond;

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Peaks.Count / BucketsPerSecond);

    /// <summary>Highest peak (0..1) between two times; used when one pixel covers many buckets.</summary>
    public double PeakBetween(TimeSpan from, TimeSpan to)
    {
        var first = Math.Max(0, (int)Math.Floor(from.TotalSeconds * BucketsPerSecond));
        // A bucket ending exactly at "to" counts; the next one does not.
        var last = Math.Min(Peaks.Count - 1, Math.Max(first, (int)Math.Ceiling(to.TotalSeconds * BucketsPerSecond) - 1));
        var max = 0;
        for (var i = first; i <= last; i++)
        {
            max = Math.Max(max, Peaks[i]);
        }

        return max / 255.0;
    }

    /// <summary>Builds peaks from 16-bit little-endian mono PCM.</summary>
    public static Waveform FromPcm16(Stream pcm, int sampleRate, int bucketsPerSecond = DefaultBucketsPerSecond)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, bucketsPerSecond);
        var samplesPerBucket = sampleRate / bucketsPerSecond;

        var peaks = new List<byte>();
        var buffer = new byte[64 * 1024];
        var carry = -1; // odd byte left over from the previous read
        var inBucket = 0;
        var max = 0;
        int read;
        while ((read = pcm.Read(buffer, 0, buffer.Length)) > 0)
        {
            var i = 0;
            if (carry >= 0)
            {
                Add((short)(carry | (buffer[0] << 8)));
                carry = -1;
                i = 1;
            }

            for (; i + 1 < read; i += 2)
            {
                Add(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2)));
            }

            if (i < read)
            {
                carry = buffer[i];
            }
        }

        if (inBucket > 0)
        {
            peaks.Add(Scale(max));
        }

        return new Waveform([.. peaks], bucketsPerSecond);

        void Add(short sample)
        {
            max = Math.Max(max, Math.Abs((int)sample));
            if (++inBucket == samplesPerBucket)
            {
                peaks.Add(Scale(max));
                inBucket = 0;
                max = 0;
            }
        }

        // Square-root scale so quiet speech is still visible next to loud parts.
        static byte Scale(int peak) => (byte)Math.Round(Math.Sqrt(Math.Min(peak, 32767) / 32767.0) * 255);
    }

    // File format: "LCWF" magic, version byte, int32 buckets per second, int32 count, peaks.
    private static ReadOnlySpan<byte> Magic => "LCWF"u8;

    private const byte FormatVersion = 1;

    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[13];
        Magic.CopyTo(header);
        header[4] = FormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(header[5..], BucketsPerSecond);
        BinaryPrimitives.WriteInt32LittleEndian(header[9..], Peaks.Count);
        stream.Write(header);
        stream.Write([.. Peaks]);
    }

    /// <summary>Reads a saved waveform; null when the data is not a complete, current-version file.</summary>
    public static Waveform? TryLoad(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[13];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length ||
            !header[..4].SequenceEqual(Magic) || header[4] != FormatVersion)
        {
            return null;
        }

        var rate = BinaryPrimitives.ReadInt32LittleEndian(header[5..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(header[9..]);
        if (rate <= 0 || count < 0 || count > 100_000_000)
        {
            return null;
        }

        var peaks = new byte[count];
        return stream.ReadAtLeast(peaks, count, throwOnEndOfStream: false) == count ? new Waveform(peaks, rate) : null;
    }
}

public interface IWaveformService
{
    /// <summary>
    /// Returns the waveform of the first audio stream, from the cache when the file is unchanged,
    /// otherwise decoded with FFmpeg in the background and cached. Null when the file has no audio.
    /// </summary>
    /// <exception cref="AudioExtractionException">The file could not be read or decoded.</exception>
    Task<Waveform?> GetAsync(string mediaPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decodes audio to 8 kHz mono PCM in a temporary file and reduces it to peaks. The cache lives in
/// <c>&lt;app data&gt;/cache/waveforms</c>, keyed by full path, size and modification time.
/// </summary>
public sealed class WaveformService(IMediaToolLocator locator, IProcessRunner runner, IMediaProbe probe, string cacheDirectory)
    : IWaveformService
{
    private const int SampleRate = 8000;

    public WaveformService(IMediaToolLocator locator, IProcessRunner runner, IMediaProbe probe, IPlatformInfo platform)
        : this(locator, runner, probe, Path.Combine(platform.AppDataDirectory, "cache", "waveforms"))
    {
    }

    public async Task<Waveform?> GetAsync(
        string mediaPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        var cachePath = Path.Combine(cacheDirectory, CacheKey(mediaPath) + ".lcwf");
        if (TryReadCache(cachePath) is { } cached)
        {
            progress?.Report(1);
            return cached;
        }

        var tools = locator.Locate();
        MediaInfo info;
        try
        {
            info = await probe.ProbeAsync(mediaPath, cancellationToken).ConfigureAwait(false);
        }
        catch (MediaProbeException ex)
        {
            throw new AudioExtractionException($"The file could not be read for the waveform: {ex.Message}", ex);
        }

        if (!info.HasAudio)
        {
            return null;
        }

        Directory.CreateDirectory(cacheDirectory);
        var rawPath = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N") + ".raw");
        try
        {
            string[] args =
            [
                "-nostdin", "-hide_banner", "-v", "error", "-nostats", "-progress", "pipe:1", "-y",
                "-i", mediaPath, "-map", "0:a:0", "-vn", "-sn", "-dn",
                "-ac", "1", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture), "-c:a", "pcm_s16le", "-f", "s16le", rawPath,
            ];

            var tracker = new FfmpegProgress(info.Duration, progress);
            var result = await runner.RunAsync(tools.FfmpegPath, args, tracker.OnLine, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.ExitCode != 0)
            {
                throw new AudioExtractionException($"ffmpeg could not decode audio for the waveform ({result.ExitCode}): {LastLine(result.StandardError)}");
            }

            // A stream that decodes to nothing (zero-length audio) has no waveform either.
            if (!File.Exists(rawPath) || new FileInfo(rawPath).Length == 0)
            {
                return null;
            }

            Waveform waveform;
            await using (var raw = File.OpenRead(rawPath))
            {
                waveform = Waveform.FromPcm16(raw, SampleRate);
            }

            WriteCache(cachePath, waveform);
            progress?.Report(1);
            return waveform;
        }
        finally
        {
            TryDelete(rawPath);
        }
    }

    internal static string CacheKey(string mediaPath)
    {
        var info = new FileInfo(mediaPath);
        var identity = string.Create(CultureInfo.InvariantCulture,
            $"{info.FullName}|{(info.Exists ? info.Length : -1)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
    }

    private static Waveform? TryReadCache(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return Waveform.TryLoad(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteCache(string path, Waveform waveform)
    {
        // Write to a temporary name first so a crash never leaves a half file under the real name.
        var temp = path + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            {
                waveform.Save(stream);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (IOException)
        {
            TryDelete(temp);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temp);
        }
    }

    private static string LastLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
