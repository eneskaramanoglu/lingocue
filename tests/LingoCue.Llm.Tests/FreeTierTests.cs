using LingoCue.Core.Projects;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Llm.Tests;

/// <summary>Behaviour that keeps runs alive on free tiers: retry hints, pacing, and batches that never answer.</summary>
public class FreeTierTests
{
    private static readonly TaskContext s_context = new(new ProjectContext(), [], "tr", "en");

    private static List<LineItem> Lines(int count)
        => [.. Enumerable.Range(1, count).Select(i => new LineItem(i, $"satır {i}", TimeSpan.FromSeconds(3)))];

    private static (LineBatchRunner Runner, List<TimeSpan> Delays) Runner(IChatClient client)
    {
        var delays = new List<TimeSpan>();
        var runner = new LineBatchRunner(client, NullLogger<LineBatchRunner>.Instance)
        {
            Delay = (d, _) =>
            {
                lock (delays)
                {
                    delays.Add(d);
                }

                return Task.CompletedTask;
            },
        };
        return (runner, delays);
    }

    [Theory]
    [InlineData("Quota exceeded. Please retry in 37.2s.", 37.2)]
    [InlineData("""{"error":{"details":[{"retryDelay": "21s"}]}}""", 21.0)]
    [InlineData("Service unavailable", null)]
    public void Reads_the_retry_hint(string message, double? seconds)
        => RetryHint.From(new InvalidOperationException("outer", new InvalidOperationException(message)))
            .ShouldBe(seconds is { } s ? TimeSpan.FromSeconds(s) : null);

    [Fact]
    public async Task Waits_as_long_as_a_rate_limit_asks()
    {
        var calls = 0;
        var client = new FakeChatClient((m, _, _) => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException("429 RESOURCE_EXHAUSTED. Please retry in 30s.")
            : FakeChatClient.Echo(m, (_, t) => t + " (en)"));
        var (runner, delays) = Runner(client);

        await runner.RunAsync(new TranslationTask(), Lines(2), s_context, new PipelineOptions { BatchSize = 2, ShortenOverlong = false },
            cancellationToken: TestContext.Current.CancellationToken);

        delays.ShouldContain(TimeSpan.FromSeconds(30)); // not the scheduled 2 s
    }

    [Fact]
    public async Task Pacer_spaces_requests_by_the_interval()
    {
        var now = TimeSpan.Zero;
        var pacer = new RequestPacer(TimeSpan.FromSeconds(6), () => now);
        var waits = new List<TimeSpan>();
        Task Delay(TimeSpan d, CancellationToken _)
        {
            waits.Add(d);
            return Task.CompletedTask;
        }

        await pacer.WaitAsync(Delay, TestContext.Current.CancellationToken);
        await pacer.WaitAsync(Delay, TestContext.Current.CancellationToken);
        now = TimeSpan.FromSeconds(20);
        await pacer.WaitAsync(Delay, TestContext.Current.CancellationToken);

        waits.ShouldBe([TimeSpan.FromSeconds(6)]); // only the second call waited; the third came late enough
        RequestPacer.ForRate(10)!.Interval.ShouldBe(TimeSpan.FromSeconds(6));
        RequestPacer.ForRate(0).ShouldBeNull();
    }

    [Fact]
    public async Task An_unanswered_batch_is_split_and_the_run_keeps_its_other_lines()
    {
        // Line 3 never gets an answer; everything else does. The run must finish with only line 3 failed.
        var client = new FakeChatClient((m, _, _) =>
        {
            var ids = FakeChatClient.RequestedLines(m).Select(l => l.Id).ToList();
            return ids.Contains(3)
                ? throw new InvalidOperationException("503 UNAVAILABLE high demand")
                : FakeChatClient.Echo(m, (_, t) => t + " (en)");
        });
        var (runner, _) = Runner(client);
        var options = new PipelineOptions { BatchSize = 4, Parallelism = 1, ShortenOverlong = false, TransientDelays = [TimeSpan.Zero] };

        var result = await runner.RunAsync(new TranslationTask(), Lines(6), s_context, options, cancellationToken: TestContext.Current.CancellationToken);

        result.Lines.Single(l => l.Id == 3).Status.ShouldBe(LineStatus.Failed);
        result.Lines.Where(l => l.Id != 3).ShouldAllBe(l => l.Status == LineStatus.Changed);
        result.Lines.Single(l => l.Id == 4).Text.ShouldBe("satır 4 (en)");
    }

    [Fact]
    public async Task A_model_that_never_answers_is_given_up_quickly()
    {
        var client = new FakeChatClient((_, _, _) => throw new InvalidOperationException("503 UNAVAILABLE"));
        var (runner, _) = Runner(client);
        var options = new PipelineOptions { BatchSize = 1, Parallelism = 1, ShortenOverlong = false, TransientDelays = [] };

        var result = await runner.RunAsync(new TranslationTask(), Lines(10), s_context, options, cancellationToken: TestContext.Current.CancellationToken);

        result.Lines.ShouldAllBe(l => l.Status == LineStatus.Failed);
        client.CallCount.ShouldBe(LineBatchRunner.MaxConsecutiveFailures); // the rest are skipped without a call
    }

    [Fact]
    public async Task A_bad_key_still_stops_the_run()
    {
        // Not a timeout or rate limit: retrying cannot help, so the error reaches the user right away.
        var client = new FakeChatClient((_, _, _) => throw new Google.GenAI.ClientError("API key not valid", 400, "INVALID_ARGUMENT", null!));
        var (runner, _) = Runner(client);

        await Should.ThrowAsync<Google.GenAI.ClientError>(() => runner.RunAsync(new TranslationTask(), Lines(2), s_context,
            new PipelineOptions { BatchSize = 2, ShortenOverlong = false }, cancellationToken: TestContext.Current.CancellationToken));
    }
}
