using LingoCue.Core.Subtitles;

namespace LingoCue.Transcription.Tests;

public class WordTimelineTests
{
    private static Word W(string text, double start, double end) => new(text, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    [Fact]
    public void Attaches_punctuation_trims_and_drops_empty()
    {
        var words = WordTimeline.Normalize([W(" Merhaba", 0, 0.4), W(",", 0.4, 0.45), W("  ", 0.5, 0.5), W("dünya", 0.5, 0.9), W("!", 0.9, 1.0)]);

        words.Select(w => w.Text).ShouldBe(["Merhaba,", "dünya!"]);
        words[0].End.ShouldBe(TimeSpan.FromSeconds(0.45));
        words[1].End.ShouldBe(TimeSpan.FromSeconds(1.0));
    }

    [Fact]
    public void Fixes_inverted_and_overlapping_times_and_sorts()
    {
        var words = WordTimeline.Normalize([W("iki", 1.0, 1.5), W("bir", 0.0, 1.2), W("üç", 2.0, 1.8)]);

        words.Select(w => w.Text).ShouldBe(["bir", "iki", "üç"]);
        words[0].End.ShouldBe(TimeSpan.FromSeconds(1.0));
        words[2].End.ShouldBe(words[2].Start);
    }

    [Fact]
    public void Drops_annotations_and_laughter_but_keeps_interjections()
    {
        var words = WordTimeline.Normalize(
        [
            W("*gülüşmeler*", 0, 1), W("Eheheh", 1, 2), W("Aa,", 2, 2.2), W("[Müzik", 2.3, 2.5), W("çalıyor]", 2.5, 3),
            W("Evet", 3, 3.3), W("(alkış)", 3.3, 4), W("hahaha!", 4, 4.5), W("♪", 4.5, 5), W("eh", 5, 5.2), W("tamam.", 5.2, 5.6),
        ]);

        words.Select(w => w.Text).ShouldBe(["Aa,", "Evet", "eh", "tamam."]);
    }

    [Theory]
    [InlineData("Eheheh", true)]
    [InlineData("hahaha", true)]
    [InlineData("Hehe.", true)]
    [InlineData("hıhıhı", true)]
    [InlineData("Aa", false)]
    [InlineData("eh", false)]
    [InlineData("hah", false)]
    [InlineData("hayır", false)]
    public void Recognizes_laughter(string text, bool expected)
        => WordTimeline.IsLaughter(text).ShouldBe(expected);

    [Fact]
    public void Offsets_chunk_times()
        => WordTimeline.Offset([W("a", 1, 2)], TimeSpan.FromMinutes(25)).Single().Start.ShouldBe(TimeSpan.FromMinutes(25) + TimeSpan.FromSeconds(1));
}
