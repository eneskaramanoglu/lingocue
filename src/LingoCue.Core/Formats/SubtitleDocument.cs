using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Formats;

/// <summary>What a subtitle file contains, independent of its format.</summary>
public sealed class SubtitleDocument
{
    public List<Cue> Cues { get; init; } = [];

    /// <summary>ASS styles (empty for SRT/VTT).</summary>
    public List<SubtitleStyle> Styles { get; init; } = [];

    /// <summary>ASS <c>[Script Info]</c>; null for SRT/VTT.</summary>
    public AssScriptInfo? ScriptInfo { get; set; }

    /// <summary>WebVTT header text after <c>WEBVTT</c>, and STYLE/REGION blocks, kept verbatim.</summary>
    public string? VttHeader { get; set; }

    public IList<string> VttHeaderBlocks { get; init; } = [];

    /// <summary>Recoverable problems found while parsing (line numbers are 1-based).</summary>
    public List<string> Warnings { get; init; } = [];

    public SubtitleTrack ToTrack(string language = "tr", string name = "Altyazı") => new()
    {
        Language = language,
        Name = name,
        Cues = [.. Cues],
    };

    public static SubtitleDocument FromTrack(SubtitleTrack track) => new() { Cues = [.. track.Cues] };
}
