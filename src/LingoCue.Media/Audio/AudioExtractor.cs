using System.Globalization;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Audio;

public enum AudioOutputFormat
{
    /// <summary>16 kHz mono 16-bit PCM WAV, what whisper.cpp expects.</summary>
    Wav16kMono,

    /// <summary>16 kHz mono FLAC: lossless and about half the size, for uploading to cloud ASR.</summary>
    Flac16kMono,
}

public interface IAudioExtractor
{
    /// <summary>Writes the first audio stream (or a slice of it) to <paramref name="outputPath"/>.</summary>
    Task ExtractAsync(
        string inputPath,
        string outputPath,
        AudioOutputFormat format,
        TimeSpan? start = null,
        TimeSpan? duration = null,
        TimeSpan? totalForProgress = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Finds silences of at least <paramref name="minSilence"/> below <paramref name="noiseDb"/>.</summary>
    Task<IReadOnlyList<SilenceInterval>> DetectSilenceAsync(
        string inputPath, TimeSpan total, double noiseDb = -35, TimeSpan? minSilence = null, CancellationToken cancellationToken = default);
}

public sealed class AudioExtractionException : Exception
{
    public AudioExtractionException()
    {
    }

    public AudioExtractionException(string message)
        : base(message)
    {
    }

    public AudioExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class AudioExtractor(IMediaToolLocator locator, IProcessRunner runner) : IAudioExtractor
{
    public async Task ExtractAsync(
        string inputPath,
        string outputPath,
        AudioOutputFormat format,
        TimeSpan? start = null,
        TimeSpan? duration = null,
        TimeSpan? totalForProgress = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var tools = locator.Locate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var args = new List<string> { "-nostdin", "-hide_banner", "-v", "error", "-nostats", "-progress", "pipe:1", "-y" };
        if (start is { } s && s > TimeSpan.Zero)
        {
            // Input seeking is fast and, for audio, sample accurate.
            args.AddRange(["-ss", Seconds(s)]);
        }

        args.AddRange(["-i", inputPath]);
        if (duration is { } d)
        {
            args.AddRange(["-t", Seconds(d)]);
        }

        args.AddRange(["-map", "0:a:0", "-vn", "-sn", "-dn", "-ac", "1", "-ar", "16000"]);
        args.AddRange(format == AudioOutputFormat.Wav16kMono ? ["-c:a", "pcm_s16le", "-f", "wav"] : ["-c:a", "flac", "-f", "flac"]);
        args.Add(outputPath);

        var tracker = new FfmpegProgress(duration ?? totalForProgress ?? TimeSpan.Zero, progress);
        var result = await runner.RunAsync(tools.FfmpegPath, args, tracker.OnLine, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            TryDelete(outputPath);
            throw new AudioExtractionException($"ffmpeg could not extract audio ({result.ExitCode}): {LastLine(result.StandardError)}");
        }

        progress?.Report(1);
    }

    public async Task<IReadOnlyList<SilenceInterval>> DetectSilenceAsync(
        string inputPath, TimeSpan total, double noiseDb = -35, TimeSpan? minSilence = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var tools = locator.Locate();
        var min = minSilence ?? TimeSpan.FromMilliseconds(400);
        string[] args =
        [
            "-nostdin", "-hide_banner", "-nostats", "-i", inputPath, "-map", "0:a:0", "-vn",
            "-af", string.Create(CultureInfo.InvariantCulture, $"silencedetect=noise={noiseDb}dB:d={min.TotalSeconds:0.###}"),
            "-f", "null", "-",
        ];

        // silencedetect logs at info level on stderr.
        var result = await runner.RunAsync(tools.FfmpegPath, args, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new AudioExtractionException($"ffmpeg silence detection failed ({result.ExitCode}): {LastLine(result.StandardError)}");
        }

        return AudioChunkPlanner.ParseSilenceDetect(result.StandardError, total);
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

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
