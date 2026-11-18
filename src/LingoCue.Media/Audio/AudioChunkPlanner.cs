using System.Globalization;
using System.Text.RegularExpressions;

namespace LingoCue.Media.Audio;

/// <summary>A silent interval found by FFmpeg's <c>silencedetect</c>.</summary>
public readonly record struct SilenceInterval(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Middle => Start + ((End - Start) / 2);
}

/// <summary>A slice of the source audio to transcribe separately; word times are offset by <see cref="Start"/>.</summary>
public readonly record struct AudioChunk(int Index, TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Splits long audio into chunks no longer than a maximum, cutting in the middle of a silence so
/// no word is cut in half. Cloud ASR limits a request (Gemini: 30 min with timestamps).
/// </summary>
public static partial class AudioChunkPlanner
{
    /// <summary>A cut is only searched in the last part of a chunk, so chunks stay reasonably long.</summary>
    private const double SearchWindowFraction = 0.25;

    public static IReadOnlyList<AudioChunk> Plan(TimeSpan total, TimeSpan maxChunk, IReadOnlyList<SilenceInterval> silences)
    {
        ArgumentNullException.ThrowIfNull(silences);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxChunk, TimeSpan.Zero);

        var chunks = new List<AudioChunk>();
        var start = TimeSpan.Zero;
        while (total - start > maxChunk)
        {
            var limit = start + maxChunk;
            var windowStart = limit - (maxChunk * SearchWindowFraction);

            // Latest silence whose middle falls inside the search window; otherwise a hard cut.
            var cut = silences
                .Where(s => s.Middle > windowStart && s.Middle <= limit)
                .Select(s => (TimeSpan?)s.Middle)
                .LastOrDefault() ?? limit;

            chunks.Add(new AudioChunk(chunks.Count, start, cut));
            start = cut;
        }

        if (total > start || chunks.Count == 0)
        {
            chunks.Add(new AudioChunk(chunks.Count, start, total));
        }

        return chunks;
    }

    /// <summary>Reads <c>silence_start: x</c> / <c>silence_end: y</c> pairs from FFmpeg's stderr.</summary>
    public static IReadOnlyList<SilenceInterval> ParseSilenceDetect(string stderr, TimeSpan total)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        var result = new List<SilenceInterval>();
        TimeSpan? open = null;

        foreach (Match m in SilenceLine().Matches(stderr))
        {
            var seconds = double.Parse(m.Groups["t"].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            var at = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (m.Groups["kind"].Value == "start")
            {
                open = at;
            }
            else if (open is { } s)
            {
                result.Add(new SilenceInterval(s, at));
                open = null;
            }
        }

        // Silence running until the end of the file has no silence_end line.
        if (open is { } tail && total > tail)
        {
            result.Add(new SilenceInterval(tail, total));
        }

        return result;
    }

    [GeneratedRegex(@"silence_(?<kind>start|end):\s*(?<t>-?[0-9.]+)")]
    private static partial Regex SilenceLine();
}
