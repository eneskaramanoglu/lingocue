using LingoCue.Llm.Providers;

namespace LingoCue.Llm.Tests;

public class ModelCatalogTests
{
    private static readonly LlmModelInfo[] s_models =
    [
        new("gemini-3.8-flash", "Gemini 3.8 Flash"),
        new("gemini-3.6-flash", "Gemini 3.6 Flash"),
        new("gemini-3-flash-preview", "Gemini 3 Flash Preview"),
        new("gemini-flash-latest", "Gemini Flash Latest"),
        new("gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite"),
        new("gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite"),
        new("gemini-3.1-pro-preview", "Gemini 3.1 Pro Preview"),
        new("gemini-3.5-transcribe", "Gemini 3.5 Transcribe"),
        new("gemini-3.8-flash-image", "Gemini 3.8 Flash Image"),
        new("gemini-embedding-001", "Embedding"),
        new("imagen-4.0-generate", "Imagen"),
    ];

    [Fact]
    public void Picks_newest_stable_flash_lite_and_pro()
    {
        ModelCatalog.PickFast(s_models)!.Id.ShouldBe("gemini-3.8-flash");
        ModelCatalog.PickLite(s_models)!.Id.ShouldBe("gemini-3.5-flash-lite");
        ModelCatalog.PickQuality(s_models)!.Id.ShouldBe("gemini-3.8-flash"); // 3.1 Pro (preview) is older than 3.8 Flash
    }

    [Fact]
    public void Prefers_a_stable_pro_when_one_exists()
        => ModelCatalog.PickQuality([.. s_models, new("gemini-3.9-pro", "Gemini 3.9 Pro")])!.Id.ShouldBe("gemini-3.9-pro");

    [Fact]
    public void Quality_falls_back_to_flash_when_the_only_pro_is_older()
    {
        // The real list for the owner's key on 2026-11-18 (codenames and image model included).
        LlmModelInfo[] real =
        [
            new("gemini-4-argon", "Gemini 4 Argon"), new("gemini-3.8-flash", "Gemini 3.8 Flash"),
            new("gemini-3.7-flash", "Gemini 3.7 Flash"), new("gemini-3.5-flash-lite", "Gemini 3.5 Flash Lite"),
            new("gemini-2.5-pro", "Gemini 2.5 Pro"), new("gemini-nano-banana-2.1", "Nano Banana 2.1"),
        ];

        ModelCatalog.PickFast(real)!.Id.ShouldBe("gemini-3.8-flash");
        ModelCatalog.PickQuality(real)!.Id.ShouldBe("gemini-3.8-flash");
        ModelCatalog.TextModels(real).Select(m => m.Id).ShouldNotContain("gemini-nano-banana-2.1");
    }

    [Fact]
    public void Text_models_exclude_image_audio_transcribe_and_embeddings()
        => ModelCatalog.TextModels(s_models).Select(m => m.Id)
            .ShouldNotContain(id => id.Contains("image") || id.Contains("transcribe") || id.Contains("embedding") || id.StartsWith("imagen"));

    [Theory]
    [InlineData("gemini-3.8-flash", 3.8)]
    [InlineData("gemini-3-flash-preview", 3)]
    [InlineData("gemini-flash-latest", 0)]
    public void Parses_versions(string id, double expected)
        => ModelCatalog.Version(id).ShouldBe((decimal)expected);

    [Fact]
    public void Prices_are_known_for_listed_families_and_compute_cost()
    {
        var provider = new GeminiProvider(() => "x");

        var flash = provider.PriceFor("gemini-3.8-flash")!;
        flash.HasFreeTier.ShouldBeTrue();
        flash.Cost(20_000, 10_000).ShouldBe(0.0525m); // 0.015 + 0.0375
        provider.PriceFor("gemini-3.1-pro-preview")!.HasFreeTier.ShouldBeFalse();
        provider.PriceFor("gemini-9-unknown").ShouldBeNull();
    }

    [Fact]
    public void Missing_key_gives_a_turkish_remedy()
    {
        var provider = new GeminiProvider(() => null);

        provider.IsConfigured.ShouldBeFalse();
        Should.Throw<LlmException>(() => provider.CreateChatClient("gemini-3.8-flash")).Remedy.ShouldNotBeNull();
    }
}
