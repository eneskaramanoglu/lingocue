using LingoCue.Core.Text;

namespace LingoCue.Core.Tests.Text;

public class LanguageDetectionTextTests
{
    private static readonly string[] s_trEnKo = ["tr", "en", "ko"];

    [Theory]
    [InlineData("Bugün İstanbul'da hava çok güzel.", "tr")]
    [InlineData("Boğaz'ın kıyısında yürüyoruz", "tr")]
    [InlineData("Thank you so much for watching this video.", "en")]
    [InlineData("What do you think about it?", "en")]
    [InlineData("안녕하세요, 여러분!", "ko")]
    [InlineData("오늘은 서울 Tower에 갑니다", "ko")]
    public void Picks_the_language_among_the_candidates(string text, string expected)
        => TextLanguageGuess.Guess(text, s_trEnKo).ShouldBe(expected);

    [Theory]
    [InlineData("2026")]
    [InlineData("...")]
    [InlineData("Instagram")]
    [InlineData("")]
    public void Gives_no_answer_without_evidence(string text)
        => TextLanguageGuess.Guess(text, s_trEnKo).ShouldBeNull();

    [Fact]
    public void English_contractions_are_not_turkish_suffixes()
        => TextLanguageGuess.Guess("I don't know, it's fine", ["tr", "en"]).ShouldBe("en");

    [Fact]
    public void A_single_candidate_is_the_answer()
        => TextLanguageGuess.Guess("Instagram", ["tr"]).ShouldBe("tr");

    [Fact]
    public void Mixed_selections_round_trip_and_name_their_main_language()
    {
        var selection = LanguageSelection.ForMixed(["tr", "en-US", "ko", "tr"]);

        selection.ShouldBe("tr+en+ko");
        LanguageSelection.Mixed(selection).ShouldBe(["tr", "en", "ko"]);
        LanguageSelection.Main(selection).ShouldBe("tr");
        LanguageSelection.IsMixed("tr").ShouldBeFalse();
        LanguageSelection.Main("auto").ShouldBeNull();
        LanguageSelection.Main("en").ShouldBe("en");
        LanguageSelection.ForMixed(["en"]).ShouldBe("en");
        LanguageSelection.Mixed("tr+en+ko+de").Count.ShouldBe(LanguageSelection.MaxMixed);
    }
}
