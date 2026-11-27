namespace LingoCue.Core.Subtitles;

/// <summary>Editing operations on the cues of a track: split, merge and nudging times without overlaps.</summary>
public static class CueEditing
{
    /// <summary>
    /// Gap kept between neighbouring cues when moving one: 2 frames at 25 fps, the gap the QA check asks
    /// for and the segmenter leaves, so editing never creates a "too small gap" warning.
    /// </summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(80);

    /// <summary>Shortest cue a split or a nudge may leave.</summary>
    public static readonly TimeSpan MinDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Splits <paramref name="cue"/> at <paramref name="at"/> (clamped inside the cue; the middle when
    /// outside). The text is cut at <paramref name="textIndex"/> when given (e.g. the editor's caret),
    /// otherwise at the word spoken at that moment when word timings exist, otherwise at the word
    /// boundary nearest the middle. Returns the new second cue, or null when the cue is too short.
    /// </summary>
    public static Cue? Split(SubtitleTrack track, Cue cue, TimeSpan at, int? textIndex = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(cue);
        if (cue.Duration < MinDuration + MinDuration)
        {
            return null;
        }

        var cut = at > cue.Start + MinDuration && at < cue.End - MinDuration ? at : cue.Start + (cue.Duration / 2);
        var (first, second) = SplitText(cue, cut, textIndex);

        var next = new Cue(track.NextCueId(), cut, cue.End, second)
        {
            Speaker = cue.Speaker,
            Language = cue.Language,
            Marks = cue.Marks,
            SourceCueId = cue.SourceCueId,
            Format = cue.Format,
        };
        next.Words.AddRange(cue.Words.Where(w => w.Start >= cut));
        cue.Words.RemoveAll(w => w.Start >= cut);
        cue.Text = first;
        cue.End = cut;

        track.Cues.Insert(track.Cues.IndexOf(cue) + 1, next);
        return next;
    }

    private static (string First, string Second) SplitText(Cue cue, TimeSpan cut, int? textIndex)
    {
        var text = cue.Text;
        if (textIndex is { } index && index > 0 && index < text.Length)
        {
            return (text[..index].TrimEnd(), text[index..].TrimStart());
        }

        var tokens = KaraokeTiming.Align(cue);
        if (tokens is not null && tokens.Count > 1)
        {
            var split = tokens.Count(t => t.Start < cut);
            split = Math.Clamp(split, 1, tokens.Count - 1);
            return (Join(tokens.Take(split)), Join(tokens.Skip(split)));
        }

        var parts = KaraokeTiming.Tokenize(text);
        if (parts.Count < 2)
        {
            return (text, string.Empty);
        }

        // Word boundary closest to the middle of the text.
        var half = SubtitleText.VisibleLength(text) / 2.0;
        var best = 1;
        var bestDistance = double.MaxValue;
        var length = 0;
        for (var i = 1; i < parts.Count; i++)
        {
            length += SubtitleText.VisibleLength(parts[i - 1].Text) + 1;
            if (Math.Abs(length - half) < bestDistance)
            {
                bestDistance = Math.Abs(length - half);
                best = i;
            }
        }

        return (JoinParts(parts.Take(best)), JoinParts(parts.Skip(best)));

        static string Join(IEnumerable<TimedToken> tokens) => JoinParts(tokens.Select(t => (t.Separator, t.Text)));
    }

    private static string JoinParts(IEnumerable<(string Separator, string Text)> parts)
        => string.Concat(parts.Select((p, i) => (i == 0 ? string.Empty : p.Separator) + p.Text)).Trim();

    /// <summary>
    /// Merges <paramref name="cue"/> with the cue after it. The texts join on one line when the result
    /// fits <paramref name="maxCharsPerLine"/>, otherwise as two lines. Returns false when there is no next cue.
    /// </summary>
    public static bool MergeWithNext(SubtitleTrack track, Cue cue, int maxCharsPerLine = 42)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(cue);
        var index = track.Cues.IndexOf(cue);
        if (index < 0 || index + 1 >= track.Cues.Count)
        {
            return false;
        }

        var next = track.Cues[index + 1];
        var a = cue.Text.Trim();
        var b = next.Text.Trim();
        var oneLine = $"{a} {b}".Trim();
        cue.Text = a.Length == 0 || b.Length == 0
            ? oneLine
            : !a.Contains('\n', StringComparison.Ordinal) && !b.Contains('\n', StringComparison.Ordinal) && SubtitleText.VisibleLength(oneLine) <= maxCharsPerLine
                ? oneLine
                : $"{a.Replace('\n', ' ')}\n{b.Replace('\n', ' ')}";
        cue.End = next.End > cue.End ? next.End : cue.End;
        cue.Marks |= next.Marks;
        cue.Words.AddRange(next.Words);
        track.Cues.RemoveAt(index + 1);
        return true;
    }

