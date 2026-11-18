namespace LingoCue.Media.Tools;

/// <summary>Resolved absolute paths of the FFmpeg executables.</summary>
public sealed record MediaTools(string FfmpegPath, string FfprobePath);

/// <summary>User overrides for tool locations (from settings). Empty means auto-detect.</summary>
public sealed class MediaToolOptions
{
    public string? FfmpegPath { get; set; }

    public string? FfprobePath { get; set; }
}

/// <summary>Thrown when ffmpeg/ffprobe cannot be found. <see cref="SearchedLocations"/> helps users fix it.</summary>
public sealed class MediaToolNotFoundException : Exception
{
    public MediaToolNotFoundException()
    {
        ToolName = string.Empty;
        SearchedLocations = [];
    }

    public MediaToolNotFoundException(string message)
        : base(message)
    {
        ToolName = string.Empty;
        SearchedLocations = [];
    }

    public MediaToolNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        ToolName = string.Empty;
        SearchedLocations = [];
    }

    public MediaToolNotFoundException(string toolName, IReadOnlyList<string> searchedLocations)
        : base($"'{toolName}' was not found. Searched: {string.Join(", ", searchedLocations)}")
    {
        ToolName = toolName;
        SearchedLocations = searchedLocations;
    }

    public string ToolName { get; }

    public IReadOnlyList<string> SearchedLocations { get; }
}
