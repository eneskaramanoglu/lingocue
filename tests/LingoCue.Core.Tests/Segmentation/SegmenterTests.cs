using LingoCue.Core.Segmentation;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Segmentation;

public class SegmenterTests
{
    /// <summary>Builds words spoken back-to-back at 300 ms per word starting at <paramref name="start"/>.</summary>
    private static List<Word> Words(string text, double start = 0, double perWord = 0.3, string? speaker = null)
    {
        var result = new List<Word>();
        var t = start;
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result.Add(new Word(token, TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + perWord - 0.02), 0.9, speaker));
            t += perWord;
        }

        return result;
    }

    [Fact]
    public void Short_sentence_becomes_one_cue_with_words_and_speaker()
    {
        var cues = Segmenter.Segment(Words("Merhaba, nasılsın?", speaker: "spk_1"));

        var cue = cues.ShouldHaveSingleItem();
        cue.Text.ShouldBe("Merhaba, nasılsın?");
        cue.Speaker.ShouldBe("spk_1");
        cue.Words.Count.ShouldBe(2);
        cue.Start.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Splits_on_sentence_end_pause_and_speaker_change()
    {
        var words = Words("Bugün hava çok güzel.", 0)
            .Concat(Words("Evet öyle.", 1.3))                 // sentence end before
            .Concat(Words("Peki sonra", 3.0))                 // 1.1 s pause before
            .Concat(Words("Bilmiyorum", 3.6, speaker: "spk_2")) // speaker change (previous null → no split)
            .ToList();
        for (var i = 0; i < words.Count - 1; i++)
        {
            words[i] = words[i] with { Speaker = "spk_1" };
        }

        var cues = Segmenter.Segment(words);

        cues.Select(c => c.Text).ShouldBe(["Bugün hava çok güzel.", "Evet öyle.", "Peki sonra", "Bilmiyorum"]);
        cues[3].Speaker.ShouldBe("spk_2");
    }

    [Fact]
    public void Long_speech_respects_line_length_line_count_and_duration()
    {
        var text = string.Join(' ', Enumerable.Repeat("bugün çok uzun ve kesintisiz bir konuşma yapıyoruz", 6));

        var cues = Segmenter.Segment(Words(text, perWord: 0.25));

        cues.Count.ShouldBeGreaterThan(2);
        foreach (var cue in cues)
        {
            var lines = cue.Text.Split('\n');
            lines.Length.ShouldBeLessThanOrEqualTo(2);
            lines.ShouldAllBe(l => l.Length <= 42);
            cue.Duration.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(7));
        }

        // Nothing lost or duplicated.
        string.Join(' ', cues.Select(c => c.Text.Replace('\n', ' '))).ShouldBe(text);
    }

    [Fact]
    public void Prefers_breaking_after_a_comma()
    {
        // 9 + 1 + ... the comma sits early enough to be a candidate.
        var text = "Toplantıdan sonra hemen eve döndük ve akşam yemeği için hazırlık yaptık, misafirler de saat sekizde geldiler";

        var cues = Segmenter.Segment(Words(text, perWord: 0.2));

        cues[0].Text.Replace('\n', ' ').ShouldEndWith("yaptık,");
    }

    [Theory]
    [InlineData("İstanbul'un", "sokakları")]
    [InlineData("evimizin", "bahçesi")]
    [InlineData("bir", "ev")]
    [InlineData("3", "kişi")]
    [InlineData("çok", "güzel")]
    [InlineData("güzel", "köşelerini")]
    [InlineData("büyük", "şehir")]
    public void Never_breaks_lines_inside_turkish_noun_phrases(string left, string right)
    {
        // Padding so the natural balanced break would fall exactly between left and right.
        var padding = "aaaaa bbbbb ccccc ddddd eee";
        var words = Words($"{padding} {left} {right} {padding}");

        var text = Segmenter.WrapLines(words);

        text.ShouldContain($"{left} {right}");
        text.ShouldContain('\n');
    }

    [Fact]
    public void Wraps_long_cue_into_balanced_lines_with_shorter_top()
    {
        var text = Segmenter.WrapLines(Words("Bu akşam İstanbul'da büyük bir konser var ve herkes oraya gidiyor"));

        var lines = text.Split('\n');
        lines.Length.ShouldBe(2);
        lines.ShouldAllBe(l => l.Length <= 42);
        lines[1].ShouldStartWith("ve"); // break before the conjunction
    }

    [Fact]
    public void Extends_short_cues_for_reading_but_never_overlaps_next()
    {
        var words = Words("Tamam.", 0, 0.3).Concat(Words("Peki o zaman yarın görüşürüz.", 1.0)).ToList();

        var cues = Segmenter.Segment(words);

        cues[0].End.ShouldBe(TimeSpan.FromSeconds(0.8)); // extended to the 0.8 s minimum (room until 0.92 s)
        cues[0].End.ShouldBeLessThan(cues[1].Start);
        cues[1].Duration.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.8));
    }

    [Fact]
    public void Extended_cues_do_not_exceed_the_cps_target_when_there_is_room()
    {
        // Fast speech followed by a long pause: room to extend.
        var cues = Segmenter.Segment(Words("Bu videoda size şehrin en güzel köşelerini göstereceğim.", perWord: 0.2)
            .Concat(Words("Sonra.", 10)).ToList());

        cues[0].CharactersPerSecond.ShouldBeLessThanOrEqualTo(17);
        ReadingRules.Default.Evaluate(cues[0]).ShouldBe(CueIssues.None);
    }

    [Fact]
    public void English_rules_keep_articles_with_nouns()
    {
        var words = Words("we walked through the old town and found a really nice little cafe near the river");

        var text = Segmenter.WrapLines(words, "en");

        text.ShouldNotContain("the\n");
        text.ShouldNotContain("a\n");
    }

    [Fact]
    public void Real_whisper_sentence_keeps_adjective_with_noun()
    {
        // From an actual Whisper run during development.
        var text = Segmenter.WrapLines(Words("Bu videoda size şehrin en güzel köşelerini göstereceğim."));

        text.ShouldNotContain("güzel\n");
        text.ShouldNotContain("şehrin\n");
        text.Split('\n').ShouldAllBe(l => l.Length <= 42);
    }

    [Fact]
    public void Empty_input_gives_no_cues()
        => Segmenter.Segment([]).ShouldBeEmpty();
}
