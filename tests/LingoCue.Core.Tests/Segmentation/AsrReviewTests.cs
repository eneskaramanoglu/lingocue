using LingoCue.Core.Projects;
using LingoCue.Core.Segmentation;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Segmentation;

public class AsrReviewTests
{
    [Fact]
    public void Glossary_hints_use_the_preferred_spelling_of_source_terms()
    {
        GlossaryEntry[] glossary =
        [
            new() { Term = "kuzguncuk", Preferred = "Kuzguncuk", Suggested = true },
            new() { Term = "Kerem Yılmazer" },
            new() { Term = "Boğaz", Preferred = "the Bosphorus", TargetLanguage = "en" },
            new() { Term = "KUZGUNCUK" },
        ];

        AsrReview.SpellingHints(glossary).ShouldBe(["Kerem Yılmazer", "KUZGUNCUK"]); // confirmed first; duplicates dropped
    }

    [Fact]
    public void Lines_with_a_doubtful_word_are_flagged()
    {
        var sure = new Cue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Merhaba") { Words = { new Word("Merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.9) } };
        var doubtful = new Cue(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Altyazı M.K.")
        {
            Words = { new Word("Altyazı", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.5), 0), new Word("M.K.", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(3), 0) },
        };
        var gemini = new Cue(3, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), "Tamam") { Words = { new Word("Tamam", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5)) } };

        AsrReview.FlagDoubtful([sure, doubtful, gemini]).ShouldBe(1);

        doubtful.Marks.HasFlag(CueMarks.NeedsReview).ShouldBeTrue();
        sure.Marks.ShouldBe(CueMarks.None);
        gemini.Marks.ShouldBe(CueMarks.None); // no confidence reported
    }
}
