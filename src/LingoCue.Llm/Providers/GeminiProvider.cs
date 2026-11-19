using Google.GenAI;
using Microsoft.Extensions.AI;

namespace LingoCue.Llm.Providers;

/// <summary>
/// Google Gemini through the official Google.GenAI SDK and its <see cref="IChatClient"/> adapter.
/// The key is read on demand, so changing it in the settings takes effect without a restart.
/// </summary>
public sealed class GeminiProvider(Func<string?> apiKey) : ILlmProvider
{
    // List prices in USD per 1M tokens (Standard tier, ai.google.dev pricing, checked 2026-11-18).
    // Used only for estimates; the free tier costs nothing within its rate limits.
    private static readonly (string Prefix, LlmPrice Price)[] s_prices =
    [
        ("gemini-3.8-flash", new(0.75m, 3.75m, true)),
        ("gemini-3.6-flash", new(0.75m, 3.75m, true)),
        ("gemini-3.5-flash-lite", new(0.30m, 2.50m, true)),
        ("gemini-3.1-flash-lite", new(0.25m, 1.50m, true)),
        ("gemini-3.1-pro", new(2.00m, 12.00m, false)),
    ];

    public string Id => "gemini";

    public string DisplayName => "Google Gemini";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey());

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var result = new List<LlmModelInfo>();
        var pager = await client.Models.ListAsync(cancellationToken: cancellationToken);
        await foreach (var model in pager.WithCancellation(cancellationToken))
        {
            if (model.Name is not { } name)
            {
                continue;
            }

            var id = name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name;
            result.Add(new LlmModelInfo(id, model.DisplayName ?? id, model.Description, model.InputTokenLimit));
        }

        return [.. ModelCatalog.TextModels(result).OrderByDescending(m => ModelCatalog.Version(m.Id)).ThenBy(m => m.Id, StringComparer.Ordinal)];
    }

    public IChatClient CreateChatClient(string modelId) => CreateClient().AsIChatClient(modelId);

    public LlmPrice? PriceFor(string modelId)
        => s_prices.Where(p => modelId.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(p => p.Prefix.Length)
                   .Select(p => p.Price)
                   .FirstOrDefault();

    private Client CreateClient()
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new LlmException("Gemini API key is missing.")
            {
                Remedy = "Ayarlar → API anahtarları bölümünden Gemini anahtarınızı girin.",
            };
        }

        return new Client(apiKey: key);
    }
}
