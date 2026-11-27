using LingoCue.Core.Subtitles;
using LingoCue.Transcription.Gemini;
using LingoCue.Transcription.Whisper;

namespace LingoCue.Transcription.Tests;

public class MixedLanguageTests
{
    private static SpeechRegion R(double start, double end) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    private static DetectedRegion D(double start, double end, string language, float probability = 0.9f) => new(R(start, end), language, probability);

    [Fact]
    public void Short_or_uncertain_regions_take_their_neighbours_language()
    {
        DetectedRegion[] regions =
        [
            D(0, 4, "tr"),
            D(4.5, 5, "en"),            // "okay": too short to trust
            D(6, 10, "tr"),
            D(11, 15, "ko", 0.3f),      // uncertain, between tr and en: the closer one (en, 0.5 s away) wins
            D(15.5, 20, "en"),
        ];

        LanguageRuns.Smooth(regions, "tr").ShouldBe(["tr", "tr", "tr", "en", "en"]);
    }

    [Fact]
    public void Reliable_guesses_are_kept_and_a_lone_unreliable_region_keeps_its_guess()
    {
        LanguageRuns.Smooth([D(0, 4, "ko"), D(5, 9, "tr")], "tr").ShouldBe(["ko", "tr"]);
        LanguageRuns.Smooth([D(0, 1, "en")], "tr").ShouldBe(["en"]);
        LanguageRuns.Smooth([D(0, 1, "en", 0)], "tr").ShouldBe(["tr"]);
    }

    [Fact]
    public void Regions_are_grouped_by_language_in_order_of_first_appearance()
    {
        SpeechRegion[] regions = [R(0, 2), R(3, 5), R(6, 8), R(9, 11)];

        var passes = LanguageRuns.Group(regions, ["en", "tr", "en", "ko"]);

        passes.Select(p => p.Language).ShouldBe(["en", "tr", "ko"]);
        passes[0].Regions.ShouldBe([R(0, 2), R(6, 8)]);
    }

    [Fact]
    public void A_stretch_written_in_another_chosen_language_is_flagged()
    {
        string[] candidates = ["tr", "en", "ko"];

        WhisperNetEngine.WrittenInAnotherLanguage("Thank you very much for watching.", "tr", candidates).ShouldBeTrue();
        WhisperNetEngine.WrittenInAnotherLanguage("Bugün hava çok güzel.", "tr", candidates).ShouldBeFalse();
        WhisperNetEngine.WrittenInAnotherLanguage("Instagram", "ko", candidates).ShouldBeFalse(); // no evidence
    }

    [Fact]
    public void Korean_has_its_own_style_prompt()
        => WhisperNetEngine.PromptFor("ko").ShouldNotBeNull().ShouldContain("안녕하세요");

    [Fact]
    public void Gemini_gets_every_chosen_language()
        => GeminiTranscribeEngine.ToGeminiLanguages("tr+en+ko").ShouldBe(["tr-TR", "en-US", "ko-KR"]);

    [Fact]
    public void Sentences_from_an_engine_without_languages_are_tagged_by_their_text()
    {
        var t = 0.0;
        Word W(string text)
        {
            var word = new Word(text, TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + 0.3));
            t += 0.35;
            return word;
        }

        Word[] words = [W("Merhaba"), W("arkadaşlar,"), W("bugün"), W("başlıyoruz."), W("Thank"), W("you"), W("so"), W("much."), W("2026"), W("안녕하세요!")];

        var tagged = MixedLanguageTagger.Tag(words, ["tr", "en", "ko"]);

        tagged.Select(w => w.Language).ShouldBe(["tr", "tr", "tr", "tr", "en", "en", "en", "en", "ko", "ko"]);
    }
}
