using System.Text;
using System.Text.RegularExpressions;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Formats;

/// <summary>
/// Converts inline formatting between HTML-like tags (SRT/VTT) and ASS override blocks.
/// libass draws HTML tags literally, so text must be converted before it reaches an ASS file;
/// SRT readers in turn don't understand <c>{\i1}</c>.
/// </summary>
public static partial class TagConverter
{
    /// <summary><c>&lt;i&gt;</c>, <c>&lt;b&gt;</c>, <c>&lt;u&gt;</c>, <c>&lt;s&gt;</c>, <c>&lt;font color&gt;</c> → ASS overrides; other HTML tags are dropped.</summary>
    public static string HtmlToAss(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains('<', StringComparison.Ordinal))
        {
            return text;
        }

        return HtmlTag().Replace(text, m =>
        {
            var closing = m.Groups["close"].Success;
            var name = m.Groups["name"].Value.ToLowerInvariant();
            return name switch
            {
                "i" => closing ? @"{\i0}" : @"{\i1}",
                "b" => closing ? @"{\b0}" : @"{\b1}",
                "u" => closing ? @"{\u0}" : @"{\u1}",
                "s" => closing ? @"{\s0}" : @"{\s1}",
                "font" when closing => @"{\c}",
                "font" => FontColor(m.Groups["attrs"].Value) is { } c ? $@"{{\c{c.ToAssOverride()}}}" : string.Empty,
                _ => string.Empty,
            };
        });
    }

    /// <summary>ASS <c>\i \b \u \s \c</c> → HTML-like tags; every other override block is removed.</summary>
    public static string AssToHtml(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains("{\\", StringComparison.Ordinal))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var open = new HashSet<char>();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '{' && i + 1 < text.Length && text[i + 1] == '\\')
            {
                var end = text.IndexOf('}', i);
                if (end < 0)
                {
                    break;
                }

                foreach (Match tag in AssTag().Matches(text[(i + 1)..end]))
                {
                    var t = tag.Groups["tag"].Value;
                    var on = tag.Groups["value"].Value != "0";
                    if (t.Length == 1 && "ibus".Contains(t[0], StringComparison.Ordinal))
                    {
                        var c = t[0];
                        if (on && open.Add(c))
                        {
                            sb.Append('<').Append(c).Append('>');
                        }
                        else if (!on && open.Remove(c))
                        {
                            sb.Append("</").Append(c).Append('>');
                        }
                    }
                }

                i = end + 1;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        // Close anything left open, innermost first is not tracked: order i, b, u, s is fine for subtitles.
        foreach (var c in "sbui")
        {
            if (open.Contains(c))
            {
                sb.Append("</").Append(c).Append('>');
            }
        }

        return sb.ToString();
    }

    private static AssColor? FontColor(string attributes)
    {
        var m = ColorAttribute().Match(attributes);
        if (!m.Success)
        {
            return null;
        }

        var hex = m.Groups["hex"].Value;
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        }

        var rgb = Convert.FromHexString(hex);
        return new AssColor(rgb[0], rgb[1], rgb[2]);
    }

    [GeneratedRegex(@"<(?<close>/)?(?<name>[a-zA-Z]+)(?<attrs>[^>]*)>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\\(?<tag>[a-z]+)(?<value>\d*)")]
    private static partial Regex AssTag();

    [GeneratedRegex(@"color\s*=\s*[""']?#(?<hex>[0-9a-fA-F]{6}|[0-9a-fA-F]{3})")]
    private static partial Regex ColorAttribute();
}
