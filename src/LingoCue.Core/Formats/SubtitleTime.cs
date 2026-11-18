using System.Globalization;
using System.Text.RegularExpressions;

namespace LingoCue.Core.Formats;

/// <summary>Timestamp parsing/formatting for SRT, WebVTT and ASS.</summary>
public static partial class SubtitleTime
{
    /// <summary>
    /// Parses SRT/VTT style times leniently: <c>01:02:03,456</c>, <c>01:02:03.456</c>, <c>02:03.456</c> (VTT, no hours),
    /// 1–3 fraction digits (<c>,5</c> = 500 ms) and the occasional <c>:</c> before milliseconds.
    /// </summary>
    public static bool TryParseSrtOrVtt(ReadOnlySpan<char> text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var match = SrtVttTime().Match(text.Trim().ToString());
        if (!match.Success)
        {
            return false;
        }

        var hours = match.Groups["h"].Success ? int.Parse(match.Groups["h"].ValueSpan, CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(match.Groups["m"].ValueSpan, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups["s"].ValueSpan, CultureInfo.InvariantCulture);
        var fraction = match.Groups["f"].Success ? match.Groups["f"].Value : "0";
        if (minutes > 59 || seconds > 59)
        {
            return false;
        }

        var ms = int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        value = new TimeSpan(0, hours, minutes, seconds, ms);
        return true;
    }

    /// <summary><c>hh:mm:ss,fff</c>.</summary>
    public static string FormatSrt(TimeSpan value) => Format(value, ',');

    /// <summary><c>hh:mm:ss.fff</c>.</summary>
    public static string FormatVtt(TimeSpan value) => Format(value, '.');

    /// <summary>Parses ASS <c>h:mm:ss.cc</c> (centiseconds; also accepts more digits).</summary>
    public static bool TryParseAss(ReadOnlySpan<char> text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var match = AssTime().Match(text.Trim().ToString());
        if (!match.Success)
        {
            return false;
        }

        var hours = int.Parse(match.Groups["h"].ValueSpan, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["m"].ValueSpan, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups["s"].ValueSpan, CultureInfo.InvariantCulture);
        var fraction = match.Groups["f"].Success ? match.Groups["f"].Value : "0";
        var ms = int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        value = new TimeSpan(0, hours, minutes, seconds, ms);
        return true;
    }

    /// <summary>
    /// <c>h:mm:ss.cc</c>. ASS only has centisecond precision, so milliseconds are rounded to the nearest
    /// 10 ms (half away from zero); a round-trip can move a time by up to 5 ms.
    /// </summary>
    public static string FormatAss(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var totalCs = (long)Math.Round(value.TotalMilliseconds / 10d, MidpointRounding.AwayFromZero);
        var hours = totalCs / 360_000;
        var minutes = totalCs / 6_000 % 60;
        var seconds = totalCs / 100 % 60;
        var cs = totalCs % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}.{cs:00}");
    }

    /// <summary>Rounds to whole milliseconds (all formats store ms).</summary>
    public static TimeSpan RoundToMilliseconds(TimeSpan value)
        => TimeSpan.FromMilliseconds(Math.Round(value.TotalMilliseconds, MidpointRounding.AwayFromZero));

    private static string Format(TimeSpan value, char msSeparator)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var totalMs = (long)Math.Round(value.TotalMilliseconds, MidpointRounding.AwayFromZero);
        var hours = totalMs / 3_600_000;
        var minutes = totalMs / 60_000 % 60;
        var seconds = totalMs / 1000 % 60;
        var ms = totalMs % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}{msSeparator}{ms:000}");
    }

    [GeneratedRegex(@"^(?:(?<h>\d{1,3}):)?(?<m>\d{1,2}):(?<s>\d{1,2})(?:[,.:](?<f>\d{1,3}))?$")]
    private static partial Regex SrtVttTime();

    [GeneratedRegex(@"^(?<h>\d{1,2}):(?<m>\d{1,2}):(?<s>\d{1,2})(?:[.,](?<f>\d{1,3}))?$")]
    private static partial Regex AssTime();
}
