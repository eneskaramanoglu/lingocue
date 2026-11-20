using LingoCue.Core.Projects;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;
using Microsoft.Extensions.AI;

namespace LingoCue.Llm.Tests;

public class ResilientChatTests
{
    /// <summary>Hangs (until cancelled) for the first <paramref name="stalls"/> calls, then answers.</summary>
    private sealed class StallingClient(int stalls, string answer) : IChatClient
    {
        public int Calls { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (Calls++ < stalls)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static readonly PipelineOptions s_fast = new()
    {
        RequestTimeout = TimeSpan.FromMilliseconds(100),
        TransientDelays = [TimeSpan.Zero, TimeSpan.Zero],
    };

    private const string ContextJson = """{ "summary": "Özet.", "tone": "samimi", "address": "siz", "speakers": [], "terms": [] }""";

    [Fact]
    public async Task A_stalled_call_times_out_and_is_retried()
    {
        var client = new StallingClient(stalls: 1, ContextJson);
        var extractor = new ContextExtractor(client) { Policy = s_fast, Delay = (_, _) => Task.CompletedTask };

        var result = await extractor.ExtractAsync([new LineItem(1, "merhaba", TimeSpan.FromSeconds(1))], "tr", [], TestContext.Current.CancellationToken);

        result.Context.Summary.ShouldBe("Özet.");
        client.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Giving_up_explains_what_to_do_in_turkish()
    {
        var client = new StallingClient(stalls: int.MaxValue, ContextJson);
        var extractor = new ContextExtractor(client) { Policy = s_fast, Delay = (_, _) => Task.CompletedTask };

        var ex = await Should.ThrowAsync<LlmException>(() =>
            extractor.ExtractAsync([new LineItem(1, "merhaba", TimeSpan.FromSeconds(1))], "tr", [], TestContext.Current.CancellationToken));

        ex.Remedy!.ShouldContain("zamanında yanıt vermedi");
        client.Calls.ShouldBe(3); // first try + two retries
    }

    [Fact]
    public async Task The_users_cancel_is_not_mistaken_for_a_timeout()
    {
        var client = new StallingClient(stalls: int.MaxValue, ContextJson);
        var extractor = new ContextExtractor(client) { Policy = s_fast with { RequestTimeout = TimeSpan.FromMinutes(5) } };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(100);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            extractor.ExtractAsync([new LineItem(1, "merhaba", TimeSpan.FromSeconds(1))], "tr", [], cts.Token));
        client.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Line_batches_use_the_same_timeout()
    {
        var inner = new FakeChatClient((m, _, _) => FakeChatClient.Echo(m, (id, t) => t + "!"));
        var client = new StallingWrapper(inner, stalls: 1);
        var runner = new LineBatchRunner(client, Microsoft.Extensions.Logging.Abstractions.NullLogger<LineBatchRunner>.Instance) { Delay = (_, _) => Task.CompletedTask };

        var result = await runner.RunAsync(new CorrectionTask(keepCircumflex: false), [new LineItem(1, "merhaba", TimeSpan.FromSeconds(2))],
            new TaskContext(new ProjectContext(), [], "tr", null), s_fast with { ShortenOverlong = false }, cancellationToken: TestContext.Current.CancellationToken);

        result.Lines.ShouldHaveSingleItem().Text.ShouldBe("merhaba!");
    }

    private sealed class StallingWrapper(IChatClient inner, int stalls) : IChatClient
    {
        private int _calls;

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (_calls++ < stalls)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return await inner.GetResponseAsync(messages, options, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => inner.Dispose();
    }
}
