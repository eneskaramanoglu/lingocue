using System.Text.Json;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Probing;

public interface IMediaProbe
{
    /// <exception cref="MediaToolNotFoundException">ffprobe is not installed.</exception>
    /// <exception cref="MediaProbeException">The file could not be read as media.</exception>
    Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}

public sealed class MediaProbeException : Exception
{
    public MediaProbeException()
    {
    }

    public MediaProbeException(string message)
        : base(message)
    {
    }

    public MediaProbeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class MediaProbe(IMediaToolLocator locator, IProcessRunner runner) : IMediaProbe
{
    public async Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Media file not found.", filePath);
        }

        var tools = locator.Locate();
        string[] args =
        [
            "-v", "error",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            filePath,
        ];

        var result = await runner.RunAsync(tools.FfprobePath, args, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new MediaProbeException($"ffprobe failed ({result.ExitCode}): {result.StandardError.Trim()}");
        }

        MediaInfo info;
        try
        {
            info = FfprobeJsonParser.Parse(filePath, result.StandardOutput);
        }
        catch (JsonException ex)
        {
            throw new MediaProbeException("ffprobe returned unreadable output.", ex);
        }

        if (info.Video is null && !info.HasAudio)
        {
            throw new MediaProbeException("The file contains no audio or video streams.");
        }

        return info;
    }
}
