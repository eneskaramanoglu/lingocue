namespace LingoCue.Core.Subtitles;

/// <summary>What one line of the shown subtitle is made of.</summary>
public enum PlanLineKind
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The line as spoken (the active track's text).</summary>
    Original,

    /// <summary>The translation into <see cref="PlanLine.Language"/>.</summary>
    Translation,
}

/// <summary>One line of the shown subtitle: nothing, the original text or a translation.</summary>
public sealed record PlanLine(PlanLineKind Kind, string? Language = null)
{
    public static PlanLine None { get; } = new(PlanLineKind.None);

    public static PlanLine Original { get; } = new(PlanLineKind.Original);

    public static PlanLine TranslationTo(string language) => new(PlanLineKind.Translation, language);

    public bool IsShown => Kind != PlanLineKind.None;
}

/// <summary>
/// For lines spoken in <see cref="Language"/>: the subtitle shown on top (main style) and an optional
/// second one under it (second style), e.g. Korean speech → Turkish on top, English below.
/// </summary>
public sealed record LanguageRule
{
    public required string Language { get; init; }

    public PlanLine Top { get; init; } = PlanLine.Original;

    public PlanLine Bottom { get; init; } = PlanLine.None;
}

/// <summary>
/// Which subtitles are shown for each spoken language of a (mixed-language) track. Languages without a
/// rule show the original. Rules hold language codes only, so a plan carries over to other projects
/// in a style template.
/// </summary>
public sealed record SubtitlePlan
{
    public bool Enabled { get; init; }

    public IReadOnlyList<LanguageRule> Rules { get; init; } = [];

    /// <summary>The rule for a spoken language (primary subtag match); the original alone when there is none.</summary>
    public LanguageRule RuleFor(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        var code = Text.LanguageCodes.Primary(language);
        return Rules.FirstOrDefault(r => Text.LanguageCodes.Primary(r.Language) == code) ?? new LanguageRule { Language = code };
    }

    /// <summary>The same plan with <paramref name="rule"/> replacing the rule for its language.</summary>
    public SubtitlePlan With(LanguageRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var code = Text.LanguageCodes.Primary(rule.Language);
        return this with
        {
            Rules = [.. Rules.Where(r => Text.LanguageCodes.Primary(r.Language) != code), rule with { Language = code }],
        };
    }

    // Records compare lists by reference; compare the rules themselves so "unchanged" is detected.
    public bool Equals(SubtitlePlan? other)
        => other is not null && Enabled == other.Enabled && Rules.SequenceEqual(other.Rules);

    public override int GetHashCode() => HashCode.Combine(Enabled, Rules.Count);
}
