namespace LingoCue.Core.Subtitles;

/// <summary>How the active word of a cue is highlighted.</summary>
public enum KaraokeMode
{
    Off,

    /// <summary>Color fills each word as it is spoken (ASS <c>\kf</c>); spoken words stay highlighted.</summary>
    Fill,

    /// <summary>Only the word being spoken is highlighted (Shorts/Reels look).</summary>
    ActiveWord,
}

public sealed record KaraokeOptions
{
    public KaraokeMode Mode { get; init; } = KaraokeMode.Off;

    /// <summary>Color of the spoken (Fill) or active (ActiveWord) word.</summary>
    public AssColor HighlightColor { get; init; } = AssColor.Yellow;
}

/// <summary>Shows a second track under the active one (e.g. Turkish above, English below).</summary>
public sealed record BilingualOptions
{
    public bool Enabled { get; init; }

    public Guid? SecondaryTrackId { get; init; }

    /// <summary>Style of the second track; its alignment is taken from the main style.</summary>
    public SubtitleStyle SecondaryStyle { get; init; } = DefaultSecondaryStyle;

    /// <summary>Space between the two blocks, in script pixels (1080-high canvas).</summary>
    public int Gap { get; init; } = 12;

    public static SubtitleStyle DefaultSecondaryStyle { get; } = new()
    {
        Name = "Secondary",
        FontSize = 38,
        PrimaryColor = new AssColor(255, 230, 140),
        Outline = 2,
        Shadow = 1,
    };
}

/// <summary>
/// Everything about how a project's subtitles look beyond the main ASS style. All of it is turned
/// into plain ASS by <c>AssScriptBuilder</c>, so preview and burn-in render the same thing.
/// </summary>
public sealed record PresentationOptions
{
    /// <summary>Show text in capitals (culture-aware: i → İ for Turkish).</summary>
    public bool UpperCase { get; init; }

    public int FadeInMs { get; init; }

    public int FadeOutMs { get; init; }

    public KaraokeOptions Karaoke { get; init; } = new();

    public BilingualOptions Bilingual { get; init; } = new();
}
