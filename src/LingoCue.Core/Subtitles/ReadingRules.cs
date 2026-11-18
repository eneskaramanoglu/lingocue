namespace LingoCue.Core.Subtitles;

[Flags]
public enum CueIssues
{
    None = 0,
    ReadingSpeedHigh = 1,
    ReadingSpeedTooHigh = 2,
    LineTooLong = 4,
    TooManyLines = 8,
    TooShort = 16,
    TooLong = 32,
    Empty = 64,
}

/// <summary>
/// Readability limits (configurable). Defaults follow common broadcast/Netflix-style guidance and
/// the plan: 42 chars per line, 2 lines, 0.8–7 s, ~17 CPS target.
/// </summary>
public sealed record ReadingRules
{
    public static ReadingRules Default { get; } = new();

    public int MaxCharsPerLine { get; init; } = 42;

    public int MaxLines { get; init; } = 2;

    public TimeSpan MinDuration { get; init; } = TimeSpan.FromSeconds(0.8);

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(7);

    /// <summary>Target reading speed; above this the cue is flagged as a warning.</summary>
    public double TargetCps { get; init; } = 17;

    /// <summary>Hard limit; above this the cue is flagged as an error.</summary>
    public double MaxCps { get; init; } = 21;

    public CueIssues Evaluate(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        var issues = CueIssues.None;

        if (SubtitleText.StripTags(cue.Text).Trim().Length == 0)
        {
            issues |= CueIssues.Empty;
        }

        var cps = cue.CharactersPerSecond;
        if (cps > MaxCps)
        {
            issues |= CueIssues.ReadingSpeedTooHigh;
        }
        else if (cps > TargetCps)
        {
            issues |= CueIssues.ReadingSpeedHigh;
        }

        if (SubtitleText.LongestLineLength(cue.Text) > MaxCharsPerLine)
        {
            issues |= CueIssues.LineTooLong;
        }

        if (SubtitleText.LineCount(cue.Text) > MaxLines)
        {
            issues |= CueIssues.TooManyLines;
        }

        if (cue.Duration < MinDuration)
        {
            issues |= CueIssues.TooShort;
        }
        else if (cue.Duration > MaxDuration)
        {
            issues |= CueIssues.TooLong;
        }

        return issues;
    }
}
