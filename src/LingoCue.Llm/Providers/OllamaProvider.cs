using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace LingoCue.Llm.Providers;

/// <summary>
/// Local models through Ollama (<c>http://localhost:11434</c> by default): nothing leaves the computer
/// and there are no rate limits. The model list is whatever the user has pulled (<c>ollama pull …</c>).
/// Talks to Ollama's own HTTP API directly, so no extra package is needed.
/// </summary>
public sealed class OllamaProvider(HttpClient http, Func<string> baseUrl) : ILlmProvider
{
    public const string ProviderId = "ollama";

    public const string DefaultUrl = "http://localhost:11434";

    public string Id => ProviderId;

    public string DisplayName => "Yerel (Ollama)";

    /// <summary>No key needed; whether Ollama runs is checked when the model list is read.</summary>
    public bool IsConfigured => true;

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        TagsResponse? tags;
        try
        {
            tags = await http.GetFromJsonAsync(Url("api/tags"), OllamaJson.Default.TagsResponse, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw NotRunning(ex);
        }

        return [.. (tags?.Models ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => new LlmModelInfo(m.Name!, Describe(m)))
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)];
    }

    public IChatClient CreateChatClient(string modelId) => new OllamaChatClient(http, Url("api/chat"), modelId);

    /// <summary>Local runs cost nothing.</summary>
    public LlmPrice? PriceFor(string modelId) => new(0, 0, HasFreeTier: true);

    internal static LlmException NotRunning(Exception inner)
        => new("Ollama is not reachable.", inner)
        {
            Remedy = "Yerel model için Ollama çalışmıyor. Kurun (Windows: winget install Ollama.Ollama, macOS: brew install ollama), " +
                     "uygulamayı açın ve bir model indirin (ör. ollama pull gemma3:12b). Adres Tercihler'deki ile aynı olmalı.",
            IsUnanswered = true,
        };

    private Uri Url(string path) => new(new Uri(baseUrl().TrimEnd('/') + "/"), path);

    private static string Describe(TagModel m)
    {
        var size = m.Details?.ParameterSize is { Length: > 0 } p ? $" · {p}" : string.Empty;
        var gb = m.Size > 0 ? $" · {m.Size / 1_000_000_000.0:0.#} GB" : string.Empty;
        return m.Name + size + gb;
    }
}

/// <summary>
/// <see cref="IChatClient"/> over Ollama's <c>/api/chat</c> (non-streaming). A JSON schema response
/// format is passed as Ollama's <c>format</c>, so small local models still return valid JSON.
/// </summary>
public sealed class OllamaChatClient(HttpClient http, Uri chatUrl, string model) : IChatClient
{
    /// <summary>Context window requested from Ollama; batches with context lines need more than its 2048 default.</summary>
    public int ContextLength { get; init; } = 8192;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var request = new ChatRequest
        {
            Model = model,
            Messages = [.. messages.Select(m => new ChatMessageDto { Role = m.Role.Value, Content = m.Text })],
            Format = options?.ResponseFormat switch
            {
                ChatResponseFormatJson { Schema: { } schema } => schema,
                ChatResponseFormatJson => JsonSerializer.SerializeToElement("json", OllamaJson.Default.String),
                _ => null,
            },
            Options = new ModelOptions { Temperature = options?.Temperature, ContextLength = ContextLength },
        };

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(chatUrl, request, OllamaJson.Default.ChatRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw OllamaProvider.NotRunning(ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new LlmException($"Ollama returned {(int)response.StatusCode}: {body}")
                {
                    Remedy = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? $"\"{model}\" modeli Ollama'da yok. Terminalde indirin: ollama pull {model}"
                        : null,
                };
            }

            var result = await response.Content.ReadFromJsonAsync(OllamaJson.Default.ChatResponseDto, cancellationToken)
                         ?? throw new LlmException("Ollama returned an empty answer.");
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, result.Message?.Content ?? string.Empty))
            {
                ModelId = model,
                Usage = new UsageDetails { InputTokenCount = result.PromptEvalCount, OutputTokenCount = result.EvalCount },
            };
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Not needed by the pipeline; one non-streaming call, returned as a single update.
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

#pragma warning disable SA1402 // Small wire types belong with the client that uses them.
internal sealed class ChatRequest
{
    public required string Model { get; init; }

    public required List<ChatMessageDto> Messages { get; init; }

    public bool Stream { get; init; }

    /// <summary>A JSON schema, the string "json", or absent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Format { get; init; }

    public ModelOptions? Options { get; init; }
}

internal sealed class ChatMessageDto
{
    public string Role { get; init; } = "user";

    public string? Content { get; init; }
}

internal sealed class ModelOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; init; }

    [JsonPropertyName("num_ctx")]
    public int ContextLength { get; init; }
}

internal sealed class ChatResponseDto
{
    public ChatMessageDto? Message { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public long? PromptEvalCount { get; init; }

    [JsonPropertyName("eval_count")]
    public long? EvalCount { get; init; }
}

internal sealed class TagsResponse
{
    public List<TagModel>? Models { get; init; }
}

internal sealed class TagModel
{
    public string? Name { get; init; }

    public long Size { get; init; }

    public TagDetails? Details { get; init; }
}

internal sealed class TagDetails
{
    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponseDto))]
[JsonSerializable(typeof(TagsResponse))]
[JsonSerializable(typeof(string))]
internal sealed partial class OllamaJson : JsonSerializerContext
{
}
#pragma warning restore SA1402
