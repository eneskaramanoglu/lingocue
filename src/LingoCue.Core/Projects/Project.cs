using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Projects;

/// <summary>A LingoCue project: one video plus its subtitle tracks, style and caches.</summary>
public sealed class Project
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Absolute path in memory; stored relative to the project file when possible.</summary>
    public string? VideoPath { get; set; }

    /// <summary>Spoken language of the video (BCP 47).</summary>
    public string SourceLanguage { get; set; } = "tr";

    public List<SubtitleTrack> Tracks { get; init; } = [];

    public Guid? ActiveTrackId { get; set; }

    /// <summary>Style used for preview and burn-in until the style editor (M5) adds more.</summary>
    public SubtitleStyle Style { get; set; } = new();

    /// <summary>Script resolution for ASS output; follows the video size when known.</summary>
    public AssScriptInfo ScriptInfo { get; set; } = new();

    public SubtitleTrack? ActiveTrack
        => Tracks.FirstOrDefault(t => t.Id == ActiveTrackId) ?? Tracks.FirstOrDefault();

    public SubtitleTrack EnsureActiveTrack()
    {
        if (ActiveTrack is { } track)
        {
            return track;
        }

        track = new SubtitleTrack { Language = SourceLanguage, Name = "Altyazı" };
        Tracks.Add(track);
        ActiveTrackId = track.Id;
        return track;
    }
}
