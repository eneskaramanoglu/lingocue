using System.Text;
using LingoCue.Core.Text;

namespace LingoCue.Core.Subtitles;

/// <summary>A whitespace-separated piece of cue text (tags kept) and when it starts being spoken.</summary>
/// <param name="Separator">Whitespace before the token (space or line break), written back as is.</param>
public sealed record TimedToken(string Separator, string Text, TimeSpan Start);

/// <summary>
/// Gives each word of a cue's (possibly corrected) text a start time from the ASR words kept on the
/// cue. Timing never comes from anywhere else: tokens are matched to ASR words by a sequence
/// alignment on normalized spelling, and tokens without a match share the time between their
/// matched neighbours by length.
/// </summary>
public static class KaraokeTiming
{
    private const double GapCost = 0.7;
    private const double MinSimilarity = 0.4;

    /// <summary>Returns null when the cue has no word timings or no token could be matched.</summary>
    public static IReadOnlyList<TimedToken>? Align(Cue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        var tokens = Tokenize(cue.Text);
        var words = cue.Words.Where(w => Normalize(w.Text).Length > 0).ToList();
        if (tokens.Count == 0 || words.Count == 0)
        {
            return null;
        }

        var tokenKeys = tokens.Select(t => Normalize(SubtitleText.StripTags(t.Text))).ToList();
        var wordKeys = words.Select(w => Normalize(w.Text)).ToList();
        var match = AlignSequences(tokenKeys, wordKeys);

        var starts = new TimeSpan?[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (match[i] is { } w)
            {
                starts[i] = Clamp(words[w].Start, cue);
            }
        }

        if (starts.All(s => s is null))
        {
            return null;
        }

        FillGaps(starts, tokenKeys, cue);

        var result = new List<TimedToken>(tokens.Count);
        var previous = cue.Start;
        for (var i = 0; i < tokens.Count; i++)
        {
            var start = starts[i]!.Value < previous ? previous : starts[i]!.Value; // keep order
            result.Add(new TimedToken(tokens[i].Separator, tokens[i].Text, start));
            previous = start;
        }

        return result;
    }

    /// <summary>Splits at spaces and line breaks, keeping each separator with the token after it.</summary>
    internal static List<(string Separator, string Text)> Tokenize(string text)
    {
        var tokens = new List<(string, string)>();
        var separator = new StringBuilder();
        var token = new StringBuilder();
        foreach (var c in SubtitleText.NormalizeLines(text))
        {
            if (c is ' ' or '\n' or '\t')
            {
                if (token.Length > 0)
                {
                    tokens.Add((separator.ToString(), token.ToString()));
                    separator.Clear();
                    token.Clear();
                }

                separator.Append(c);
            }
            else
            {
                token.Append(c);
            }
        }

        if (token.Length > 0)
        {
            tokens.Add((separator.ToString(), token.ToString()));
        }

        return tokens;
    }

    /// <summary>Turkish lower case, letters and digits only ("İstanbul'a," → "istanbula").</summary>
    internal static string Normalize(string text)
        => new([.. TurkishText.ToLower(text).Where(char.IsLetterOrDigit)]);

    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (prev, curr) = (curr, prev);
        }

        return 1 - ((double)prev[b.Length] / Math.Max(a.Length, b.Length));
    }

    /// <summary>Needleman–Wunsch style alignment; returns the matched word index per token.</summary>
    private static int?[] AlignSequences(List<string> tokens, List<string> words)
    {
        int n = tokens.Count, m = words.Count;
        var cost = new double[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
        {
            cost[i, 0] = i * GapCost;
        }

        for (var j = 0; j <= m; j++)
        {
            cost[0, j] = j * GapCost;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var sub = cost[i - 1, j - 1] + (1 - Similarity(tokens[i - 1], words[j - 1]));
                cost[i, j] = Math.Min(sub, Math.Min(cost[i - 1, j] + GapCost, cost[i, j - 1] + GapCost));
            }
        }

        var match = new int?[n];
        int a = n, b = m;
        while (a > 0 && b > 0)
        {
            var similarity = Similarity(tokens[a - 1], words[b - 1]);
            if (Math.Abs(cost[a, b] - (cost[a - 1, b - 1] + (1 - similarity))) < 1e-9)
            {
                if (similarity >= MinSimilarity)
                {
                    match[a - 1] = b - 1;
                }

                a--;
                b--;
            }
            else if (Math.Abs(cost[a, b] - (cost[a - 1, b] + GapCost)) < 1e-9)
            {
                a--;
            }
            else
            {
                b--;
            }
        }

        return match;
    }

    /// <summary>Unmatched tokens share the span up to the next known start, in proportion to their length.</summary>
    private static void FillGaps(TimeSpan?[] starts, List<string> keys, Cue cue)
    {
        var i = 0;
        while (i < starts.Length)
        {
            if (starts[i] is not null)
            {
                i++;
                continue;
            }

            // Run of unknown tokens [i, j); it starts where the previous token starts (or the cue) and
            // shares the time until the next known start (or the cue end) with that previous token.
            var j = i;
            while (j < starts.Length && starts[j] is null)
            {
                j++;
            }

            var from = i == 0 ? cue.Start : starts[i - 1]!.Value;
            var to = j < starts.Length ? starts[j]!.Value : cue.End;
            var first = i == 0 ? i : i - 1;
            var weights = Enumerable.Range(first, j - first).Select(k => Math.Max(1, keys[k].Length)).ToList();
            var total = weights.Sum();
            var acc = 0;
            for (var k = first; k < j; k++)
            {
                if (k >= i)
                {
                    starts[k] = from + ((to - from) * acc / total);
                }

                acc += weights[k - first];
            }

            i = j;
        }
    }

    private static TimeSpan Clamp(TimeSpan t, Cue cue) => t < cue.Start ? cue.Start : t > cue.End ? cue.End : t;
}
