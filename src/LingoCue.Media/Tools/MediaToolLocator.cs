using LingoCue.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Media.Tools;

public interface IMediaToolLocator
{
    /// <summary>Finds ffmpeg and ffprobe or throws <see cref="MediaToolNotFoundException"/>.</summary>
    MediaTools Locate();

    bool TryLocate(out MediaTools? tools);
}

/// <summary>
/// Search order: explicit setting → platform tool directories (app folder, app-data tools folder,
/// package-manager folders) → PATH. The result is cached once found.
/// </summary>
public sealed partial class MediaToolLocator : IMediaToolLocator
{
    private readonly IPlatformInfo _platform;
    private readonly MediaToolOptions _options;
    private readonly ILogger<MediaToolLocator> _logger;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string?> _pathVariable;
    private readonly Lock _gate = new();
    private MediaTools? _cached;

    public MediaToolLocator(IPlatformInfo platform, MediaToolOptions options, ILogger<MediaToolLocator> logger)
        : this(platform, options, logger, File.Exists, () => Environment.GetEnvironmentVariable("PATH"))
    {
    }

    internal MediaToolLocator(
        IPlatformInfo platform,
        MediaToolOptions options,
        ILogger<MediaToolLocator>? logger,
        Func<string, bool> fileExists,
        Func<string?> pathVariable)
    {
        _platform = platform;
        _options = options;
        _logger = logger ?? NullLogger<MediaToolLocator>.Instance;
        _fileExists = fileExists;
        _pathVariable = pathVariable;
    }

    public MediaTools Locate()
    {
        lock (_gate)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var ffmpeg = Find("ffmpeg", _options.FfmpegPath);
            var ffprobe = Find("ffprobe", _options.FfprobePath, preferredDirectory: Path.GetDirectoryName(ffmpeg));
            _cached = new MediaTools(ffmpeg, ffprobe);
            LogToolsFound(ffmpeg, ffprobe);
            return _cached;
        }
    }

    public bool TryLocate(out MediaTools? tools)
    {
        try
        {
            tools = Locate();
            return true;
        }
        catch (MediaToolNotFoundException ex)
        {
            LogToolsMissing(ex.ToolName, ex.SearchedLocations.Count);
            tools = null;
            return false;
        }
    }

    private string Find(string tool, string? configuredPath, string? preferredDirectory = null)
    {
        var searched = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            searched.Add(configuredPath);
            if (_fileExists(configuredPath))
            {
                return configuredPath;
            }

            LogConfiguredPathMissing(tool, configuredPath);
        }

        var fileName = _platform.GetExecutableFileName(tool);
        foreach (var dir in CandidateDirectories(preferredDirectory))
        {
            var candidate = Path.Combine(dir, fileName);
            searched.Add(candidate);
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        throw new MediaToolNotFoundException(tool, searched);
    }

    private IEnumerable<string> CandidateDirectories(string? preferredDirectory)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(preferredDirectory) && seen.Add(preferredDirectory))
        {
            yield return preferredDirectory;
        }

        foreach (var dir in _platform.ToolSearchDirectories)
        {
            if (seen.Add(dir))
            {
                yield return dir;
            }
        }

        var path = _pathVariable();
        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(dir))
            {
                yield return dir;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Using ffmpeg at {Ffmpeg} and ffprobe at {Ffprobe}")]
    private partial void LogToolsFound(string ffmpeg, string ffprobe);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Tool} not found after searching {Count} locations")]
    private partial void LogToolsMissing(string tool, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Configured {Tool} path {Path} does not exist, falling back to auto-detection")]
    private partial void LogConfiguredPathMissing(string tool, string path);
}