    /// <summary>
    /// Moves a cue (or only its start) by <paramref name="delta"/>, limited so it never overlaps its
    /// neighbours (keeping <see cref="MinGap"/>), never starts before zero and never gets shorter than
    /// <see cref="MinDuration"/>. Returns the shift actually applied.
    /// </summary>
    public static TimeSpan Nudge(SubtitleTrack track, Cue cue, TimeSpan delta, bool startOnly = false)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(cue);
        var index = track.Cues.IndexOf(cue);
        var previous = index > 0 ? track.Cues[index - 1] : null;
        var next = index >= 0 && index + 1 < track.Cues.Count ? track.Cues[index + 1] : null;

        var lowest = previous is null ? TimeSpan.Zero : previous.End + MinGap;
        var applied = delta;
        if (cue.Start + applied < lowest)
        {
            applied = lowest - cue.Start;
        }

        if (startOnly)
        {
            if (cue.Start + applied > cue.End - MinDuration)
            {
                applied = cue.End - MinDuration - cue.Start;
            }

            cue.Start += applied;
            return applied;
        }

        if (next is not null && cue.End + applied > next.Start - MinGap)
        {
            applied = next.Start - MinGap - cue.End;
        }

        // A neighbour already too close can make the limits contradict each other: then do not move.
        if ((delta > TimeSpan.Zero && applied < TimeSpan.Zero) || (delta < TimeSpan.Zero && applied > TimeSpan.Zero))
        {
            return TimeSpan.Zero;
        }

        cue.Start += applied;
        cue.End += applied;
        return applied;
    }

    /// <summary>
    /// The times a cue would get when dragged by <paramref name="delta"/> on the timeline (one edge or the
    /// whole block), limited like <see cref="Nudge"/>: no overlap with neighbours (keeping
    /// <see cref="MinGap"/>), not before zero or after <paramref name="mediaEnd"/>, at least
    /// <see cref="MinDuration"/> long. Does not change the cue.
    /// </summary>
    public static (TimeSpan Start, TimeSpan End) ClampDrag(SubtitleTrack track, Cue cue, DragHandle handle, TimeSpan delta, TimeSpan? mediaEnd = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(cue);
        var index = track.Cues.IndexOf(cue);
        var previous = index > 0 ? track.Cues[index - 1] : null;
        var next = index >= 0 && index + 1 < track.Cues.Count ? track.Cues[index + 1] : null;

        // Never pull the limits inside the cue itself (a neighbour may already overlap it).
        var lowest = Min(cue.Start, previous is null ? TimeSpan.Zero : previous.End + MinGap);
        var highestEnd = next is not null ? next.Start - MinGap : mediaEnd is { } end && end > TimeSpan.Zero ? end : TimeSpan.MaxValue;
        highestEnd = Max(cue.End, highestEnd);

        switch (handle)
        {
            case DragHandle.Start:
                return (Clamp(cue.Start + delta, lowest, cue.End - MinDuration > lowest ? cue.End - MinDuration : lowest), cue.End);

            case DragHandle.End:
                var minEnd = cue.Start + MinDuration;
                return (cue.Start, Clamp(SafeAdd(cue.End, delta), minEnd < highestEnd ? minEnd : highestEnd, highestEnd));

            default:
                var shift = delta;
                if (cue.Start + shift < lowest)
                {
                    shift = lowest - cue.Start;
                }

                if (highestEnd != TimeSpan.MaxValue && cue.End + shift > highestEnd)
                {
                    shift = highestEnd - cue.End;
                }

                return (cue.Start + shift, cue.End + shift);
        }

        static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;

        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

        static TimeSpan SafeAdd(TimeSpan a, TimeSpan b) => b > TimeSpan.Zero && a > TimeSpan.MaxValue - b ? TimeSpan.MaxValue : a + b;
    }
}

/// <summary>What is being dragged on the timeline.</summary>
public enum DragHandle
{
    Body,
    Start,
    End,
}
