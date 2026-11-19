using LingoCue.Core.Projects;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;

namespace LingoCue.Llm.Tests;

public class PipelineHelpersTests
{
    [Theory]
    [InlineData("""{"lines":[{"id":1,"text":"a"}]}""")]
    [InlineData("```json\n{\"lines\":[{\"id\":1,\"text\":\"a\"}]}\n```")]
    [InlineData("""[{"id":1,"text":"a"}]""")]
    public void Parses_answers_with_fences_or_bare_arrays(string answer)
        => LineJson.TryParse(answer, out _)!.ShouldBe(new Dictionary<int, string> { [1] = "a" });

    [Theory]
    [InlineData("", "empty")]
    [InlineData("{ broken", "not valid JSON")]
    [InlineData("""{"items":[]}""", "no \"lines\"")]
    [InlineData("""{"lines":[{"id":1,"text":"a"},{"id":1,"text":"b"}]}""", "more than once")]
    [InlineData("""{"lines":[{"id":"x","text":"a"}]}""", "integer")]
    public void Explains_unusable_answers(string answer, string expected)
    {
        LineJson.TryParse(answer, out var error).ShouldBeNull();
        error.ShouldNotBeNull().ShouldContain(expected);
    }

    [Theory]
    [InlineData("evet", "Evet.", 0.2)]
    [InlineData("İstanbul", "istanbul", 0)]
    [InlineData("abc", "xyz", 1)]
    public void Change_ratio_is_turkish_case_insensitive(string a, string b, double expected)
        => TextDistance.ChangeRatio(a, b).ShouldBe(expected, tolerance: 0.01);

    [Fact]
    public async Task Context_extractor_reads_summary_address_speakers_and_terms()
    {
        var client = new FakeChatClient((_, _, _) => FakeChatClient.Json("""
            { "summary": "Sokak röportajı.", "tone": "samimi", "address": "siz", "speakers": ["Muhabir", "Yoldan geçen"],
              "terms": [ { "term": "evaleli", "preferred": "Evaleli", "kind": "properNoun", "note": "kanal adı" },
                         { "term": "Boğaz", "kind": "properNoun" } ] }
            """));
        var lines = new List<LineItem> { new(1, "Merhaba", TimeSpan.FromSeconds(1), "spk_1") };

        var result = await new ContextExtractor(client).ExtractAsync(lines, "tr", [new GlossaryEntry { Term = "İstanbul" }], TestContext.Current.CancellationToken);

        result.Context.Summary.ShouldBe("Sokak röportajı.");
        result.Context.Address.ShouldBe("siz");
        result.Context.Speakers.ShouldBe(["Muhabir", "Yoldan geçen"]);
        result.Suggestions.Count.ShouldBe(2);
        result.Suggestions[0].Preferred.ShouldBe("Evaleli");
        result.Suggestions[0].Kind.ShouldBe(GlossaryKind.ProperNoun);
        result.Suggestions[1].Preferred.ShouldBeNull();
        client.Calls[0].Messages[0].Text.ShouldContain("İstanbul"); // existing glossary is excluded
        client.Calls[0].Messages[1].Text.ShouldContain("[spk_1] Merhaba");
    }

    [Fact]
    public void Estimates_cost_and_reflect_roughly_doubles_it()
    {
        var lines = Enumerable.Range(1, 120).Select(i => new LineItem(i, "Bugün İstanbul'da güzel bir gün, Boğaz'a gidiyoruz.", TimeSpan.FromSeconds(3))).ToList();
        var price = new LlmPrice(0.75m, 3.75m, true);

        var plain = CostEstimator.Estimate(lines, new PipelineOptions { ShortenOverlong = false }, 10, includeContextPass: true, price);
        var reflect = CostEstimator.Estimate(lines, new PipelineOptions { ShortenOverlong = false, Reflect = true }, 10, includeContextPass: true, price);

        plain.InputTokens.ShouldBeInRange(5_000, 20_000);
        plain.Usd!.Value.ShouldBeLessThan(0.05m);
        plain.FreeTierAvailable.ShouldBeTrue();
        ((double)reflect.TotalTokens / plain.TotalTokens).ShouldBeInRange(1.6, 2.4);
        CostEstimator.Estimate([], new PipelineOptions(), 0, false, null).Usd.ShouldBe(0);
    }
}
