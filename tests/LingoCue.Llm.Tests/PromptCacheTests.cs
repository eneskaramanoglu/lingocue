using LingoCue.Core.Projects;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Llm.Tests;

/// <summary>
/// Gemini caches a prompt prefix it has seen recently and bills those tokens at a discount. That only
/// works when every request of a run begins with exactly the same text.
/// </summary>
public class PromptCacheTests
{
    private static List<LineItem> Lines(int count)
        => [.. Enumerable.Range(1, count).Select(i => new LineItem(i, $"satır {i}", TimeSpan.FromSeconds(3)))];

    [Fact]
    public async Task Every_batch_starts_with_the_same_system_prompt_and_cached_tokens_are_counted()
    {
        var client = new FakeChatClient((m, _, _) =>
        {
            var response = FakeChatClient.Echo(m, (_, t) => t + " (en)");
            response.Usage = new UsageDetails { InputTokenCount = 3000, OutputTokenCount = 200, CachedInputTokenCount = 2048 };
            return response;
        });
        var context = new TaskContext(new ProjectContext { Summary = "Gezi." }, [], "tr", "en");

        var result = await new LineBatchRunner(client, NullLogger<LineBatchRunner>.Instance).RunAsync(new TranslationTask(), Lines(9), context,
            new PipelineOptions { BatchSize = 3, Parallelism = 1, ShortenOverlong = false }, cancellationToken: TestContext.Current.CancellationToken);

        client.Calls.Count.ShouldBe(3);
        client.Calls.Select(c => c.Messages[0]).ShouldAllBe(m => m.Role == ChatRole.System);
        client.Calls.Select(c => c.Messages[0].Text).Distinct().ShouldHaveSingleItem(); // a stable prefix
        result.Usage.CachedInputTokens.ShouldBe(3 * 2048);
        result.Usage.InputTokens.ShouldBe(3 * 3000);
    }

    [Fact]
    public void Cached_input_is_billed_at_a_quarter_of_the_price()
    {
        var price = new LlmPrice(InputPerMillion: 1m, OutputPerMillion: 4m, HasFreeTier: true);
        var usage = new LlmUsage(InputTokens: 1_000_000, OutputTokens: 100_000, Requests: 10, CachedInputTokens: 400_000);

        price.Cost(usage).ShouldBe(0.6m + 0.1m + 0.4m); // 600k full + 400k at 25 % + output
        price.CacheSavings(usage).ShouldBe(0.3m);
        price.Cost(new LlmUsage(1_000_000, 0, 1)).ShouldBe(1m); // no cache, no discount
    }

    [Fact]
    public void Usage_sums_cached_tokens()
        => new LlmUsage(10, 5, 1, 4).Add(new LlmUsage(20, 5, 1, 6)).ShouldBe(new LlmUsage(30, 10, 2, 10));
}
