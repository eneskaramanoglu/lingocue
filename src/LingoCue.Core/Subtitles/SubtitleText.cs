using System.Text;

namespace LingoCue.Core.Subtitles;

/// <summary>Helpers for the text of a cue (shared by all formats).</summary>
public static class SubtitleText
{
    /// <summary>Normalizes line endings to <c>\n</c> and trims trailing whitespace on each line.</summary>
    public static string NormalizeLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return string.Join('\n', lines.Select(l => l.TrimEnd())).Trim('\n');
    }

    /// <summary>Removes ASS override blocks (<c>{\b1}</c>) and simple HTML tags (<c>&lt;i&gt;</c>).</summary>
    public static string StripTags(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        var depthCurly = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                depthCurly++;
                continue;
            }

            if (depthCurly > 0)
            {
                if (c == '}')
                {
                    depthCurly--;
                }

                continue;
            }

            if (c == '<')
            {
                var close = text.IndexOf('>', i);
                if (close > i && IsSimpleTag(text.AsSpan(i + 1, close - i - 1)))
                {
                    i = close;
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Applies <paramref name="map"/> to the visible runs of <paramref name="text"/>, leaving ASS
    /// override blocks and simple HTML tags untouched (e.g. upper-casing without breaking <c>{\i1}</c>).
    /// </summary>
    public static string MapVisible(string text, Func<string, string> map)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(map);
        var sb = new StringBuilder(text.Length);
        var run = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var tagEnd = TagEndAt(text, i);
            if (tagEnd < 0)
            {
                run.Append(text[i]);
                i++;
                continue;
            }

            Flush();
            sb.Append(text, i, tagEnd - i + 1);
            i = tagEnd + 1;
        }

        Flush();
        return sb.ToString();

        void Flush()
        {
            if (run.Length > 0)
            {
                sb.Append(map(run.ToString()));
                run.Clear();
            }
        }
    }

    /// <summary>Index of the closing character when a tag starts at <paramref name="i"/>; otherwise -1.</summary>
    private static int TagEndAt(string text, int i)
    {
        if (text[i] == '{' && i + 1 < text.Length && text[i + 1] == '\\')
        {
            var close = text.IndexOf('}', i);
            return close < 0 ? text.Length - 1 : close;
        }

        if (text[i] == '<')
        {
            var close = text.IndexOf('>', i);
            if (close > i && IsSimpleTag(text.AsSpan(i + 1, close - i - 1)))
            {
                return close;
            }
        }

        return -1;
    }

    /// <summary>Characters a viewer reads: no tags, no line breaks.</summary>
    public static int VisibleLength(string text)
        => StripTags(text).Count(c => c is not ('\n' or '\r'));

    /// <summary>
    /// Length of one plain line (no tags, no line breaks). With <see cref="LengthCounting.HalfWidthLatin"/>
    /// ASCII letters, digits, spaces and punctuation count half.
    /// </summary>
    public static double WeightedLength(string line, LengthCounting counting)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (counting == LengthCounting.Characters)
        {
            return line.Length;
        }

        var length = 0.0;
        foreach (var c in line)
        {
            length += c < 0x80 ? 0.5 : 1;
        }

        return length;
    }

    /// <summary>Length of the longest visible line.</summary>
    public static int LongestLineLength(string text)
        => StripTags(text).Split('\n').Max(l => l.Length);

    public static int LineCount(string text) => text.Split('\n').Length;

    // <i>, </i>, <b>, <u>, <font color="...">, </font>, <c.yellow>, <v Speaker>
    private static bool IsSimpleTag(ReadOnlySpan<char> inner)
    {
        if (inner.IsEmpty)
        {
            return false;
        }

        var name = inner[0] == '/' ? inner[1..] : inner;
        if (name.IsEmpty || !char.IsLetter(name[0]))
        {
            return false;
        }

        var end = 0;
        while (end < name.Length && (char.IsLetterOrDigit(name[end]) || name[end] == '.'))
        {
            end++;
        }

        var tag = name[..end].ToString().ToLowerInvariant();
        var baseName = tag.Split('.')[0];
        return baseName is "i" or "b" or "u" or "s" or "font" or "c" or "v" or "lang" or "ruby" or "rt";
    }
}
