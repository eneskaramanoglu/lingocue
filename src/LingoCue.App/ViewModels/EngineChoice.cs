using LingoCue.Llm.Providers;

namespace LingoCue.App.ViewModels;

/// <summary>Where a correction or translation runs: Gemini in the cloud or a local Ollama model.</summary>
public sealed record EngineChoice(string Id, string Label)
{
    public bool IsLocal => Id == OllamaProvider.ProviderId;

    public static IReadOnlyList<EngineChoice> From(ModelResolver resolver)
        => [.. resolver.Providers.Select(p => new EngineChoice(p.Id, p.Id == OllamaProvider.ProviderId ? "Yerel (Ollama)" : "Gemini (bulut)"))];

    /// <summary>Estimate line for a local run: no money, but time depends on the computer.</summary>
    public static string LocalEstimate(string modelId)
        => $"Yerel model: {modelId} · ücretsiz, içerik bilgisayardan çıkmaz · süre bilgisayarın ve ekran kartının hızına bağlı (ilk istekte model yüklenir).";
}
