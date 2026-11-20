namespace LingoCue.Core.Subtitles;

public enum QaSeverity
{
    Warning,
    Error,
}

/// <param name="Kind">The rule (<see cref="CueIssues"/> flag name, <c>Overlap</c> or <c>SmallGap</c>).</param>
public sealed record QaIssue(int CueId, int Number, TimeSpan Start, QaSeverity Severity, string Kind, string Message);

/// <summary>
/// The QA panel's checks over a whole track: the reading rules per cue plus problems between cues
/// (overlaps, gaps too small to notice the change), and the two quick fixes it offers.
/// </summary>
public static class QualityCheck
{
    /// <summary>Gaps below this read as one flickering subtitle (Netflix asks for at least 2 frames).</summary>
    public static readonly TimeSpan MinNoticeableGap = TimeSpan.FromMilliseconds(80);

    private static readonly System.Globalization.CultureInfo s_tr = Text.TurkishText.Culture;

    public static IReadOnlyList<QaIssue> Check(SubtitleTrack track, ReadingRules rules)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(rules);
        var cues = track.Cues.OrderBy(c => c.Start).ThenBy(c => c.End).ToList();
        var issues = new List<QaIssue>();
        for (var i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            var number = i + 1;
            var flags = rules.Evaluate(cue);
            void Add(QaSeverity severity, string kind, string message) => issues.Add(new QaIssue(cue.Id, number, cue.Start, severity, kind, message));

            if (flags.HasFlag(CueIssues.Empty))
            {
                Add(QaSeverity.Error, nameof(CueIssues.Empty), "Boş satır");
            }

            if (flags.HasFlag(CueIssues.ReadingSpeedTooHigh))
            {
                Add(QaSeverity.Error, nameof(CueIssues.ReadingSpeedTooHigh), string.Create(s_tr, $"Okuma hızı çok yüksek ({cue.CharactersPerSecond:0.0} CPS > {rules.MaxCps})"));
            }
            else if (flags.HasFlag(CueIssues.ReadingSpeedHigh))
            {
                Add(QaSeverity.Warning, nameof(CueIssues.ReadingSpeedHigh), string.Create(s_tr, $"Okuma hızı yüksek ({cue.CharactersPerSecond:0.0} CPS > {rules.TargetCps})"));
            }

            if (flags.HasFlag(CueIssues.TooManyLines))
            {
                Add(QaSeverity.Error, nameof(CueIssues.TooManyLines), $"{rules.MaxLines} satırdan fazla");
            }

            if (flags.HasFlag(CueIssues.LineTooLong))
            {
                Add(QaSeverity.Warning, nameof(CueIssues.LineTooLong), $"Satır {rules.MaxCharsPerLine} karakterden uzun");
            }

            if (flags.HasFlag(CueIssues.TooShort))
            {
                Add(QaSeverity.Warning, nameof(CueIssues.TooShort), string.Create(s_tr, $"{rules.MinDuration.TotalSeconds:0.0} sn'den kısa"));
            }

            if (flags.HasFlag(CueIssues.TooLong))
            {
                Add(QaSeverity.Warning, nameof(CueIssues.TooLong), string.Create(s_tr, $"{rules.MaxDuration.TotalSeconds:0} sn'den uzun"));
            }

            if (i + 1 < cues.Count)
            {
                var gap = cues[i + 1].Start - cue.End;
                if (gap < TimeSpan.Zero)
                {
                    Add(QaSeverity.Error, "Overlap", string.Create(s_tr, $"Sonraki satırla {-gap.TotalMilliseconds:0} ms çakışıyor"));
                }
                else if (gap > TimeSpan.Zero && gap < MinNoticeableGap)
                {
                    Add(QaSeverity.Warning, "SmallGap", string.Create(s_tr, $"Sonraki satırla arası çok kısa ({gap.TotalMilliseconds:0} ms)"));
                }
            }
        }

        return issues;
    }

    /// <summary>Ends each overlapping cue just before the next one starts. Returns how many changed.</summary>
    public static int FixOverlaps(SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        track.Sort();
        var fixedCount = 0;
        for (var i = 0; i + 1 < track.Cues.Count; i++)
        {
            var cue = track.Cues[i];
            var next = track.Cues[i + 1];
            if (cue.End > next.Start - CueEditing.MinGap)
            {
                var end = next.Start - CueEditing.MinGap;
                if (end - cue.Start >= CueEditing.MinDuration && end != cue.End)
                {
                    cue.End = end;
                    fixedCount++;
                }
            }
        }

        return fixedCount;
    }

    /// <summary>
    /// Lengthens cues that are read too fast, up to the target reading speed, without touching the
    /// next cue (and never past the maximum duration). Returns how many changed.
    /// </summary>
    public static int ExtendForReadingSpeed(SubtitleTrack track, ReadingRules rules)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(rules);
        track.Sort();
        var changed = 0;
        for (var i = 0; i < track.Cues.Count; i++)
        {
            var cue = track.Cues[i];
            if (cue.CharactersPerSecond <= rules.TargetCps || rules.TargetCps <= 0)
            {
                continue;
            }

            var wanted = cue.Start + TimeSpan.FromSeconds(cue.VisibleLength / rules.TargetCps);
            var limit = i + 1 < track.Cues.Count ? track.Cues[i + 1].Start - CueEditing.MinGap : TimeSpan.MaxValue;
            var maxEnd = cue.Start + rules.MaxDuration;
            var end = new[] { wanted, limit, maxEnd }.Min();
            if (end > cue.End)
            {
                cue.End = end;
                changed++;
            }
        }

        return changed;
    }
}
