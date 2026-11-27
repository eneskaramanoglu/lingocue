using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;

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

    /// <summary>Reading rules for lines in another language than <see cref="Language"/> (mixed-language recordings).</summary>
    public IReadOnlyDictionary<string, ReadingRules>? RulesByLanguage { get; init; }

    /// <summary>
    /// The reading rules for words in <paramref name="language"/>: <see cref="Rules"/> for the main language,
    /// else <see cref="RulesByLanguage"/>, else the Korean scale for Korean and <see cref="Rules"/> otherwise.
    /// </summary>
    public ReadingRules RulesFor(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        var code = LanguageCodes.Primary(language);
        if (code == LanguageCodes.Primary(Language))
        {
            return Rules;
        }

        if (RulesByLanguage is not null)
        {
            foreach (var (key, rules) in RulesByLanguage)
            {
                if (LanguageCodes.Primary(key) == code)
                {
                    return rules;
                }
            }
        }

        return code == "ko" ? ReadingRulesSettings.KoreanRules(Rules)
            : LanguageCodes.Primary(Language) == "ko" ? ReadingRules.Default
            : Rules;
    }
}
