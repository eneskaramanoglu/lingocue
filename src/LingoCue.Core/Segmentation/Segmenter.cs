using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Segmentation;

/// <summary>
/// Turns word-timed ASR output into readable cues: hard breaks at speaker changes, long pauses and
/// sentence ends; length/duration limits resolved by picking the best break point in the cue
/// (after punctuation, before conjunctions, never inside "bir ev", "İstanbul'un sokakları", "3 kişi");
/// text wrapped into balanced lines; timing extended toward the reading-speed target without overlap.
/// </summary>
public static class Segmenter
{
    public static IReadOnlyList<Cue> Segment(IReadOnlyList<Word> words, SegmentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(words);
        options ??= SegmentationOptions.Default;
        var rules = new BreakRules(options.Language);
        var clean = words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).Select(w => w with { Text = w.Text.Trim() }).ToList();

        var groups = new List<List<Word>>();
        var current = new List<Word>();
        foreach (var word in clean)
        {
            if (current.Count > 0 && IsHardBreak(current[^1], word, current, options))
            {
                groups.Add(current);
                current = [];
            }

            current.Add(word);
            while (Exceeds(current, options.Rules))
            {
                var split = BestSplit(current, rules, options.Rules);
                groups.Add(current[..split]);
                current = current[split..];
            }
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        var cues = new List<Cue>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            cues.Add(new Cue(i + 1, group[0].Start, group[^1].End, WrapLines(group, rules, options.Rules))
            {
                Speaker = group[0].Speaker,
                Words = [.. group],
            });
        }

        AdjustTiming(cues, options);
        return cues;
    }

    /// <summary>Joins words into at most <see cref="ReadingRules.MaxLines"/> balanced lines.</summary>
    public static string WrapLines(IReadOnlyList<Word> words, string language = "tr", ReadingRules? readingRules = null)
        => WrapLines(words, new BreakRules(language), readingRules ?? ReadingRules.Default);

    private static bool IsHardBreak(Word previous, Word next, List<Word> current, SegmentationOptions options)
    {
        if (options.SplitOnSpeakerChange && next.Speaker is not null && previous.Speaker is not null && next.Speaker != previous.Speaker)
        {
            return true;
        }

        if (next.Start - previous.End > options.PauseThreshold)
        {
            return true;
        }

        // A sentence end closes the cue unless the cue would be uselessly short.
        return BreakRules.EndsSentence(previous.Text) && current[^1].End - current[0].Start >= options.Rules.MinDuration;
    }

    private static bool Exceeds(List<Word> words, ReadingRules rules)
    {
        if (words.Count < 2)
        {
            return false;
        }

        var chars = words.Sum(w => w.Text.Length) + words.Count - 1;
        return chars > rules.MaxCharsPerLine * rules.MaxLines || words[^1].End - words[0].Start > rules.MaxDuration;
    }

    /// <summary>Index of the first word of the second part (1..Count-1).</summary>
    private static int BestSplit(List<Word> words, BreakRules rules, ReadingRules reading)
    {
        var maxChars = reading.MaxCharsPerLine * reading.MaxLines;
        var best = words.Count - 1;
        var bestScore = double.MinValue;
        var leftChars = 0;

        for (var i = 1; i < words.Count; i++)
        {
            leftChars += words[i - 1].Text.Length + (i > 1 ? 1 : 0);
            var leftDuration = words[i - 1].End - words[0].Start;
            if (leftChars > maxChars || leftDuration > reading.MaxDuration)
            {
                break;
            }

            // Prefer meaningful breaks, then longer first parts (fewer, fuller cues).
            var score = rules.Score(words[i - 1].Text, words[i].Text) + (leftChars / (double)maxChars * 3);
            if (score >= bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return Math.Clamp(best, 1, words.Count - 1);
    }

    private static string WrapLines(IReadOnlyList<Word> words, BreakRules rules, ReadingRules reading)
    {
        var text = string.Join(' ', words.Select(w => w.Text));
        if (text.Length <= reading.MaxCharsPerLine || reading.MaxLines < 2 || words.Count < 2)
        {
            return text;
        }

        var bestIndex = -1;
        var bestScore = double.MinValue;
        for (var i = 1; i < words.Count; i++)
        {
            var top = string.Join(' ', words.Take(i).Select(w => w.Text));
            var bottom = string.Join(' ', words.Skip(i).Select(w => w.Text));
            var overflow = Math.Max(0, top.Length - reading.MaxCharsPerLine) + Math.Max(0, bottom.Length - reading.MaxCharsPerLine);

            // Balanced lines, a slightly shorter top line reads best ("pyramid"), good linguistic break.
            var balance = -Math.Abs(top.Length - bottom.Length) / 4.0;
            var pyramid = top.Length <= bottom.Length ? 1 : 0;
            var score = (rules.Score(words[i - 1].Text, words[i].Text) * 1.5) + balance + pyramid - (overflow * 10);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return string.Join(' ', words.Take(bestIndex).Select(w => w.Text)) + "\n" +
               string.Join(' ', words.Skip(bestIndex).Select(w => w.Text));
    }

    private static void AdjustTiming(List<Cue> cues, SegmentationOptions options)
    {
        var rules = options.Rules;
        for (var i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            var limit = i + 1 < cues.Count ? cues[i + 1].Start - options.MinGap : TimeSpan.MaxValue;
            if (limit < cue.End)
            {
                limit = cue.End; // never shorten what was spoken
            }

            // Duration needed to read the text at the target speed (+50 ms so rounding never lands a
            // hair above the target), at least the minimum duration.
            var readable = TimeSpan.FromSeconds(rules.TextLength(cue.Text) / rules.TargetCps) + TimeSpan.FromMilliseconds(50);
            var wanted = cue.Start + Max(rules.MinDuration, readable);
            if (wanted > cue.Start + rules.MaxDuration)
            {
                wanted = cue.Start + rules.MaxDuration;
            }

            if (wanted > cue.End)
            {
                cue.End = wanted < limit ? wanted : limit;
            }
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
