using System.Net;
using System.Text;
using System.Text.Json;
using LingoCue.Core.Settings;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;
using Microsoft.Extensions.AI;

namespace LingoCue.Llm.Tests;

public class OllamaProviderTests
{
    /// <summary>Answers Ollama API paths with canned JSON and records the requests.</summary>
    private sealed class FakeOllama(Func<string, string, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            var (status, json) = answer(request.RequestUri.AbsolutePath, body);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private const string Tags = """{"models":[{"name":"gemma3:12b","size":8100000000,"details":{"parameter_size":"12.2B"}},{"name":"qwen3:8b","size":5200000000,"details":{"parameter_size":"8.2B"}}]}""";

    private static (OllamaProvider Provider, FakeOllama Handler) Create(Func<string, string, (HttpStatusCode, string)> answer)
    {
        var handler = new FakeOllama(answer);
        return (new OllamaProvider(new HttpClient(handler), () => "http://localhost:11434"), handler);
    }

    [Fact]
    public async Task Lists_installed_models()
    {
        var (provider, handler) = Create((_, _) => (HttpStatusCode.OK, Tags));

        var models = await provider.ListModelsAsync(TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/tags");
        models.Select(m => m.Id).ShouldBe(["gemma3:12b", "qwen3:8b"]);
        models[0].DisplayName.ShouldContain("12.2B");
        provider.PriceFor("gemma3:12b")!.Cost(1_000_000, 1_000_000).ShouldBe(0);
    }

    [Fact]
    public async Task Chat_sends_the_json_schema_and_context_size_and_reads_usage()
    {
        var (provider, handler) = Create((_, _) => (HttpStatusCode.OK,
            """{"message":{"role":"assistant","content":"{\"lines\":[]}"},"prompt_eval_count":120,"eval_count":30,"done":true}"""));
        using var client = provider.CreateChatClient("gemma3:12b");
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"lines":{"type":"array"}}}""").RootElement;

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "sys"), new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { Temperature = 0.2f, ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "subtitle_lines") },
            TestContext.Current.CancellationToken);

        response.Text.ShouldBe("""{"lines":[]}""");
        response.Usage!.InputTokenCount.ShouldBe(120);
        response.Usage.OutputTokenCount.ShouldBe(30);
        var (path, body) = handler.Requests.ShouldHaveSingleItem();
        path.ShouldBe("/api/chat");
        using var sent = JsonDocument.Parse(body);
        sent.RootElement.GetProperty("model").GetString().ShouldBe("gemma3:12b");
        sent.RootElement.GetProperty("stream").GetBoolean().ShouldBeFalse();
        sent.RootElement.GetProperty("format").GetProperty("type").GetString().ShouldBe("object");
        sent.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32().ShouldBe(8192);
        sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString().ShouldBe("system");
    }

    [Fact]
    public async Task A_missing_model_says_how_to_pull_it()
    {
        var (provider, _) = Create((_, _) => (HttpStatusCode.NotFound, """{"error":"model 'x' not found"}"""));
        using var client = provider.CreateChatClient("gemma3:12b");

        var ex = await Should.ThrowAsync<LlmException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken));

        ex.Remedy!.ShouldContain("ollama pull gemma3:12b");
    }

    [Fact]
    public async Task Ollama_not_running_explains_how_to_install()
    {
        var (provider, _) = Create((_, _) => throw new HttpRequestException("Connection refused"));

        var ex = await Should.ThrowAsync<LlmException>(() => provider.ListModelsAsync(TestContext.Current.CancellationToken));

        ex.Remedy!.ShouldContain("winget install Ollama.Ollama");
        ex.IsUnanswered.ShouldBeTrue();
    }

    [Fact]
    public async Task Resolver_uses_the_chosen_local_model_or_the_only_one()
    {
        var (provider, _) = Create((_, _) => (HttpStatusCode.OK, Tags));
        var resolver = new ModelResolver([provider], () => new LlmSettings { Provider = "ollama" });
        var ct = TestContext.Current.CancellationToken;

        (await resolver.ResolveAsync(new LlmSettings { Provider = "ollama", LocalModel = "qwen3:8b" }, ct)).ShouldBe("qwen3:8b");
        (await Should.ThrowAsync<LlmException>(() => resolver.ResolveAsync(new LlmSettings { Provider = "ollama" }, ct)))
            .Remedy!.ShouldContain("Tercihler");
        (await Should.ThrowAsync<LlmException>(() => resolver.ResolveAsync(new LlmSettings { Provider = "ollama", LocalModel = "llama9" }, ct)))
            .Remedy!.ShouldContain("ollama pull llama9");
    }

    [Fact]
    public void Resolver_picks_the_provider_from_the_settings()
    {
        var (ollama, _) = Create((_, _) => (HttpStatusCode.OK, Tags));
        var gemini = new GeminiProvider(() => "key");
        var settings = new LlmSettings { Provider = "gemini" };
        var resolver = new ModelResolver([gemini, ollama], () => settings);

        resolver.Provider.ShouldBe(gemini);
        settings = settings with { Provider = "ollama" };
        resolver.Provider.ShouldBe(ollama);
        resolver.ProviderFor("unknown").ShouldBe(gemini);
    }

    [Fact]
    public async Task A_local_run_translates_through_the_pipeline()
    {
        var (provider, _) = Create((path, body) =>
        {
            // Echo the requested lines with an English marker, as a local model would.
            using var request = JsonDocument.Parse(body);
            var messages = request.RootElement.GetProperty("messages").EnumerateArray()
                .Select(m => new ChatMessage(new ChatRole(m.GetProperty("role").GetString()!), m.GetProperty("content").GetString())).ToList();
            var content = FakeChatClient.Echo(messages, (_, t) => t + " (en)").Text;
            return (HttpStatusCode.OK, JsonSerializer.Serialize(new { message = new { role = "assistant", content }, prompt_eval_count = 10, eval_count = 5 }));
        });
        using var client = provider.CreateChatClient("gemma3:12b");
        var runner = new LineBatchRunner(client, Microsoft.Extensions.Logging.Abstractions.NullLogger<LineBatchRunner>.Instance);

        var result = await runner.RunAsync(new TranslationTask(), [new LineItem(1, "merhaba", TimeSpan.FromSeconds(2))],
            new TaskContext(new Core.Projects.ProjectContext(), [], "tr", "en"), new PipelineOptions { ShortenOverlong = false },
            cancellationToken: TestContext.Current.CancellationToken);

        result.Lines.ShouldHaveSingleItem().Text.ShouldBe("merhaba (en)");
    }
}
