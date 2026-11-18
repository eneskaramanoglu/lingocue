using System.Globalization;

namespace LingoCue.Core.Subtitles;

/// <summary>
/// RGBA color with ASS conversions. ASS writes <c>&amp;HAABBGGRR</c> where alpha is inverted
/// (00 = opaque, FF = fully transparent).
/// </summary>
public readonly record struct AssColor(byte R, byte G, byte B, byte A = 255)
{
    public static AssColor White { get; } = new(255, 255, 255);

    public static AssColor Black { get; } = new(0, 0, 0);

    public static AssColor Yellow { get; } = new(255, 255, 0);

    /// <summary>Formats as <c>&amp;HAABBGGRR</c> (style lines).</summary>
    public string ToAssStyle()
        => string.Create(CultureInfo.InvariantCulture, $"&H{255 - A:X2}{B:X2}{G:X2}{R:X2}");

    /// <summary>Formats as <c>&amp;HBBGGRR&amp;</c> (override tags such as <c>\c</c>).</summary>
    public string ToAssOverride()
        => string.Create(CultureInfo.InvariantCulture, $"&H{B:X2}{G:X2}{R:X2}&");

    public string ToHex() => A == 255
        ? string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}{A:X2}");

    /// <summary>Parses <c>&amp;HAABBGGRR</c>, <c>&amp;HBBGGRR</c>, <c>&amp;HBBGGRR&amp;</c> or a decimal value (old SSA).</summary>
    public static AssColor ParseAss(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var s = value.Trim().TrimEnd('&');
        uint raw;
        if (s.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            raw = uint.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        else if (s.StartsWith("H", StringComparison.OrdinalIgnoreCase))
        {
            raw = uint.Parse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        else
        {
            raw = unchecked((uint)long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
        }

        var r = (byte)(raw & 0xFF);
        var g = (byte)((raw >> 8) & 0xFF);
        var b = (byte)((raw >> 16) & 0xFF);
        var assAlpha = (byte)((raw >> 24) & 0xFF);
        return new AssColor(r, g, b, (byte)(255 - assAlpha));
    }

    public static bool TryParseAss(string value, out AssColor color)
    {
        try
        {
            color = ParseAss(value);
            return true;
        }
        catch (FormatException)
        {
        }
        catch (OverflowException)
        {
        }

        color = White;
        return false;
    }
}
