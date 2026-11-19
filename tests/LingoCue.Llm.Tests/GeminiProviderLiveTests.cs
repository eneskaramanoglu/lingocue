using LingoCue.Infrastructure.Security;
using LingoCue.Llm.Providers;

namespace LingoCue.Llm.Tests;

/// <summary>
/// Lists models with the real Gemini API (free: listing models costs nothing). Runs only when
/// LINGOCUE_LLM_LIVE=1 and a key is available (GEMINI_API_KEY or the key saved in the app).
/// </summary>
public class GeminiProviderLiveTests
{
    [Fact]
    public async Task Lists_models_and_finds_a_fast_default()
    {
        var key = new ApiKeyProvider(SecretStoreFactory.Create()).GetGeminiKey();
        if (Environment.GetEnvironmentVariable("LINGOCUE_LLM_LIVE") != "1" || string.IsNullOrWhiteSpace(key))
        {
            Assert.Skip("Set LINGOCUE_LLM_LIVE=1 and a Gemini key to run.");
        }

        var models = await new GeminiProvider(() => key).ListModelsAsync(TestContext.Current.CancellationToken);

        await File.WriteAllLinesAsync(
            Path.Combine(Path.GetTempPath(), "lingocue-gemini-models.txt"),
            models.Select(m => $"{m.Id} | {m.DisplayName} | {m.InputTokenLimit}"),
            TestContext.Current.CancellationToken);

        models.ShouldNotBeEmpty();
        ModelCatalog.PickFast(models).ShouldNotBeNull();
    }
}
