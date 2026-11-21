using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LingoCue.Llm.Pipeline;

/// <summary>
/// Spaces model requests at least <see cref="Interval"/> apart across all batches of a run, so a free
/// tier's requests-per-minute limit is not hit in the first place (one pacer per run).
/// </summary>
public sealed class RequestPacer
{
    private readonly Func<TimeSpan> _clock;
    private readonly Lock _gate = new();
    private TimeSpan? _next;

    /// <param name="clock">Elapsed time source (tests pass a fake one).</param>
    public RequestPacer(TimeSpan interval, Func<TimeSpan>? clock = null)
    {
        Interval = interval;
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed;
        }

        _clock = clock;
    }

    public TimeSpan Interval { get; }

    /// <summary>For <paramref name="requestsPerMinute"/> ≤ 0 there is no pacing.</summary>
    public static RequestPacer? ForRate(int requestsPerMinute)
        => requestsPerMinute > 0 ? new RequestPacer(TimeSpan.FromMinutes(1.0 / requestsPerMinute)) : null;

    /// <summary>Waits until this request's slot; slots are handed out in call order.</summary>
    public async Task WaitAsync(Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delay);
        TimeSpan wait;
        lock (_gate)
        {
            var now = _clock();
            var slot = _next is { } next && next > now ? next : now;
            _next = slot + Interval;
            wait = slot - now;
        }

        if (wait > TimeSpan.Zero)
        {
            await delay(wait, cancellationToken);
        }
    }
}

/// <summary>Reads how long a rate-limited API asked to wait ("Please retry in 37.2s", "retryDelay": "37s").</summary>
public static partial class RetryHint
{
    public static TimeSpan? From(Exception? exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            var match = Pattern().Match(ex.Message);
            if (match.Success && double.TryParse(match.Groups["s"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return null;
    }

    // "retry in 37.2s" in the message, or "retryDelay": "37s" in the JSON error body (\W* skips the quotes and colon).
    [GeneratedRegex(@"(?:retry in\s*|retryDelay\W*)(?<s>\d+(?:\.\d+)?)\s*s", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
