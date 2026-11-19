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

    /// <summary>Main ASS style used for preview, ASS export and burn-in.</summary>
    public SubtitleStyle Style { get; set; } = new();

    /// <summary>Upper case, fades, karaoke and bilingual mode (applied when building the ASS script).</summary>
    public PresentationOptions Presentation { get; set; } = new();

    /// <summary>Script resolution for ASS output; follows the video size when known.</summary>
    public AssScriptInfo ScriptInfo { get; set; } = new();

    /// <summary>Names, terms and agreed spellings/translations used by the LLM passes.</summary>
    public List<GlossaryEntry> Glossary { get; init; } = [];

    /// <summary>Summary, tone, address and speakers from the LLM context pass (editable).</summary>
    public ProjectContext Context { get; set; } = new();

    /// <summary>Speech-recognition results kept with the project (newest last).</summary>
    public List<AsrRun> AsrRuns { get; init; } = [];

    public AsrRun? FindAsrRun(string engine, string model, string? language, bool diarized)
        => AsrRuns.LastOrDefault(r => r.Matches(engine, model, language, diarized));

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
