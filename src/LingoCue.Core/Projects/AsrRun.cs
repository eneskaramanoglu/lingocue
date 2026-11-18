using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Projects;

/// <summary>
/// Cached speech-recognition output stored in the project, so re-segmenting or re-creating a track
/// never pays for (or waits on) recognition again.
/// </summary>
public sealed record AsrRun
{
    public required string Engine { get; init; }

    public required string Model { get; init; }

    /// <summary>Requested language (null = auto-detect).</summary>
    public string? Language { get; init; }

    public string? DetectedLanguage { get; init; }

    public bool Diarized { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public TimeSpan Elapsed { get; init; }

    public decimal? CostUsd { get; init; }

    public List<Word> Words { get; init; } = [];

    public bool Matches(string engine, string model, string? language, bool diarized)
        => Engine == engine && Model == model && Diarized == diarized &&
           string.Equals(Language ?? "auto", language ?? "auto", StringComparison.OrdinalIgnoreCase);
}
