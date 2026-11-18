using System.Globalization;

namespace LingoCue.Media.Audio;

/// <summary>
/// Parses the key=value stream FFmpeg writes with <c>-progress pipe:1</c>
/// (<c>out_time_us=1234567</c>, …, <c>progress=continue|end</c>).
/// </summary>
public sealed class FfmpegProgress(TimeSpan total, IProgress<double>? sink)
{
    /// <summary>Feeds one stdout line; reports a 0..1 fraction when a time value arrives.</summary>
    public void OnLine(string line)
    {
        if (sink is null || string.IsNullOrEmpty(line))
        {
            return;
        }

        if (line == "progress=end")
        {
            sink.Report(1);
            return;
        }

        if (total <= TimeSpan.Zero || TryReadPosition(line) is not { } position)
        {
            return;
        }

        sink.Report(Math.Clamp(position.TotalMilliseconds / total.TotalMilliseconds, 0, 1));
    }

    internal static TimeSpan? TryReadPosition(string line)
    {
        // out_time_us and out_time_ms are both microseconds (a long-standing FFmpeg quirk).
        foreach (var key in new[] { "out_time_us=", "out_time_ms=" })
        {
            if (line.StartsWith(key, StringComparison.Ordinal) &&
                long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) &&
                us >= 0)
            {
                return TimeSpan.FromMicroseconds(us);
            }
        }

        return null;
    }
}
