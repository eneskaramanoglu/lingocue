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
/// <param name="http">Client for Ollama's HTTP API.</param>
/// <param name="baseUrl">Server address, read on every call so a settings change applies at once.</param>
/// <param name="thinking">Whether thinking models may reason before answering (settings: <c>LocalThinking</c>); off when null.</param>
public sealed class OllamaProvider(HttpClient http, Func<string> baseUrl, Func<bool>? thinking = null) : ILlmProvider
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

    public IChatClient CreateChatClient(string modelId)
        => new OllamaChatClient(http, Url("api/chat"), modelId) { Thinking = thinking?.Invoke() ?? false };

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
/// format is passed as Ollama's <c>format</c>, so small local models still return valid JSON. Models
/// whose runner has no structured output (Ollama answers 501 "structured output is unavailable", e.g.
/// gemma4) are asked again without it; the prompts already ask for JSON and the parser strips fences.
/// Thinking is off unless <see cref="Thinking"/> is set (<c>think: false</c>): on gemma4 it made a 3-line
/// translation take 127 s instead of 10 s with the same result. Models that reject the switch are asked
/// again without it.
/// </summary>
public sealed class OllamaChatClient(HttpClient http, Uri chatUrl, string model) : IChatClient
{
    // Remembered for this client (one model), so later batches skip the call Ollama refused.
    private volatile bool _structuredOutputUnavailable;
    private volatile bool _thinkSwitchUnsupported;

    /// <summary>Context window requested from Ollama; batches with context lines need more than its 2048 default.</summary>
    public int ContextLength { get; init; } = 8192;

    /// <summary>Sent as Ollama's <c>think</c>: true lets thinking models reason before answering (much slower).</summary>
    public bool Thinking { get; init; }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        List<ChatMessageDto> dtos = [.. messages.Select(m => new ChatMessageDto { Role = m.Role.Value, Content = m.Text })];
        var format = _structuredOutputUnavailable ? null : options?.ResponseFormat switch
        {
            ChatResponseFormatJson { Schema: { } schema } => schema,
            ChatResponseFormatJson => JsonSerializer.SerializeToElement("json", OllamaJson.Default.String),
            _ => (JsonElement?)null,
        };
        bool? think = _thinkSwitchUnsupported ? null : Thinking;

        var response = await PostAsync(dtos, format, think, options, cancellationToken);

        // At most one retry per refused feature.
        for (var attempt = 0; attempt < 2 && !response.IsSuccessStatusCode; attempt++)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken); // buffered; still readable below
            if (format is not null && error.Contains("structured output", StringComparison.OrdinalIgnoreCase))
            {
                _structuredOutputUnavailable = true;
                format = null;
            }
            else if (think is not null && error.Contains("thinking", StringComparison.OrdinalIgnoreCase))
            {
                _thinkSwitchUnsupported = true;
                think = null;
            }
            else
            {
                break;
            }

            response.Dispose();
            response = await PostAsync(dtos, format, think, options, cancellationToken);
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

    private async Task<HttpResponseMessage> PostAsync(List<ChatMessageDto> messages, JsonElement? format, bool? think, ChatOptions? options, CancellationToken cancellationToken)
    {
        var request = new ChatRequest
        {
            Model = model,
            Messages = messages,
            Format = format,
            Think = think,
            Options = new ModelOptions { Temperature = options?.Temperature, ContextLength = ContextLength },
        };

        try
        {
            return await http.PostAsJsonAsync(chatUrl, request, OllamaJson.Default.ChatRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw OllamaProvider.NotRunning(ex);
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

    /// <summary>Ollama's reasoning switch; false keeps thinking models (gemma4, qwen3) from writing long hidden reasoning.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Think { get; init; }

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
