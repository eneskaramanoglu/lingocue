using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Segmentation;

public sealed record SegmentationOptions
{
    public static SegmentationOptions Default { get; } = new();

    public ReadingRules Rules { get; init; } = ReadingRules.Default;

    /// <summary>A silence longer than this between words always starts a new cue.</summary>
    public TimeSpan PauseThreshold { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Minimum gap kept between consecutive cues when timings are extended.</summary>
    public TimeSpan MinGap { get; init; } = TimeSpan.FromMilliseconds(80);

    /// <summary>BCP 47 language of the words; enables language-specific break rules (Turkish).</summary>
    public string Language { get; init; } = "tr";

    public bool SplitOnSpeakerChange { get; init; } = true;
}
