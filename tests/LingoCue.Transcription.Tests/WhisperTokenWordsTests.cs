using LingoCue.Core.Subtitles;
using LingoCue.Transcription.Whisper;

namespace LingoCue.Transcription.Tests;

public class WhisperTokenWordsTests
{
    [Fact]
    public void Merges_subword_tokens_and_skips_special_tokens()
    {
        RawToken[] tokens =
        [
            new("[_BEG_]", 0, 0, 1f),
            new(" Mer", 10, 30, 0.9f),
            new("haba", 30, 55, 0.6f),
            new(",", 55, 57, 0.95f),
            new(" İstanbul", 60, 120, 0.99f),
            new("<|tr|>", 120, 120, 1f),
        ];

        var words = WhisperTokenWords.ToWords(tokens, TimeSpan.FromSeconds(10));

        words.Count.ShouldBe(2);
        words[0].Text.ShouldBe("Merhaba,");
        words[0].Start.ShouldBe(TimeSpan.FromMilliseconds(10_100));
        words[0].End.ShouldBe(TimeSpan.FromMilliseconds(10_570));
        words[0].Confidence!.Value.ShouldBe(0.6, tolerance: 0.001); // lowest token wins
        words[1].Text.ShouldBe("İstanbul");
    }

    [Fact]
    public void Dtw_onsets_define_word_starts_and_ends_without_spanning_silence()
    {
        // Token t0/t1 collapsed to the segment edge (0..600 cs) as observed in real footage;
        // DTW onsets carry the real timing.
        RawToken[] tokens =
        [
            new(" Ben", 0, 600, 0.9f, 120),
            new(" size", 0, 600, 0.9f, 150),
            new(" imza", 0, 600, 0.9f, 182),
            new(" at", 0, 600, 0.9f, 220),
            new("ayım", 0, 600, 0.9f, 236),
            new(" Tamam", 0, 600, 0.9f, 480), // 2.4 s later: after a pause
        ];

        var words = WhisperTokenWords.ToWords(tokens);

        words.Select(w => w.Text).ShouldBe(["Ben", "size", "imza", "atayım", "Tamam"]);
        words[0].Start.ShouldBe(TimeSpan.FromMilliseconds(1200));
        words[0].End.ShouldBe(TimeSpan.FromMilliseconds(1500));   // up to the next word
        words[3].Start.ShouldBe(TimeSpan.FromMilliseconds(2200));
        words[3].End.ShouldBe(TimeSpan.FromMilliseconds(2760));   // last onset + 0.4 s, not across the pause
        words[4].Start.ShouldBe(TimeSpan.FromMilliseconds(4800));
    }

    [Fact]
    public void First_token_without_leading_space_still_starts_a_word()
        => WhisperTokenWords.ToWords([new("Evet", 0, 20, 1f), new(".", 20, 22, 1f)]).ShouldHaveSingleItem().Text.ShouldBe("Evet.");

    [Theory]
    [InlineData(1.0, 3.0, true)]   // fully inside speech
    [InlineData(5.0, 6.0, false)]  // silence: likely hallucination
    [InlineData(4.0, 6.0, false)]  // only 0.3 s of 2 s overlaps (with padding): 15 % < 30 %
    [InlineData(3.5, 4.5, true)]   // 0.8 of 1.0 s overlaps with padding
    public void Keeps_segments_overlapping_speech(double start, double end, bool expected)
    {
        SpeechRegion[] speech = [new(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(4))];

        WhisperTokenWords.OverlapsSpeech(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), speech, TimeSpan.FromMilliseconds(300))
            .ShouldBe(expected);
    }

    [Fact]
    public void Without_vad_regions_everything_is_kept()
        => WhisperTokenWords.OverlapsSpeech(TimeSpan.Zero, TimeSpan.FromSeconds(1), [], TimeSpan.Zero).ShouldBeTrue();

    [Theory]
    [InlineData("tr", "tr")]
    [InlineData("tr-TR", "tr")]
    [InlineData("EN_us", "en")]
    [InlineData(null, "auto")]
    [InlineData("auto", "auto")]
    public void Maps_bcp47_to_whisper_language(string? input, string expected)
        => WhisperNetEngine.ToWhisperLanguage(input).ShouldBe(expected);

    [Fact]
    public void Model_catalog_is_pinned_with_sha256()
    {
        foreach (var model in WhisperModels.All)
        {
            var file = WhisperModels.FileFor(model.Id);
            file.Sha256.Length.ShouldBe(64);
            file.Url.AbsoluteUri.ShouldNotContain("/main/");
            file.SizeBytes.ShouldBe(model.SizeBytes!.Value);
        }
    }

    [Fact]
    public void Characters_split_between_tokens_are_restored_from_the_segment_text()
    {
        // "이스탄불의" came back as two tokens that cut a Hangul syllable in half.
        RawToken[] tokens = [new(" 오늘은", 0, 50, 0.9f), new(" 이�", 50, 70, 0.8f), new("�탄불의", 70, 100, 0.9f)];

        var words = WhisperTokenWords.RepairText(WhisperTokenWords.ToWords(tokens), " 오늘은 이스탄불의");

        words.Select(w => w.Text).ShouldBe(["오늘은", "이스탄불의"]);
        words[1].Start.ShouldBe(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Damaged_words_that_cannot_be_matched_are_marked_for_review()
    {
        RawToken[] tokens = [new(" 이�", 0, 20, 0.8f), new(" 동�", 20, 40, 0.9f)];

        var words = WhisperTokenWords.RepairText(WhisperTokenWords.ToWords(tokens), "이스탄불의");

        words.ShouldAllBe(w => w.Confidence == 0);
        WhisperTokenWords.RepairText([new Word("Merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.9)], "x")[0].Text.ShouldBe("Merhaba");
    }
}
