using System.Globalization;

namespace LingoCue.Core.Time;

public static class Timecode
{
    /// <summary>
    /// Formats as <c>hh:mm:ss.fff</c> (hours always shown, as in subtitle editors). Negative values clamp to zero.
    /// Milliseconds are truncated, never rounded up, so a position never displays past the duration.
    /// </summary>
    public static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var totalMs = (long)Math.Floor(value.TotalMilliseconds);
        var hours = totalMs / 3_600_000;
        var minutes = totalMs / 60_000 % 60;
        var seconds = totalMs / 1000 % 60;
        var ms = totalMs % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}.{ms:000}");
    }
}
