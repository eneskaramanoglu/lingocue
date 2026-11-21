using Microsoft.Extensions.AI;

namespace LingoCue.Llm.Providers;

/// <summary>A text model offered by a provider.</summary>
/// <param name="Id">Id passed to the API, without the <c>models/</c> prefix (e.g. <c>gemini-3.8-flash</c>).</param>
public sealed record LlmModelInfo(string Id, string DisplayName, string? Description = null, int? InputTokenLimit = null)
{
    public bool IsPreview => ModelCatalog.IsPreview(Id);

    public override string ToString() => DisplayName;
}

/// <summary>USD per 1M tokens.</summary>
public sealed record LlmPrice(decimal InputPerMillion, decimal OutputPerMillion, bool HasFreeTier)
{
    public decimal Cost(long inputTokens, long outputTokens)
        => Math.Round(((inputTokens * InputPerMillion) + (outputTokens * OutputPerMillion)) / 1_000_000m, 4);
}

/// <summary>
/// A chat model vendor behind <see cref="IChatClient"/> (Microsoft.Extensions.AI) so Claude, OpenAI or
/// Ollama can be added without touching the pipeline.
/// </summary>
public interface ILlmProvider
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>False when the provider needs a key that is not stored.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    IChatClient CreateChatClient(string modelId);

    /// <summary>Published list price, when known.</summary>
    LlmPrice? PriceFor(string modelId);
}

public sealed class LlmException : Exception
{
    public LlmException()
    {
    }

    public LlmException(string message)
        : base(message)
    {
    }

    public LlmException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>What the user can do (Turkish), when known.</summary>
    public string? Remedy { get; init; }

    /// <summary>The model gave no answer in time (not a wrong key or a bad request): worth trying again later.</summary>
    public bool IsUnanswered { get; init; }
}
