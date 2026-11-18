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
    public void Offsets_chunk_times()
        => WordTimeline.Offset([W("a", 1, 2)], TimeSpan.FromMinutes(25)).Single().Start.ShouldBe(TimeSpan.FromMinutes(25) + TimeSpan.FromSeconds(1));
}
