using System.Globalization;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Text;

/// <param name="Index">Position in the cue's model text.</param>
public sealed record TextMatch(Cue Cue, int Index, int Length);

/// <summary>
/// Find and replace in a track's cue texts. Matching ignores case with Turkish rules (I/ı and İ/i are
/// different letters; "istanbul" finds "İSTANBUL" but not "ISTANBUL"), never across tags.
/// </summary>
public static class TextSearch
{
    private static readonly CompareInfo s_compare = TurkishText.Culture.CompareInfo;

    public static IReadOnlyList<TextMatch> FindAll(IEnumerable<Cue> cues, string query, bool matchCase = false, bool wholeWord = false)
    {
        ArgumentNullException.ThrowIfNull(cues);
        if (string.IsNullOrEmpty(query))
        {
            return [];
        }

        var options = matchCase ? CompareOptions.Ordinal : CompareOptions.IgnoreCase;
        var matches = new List<TextMatch>();
        foreach (var cue in cues)
        {
            var text = cue.Text;
            var start = 0;
            while (start <= text.Length - 1)
            {
                var index = s_compare.IndexOf(text, query, start, options);
                if (index < 0)
                {
                    break;
                }

                if (!wholeWord || IsWholeWord(text, index, query.Length))
                {
                    matches.Add(new TextMatch(cue, index, query.Length));
                }

                start = index + Math.Max(1, query.Length);
            }
        }

        return matches;
    }

    /// <summary>Replaces one match (the text must not have changed since it was found).</summary>
    public static void Replace(TextMatch match, string replacement)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(replacement);
        var text = match.Cue.Text;
        match.Cue.Text = string.Concat(text.AsSpan(0, match.Index), replacement, text.AsSpan(match.Index + match.Length));
    }

    /// <summary>Replaces every match; returns how many were replaced.</summary>
    public static int ReplaceAll(IEnumerable<Cue> cues, string query, string replacement, bool matchCase = false, bool wholeWord = false)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var matches = FindAll(cues, query, matchCase, wholeWord);

        // Back to front so earlier indexes in the same cue stay valid.
        foreach (var match in matches.OrderByDescending(m => m.Index))
        {
            Replace(match, replacement);
        }

        return matches.Count;
    }

    private static bool IsWholeWord(string text, int index, int length)
    {
        var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        var end = index + length;
        var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
        return before && after;
    }
}
