using System.Text;
using LingoCue.Transcription.Whisper;

namespace LingoCue.Transcription.Tests;

public class WhisperAccuracyTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Speech_regions_are_packed_and_mapped_back()
    {
        // Speech at 1–3 s, 3.5–5 s (close: kept together) and 20–22 s (after a long silence).
        var regions = new List<SpeechRegion> { new(S(1), S(3)), new(S(3.5), S(5)), new(S(20), S(22)) };

        var timeline = SpeechTimeline.Build(regions, S(30), padding: S(0.25), mergeGap: S(1), joinGap: S(0.5));

        timeline.Pieces.Select(p => (p.SourceStart, p.SourceEnd, p.PackedStart))
            .ShouldBe([(S(0.75), S(5.25), S(0)), (S(19.75), S(22.25), S(5))]); // 4.5 s + 0.5 s pause
        timeline.PackedDuration.ShouldBe(S(7.5)); // no pause after the last piece
        timeline.ToSource(S(1)).ShouldBe(S(1.75));
        timeline.ToSource(S(5.5)).ShouldBe(S(20.25));
        timeline.ToSource(S(4.75)).ShouldBe(S(5.25)); // inside the pause: end of the piece before it
    }

    [Fact]
    public void Regions_are_kept_inside_the_audio()
    {
        var timeline = SpeechTimeline.Build([new(S(0.1), S(2)), new(S(9.9), S(10))], S(10), S(0.3), S(1), S(0.5));

        timeline.Pieces[0].SourceStart.ShouldBe(TimeSpan.Zero);
        timeline.Pieces[^1].SourceEnd.ShouldBe(S(10));
    }

    [Fact]
    public void Packing_copies_the_samples_of_each_piece()
    {
        var samples = Enumerable.Range(0, 100).Select(i => (float)i).ToArray(); // 10 Hz: one sample per 0.1 s
        var timeline = SpeechTimeline.Build([new(S(1), S(2)), new(S(6), S(7))], S(10), TimeSpan.Zero, S(1), S(0.5));

        var packed = timeline.Pack(samples, sampleRate: 10);

        packed.Length.ShouldBe(25); // 1 s + 0.5 s pause + 1 s
        packed[0].ShouldBe(10);
        packed[9].ShouldBe(19);
        packed[12].ShouldBe(0); // the pause
        packed[15].ShouldBe(60);
    }

    [Fact]
    public void Reads_ffmpegs_wav_with_extra_chunks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lingocue-wav-{Guid.NewGuid():N}.wav");
        try
        {
            using (var stream = File.Create(path))
            using (var w = new BinaryWriter(stream))
            {
                w.Write("RIFF"u8);
                w.Write(0);
                w.Write("WAVE"u8);
                w.Write("fmt "u8);
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(16000);
                w.Write(32000);
                w.Write((short)2);
                w.Write((short)16);
                w.Write("LIST"u8);
                w.Write(3);
                w.Write(Encoding.ASCII.GetBytes("abc\0")); // odd size + pad byte
                w.Write("data"u8);
                w.Write(4);
                w.Write((short)16384);
                w.Write((short)-32768);
            }

            var (samples, rate) = WavSamples.Read(path);

            rate.ShouldBe(16000);
            samples.ShouldBe([0.5f, -1f]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Altyazı M.K.", null, 0.1f, 0.9f, true)]
    [InlineData("İzlediğiniz için teşekkürler!", null, 0.1f, 0.9f, true)]
    [InlineData("Bugün hava çok güzel.", "Bugün hava çok güzel.", 0.1f, 0.9f, true)] // a loop
    [InlineData("Evet.", null, 0.8f, 0.3f, true)]                                    // "silent" and unsure
    [InlineData("Bugün hava çok güzel.", "Merhaba.", 0.1f, 0.9f, false)]
    [InlineData("Evet.", null, 0.8f, 0.9f, false)]
    public void Flags_text_whisper_tends_to_invent(string text, string? previous, float noSpeech, float probability, bool expected)
        => WhisperHallucinations.IsSuspicious(text, previous, noSpeech, probability).ShouldBe(expected);

    [Fact]
    public void Glossary_names_follow_the_style_prompt_and_are_capped()
    {
        WhisperNetEngine.PromptFor("tr", ["Kerem Yılmazer", "Kuzguncuk", "kuzguncuk", " "])
            .ShouldBe("Merhaba, hoş geldiniz. Bugün neler yapacağız? Evet, başlayalım. Kerem Yılmazer, Kuzguncuk.");
        WhisperNetEngine.PromptFor("de", ["Berlin"]).ShouldBe("Berlin.");
        WhisperNetEngine.PromptFor("de").ShouldBeNull();

        var many = Enumerable.Range(1, 100).Select(i => $"İsim{i}").ToList();
        var prompt = WhisperNetEngine.PromptFor("tr", many)!;
        prompt.Length.ShouldBeLessThanOrEqualTo(WhisperNetEngine.PromptFor("tr")!.Length + 1 + WhisperNetEngine.MaxHintCharacters);
    }

    [Fact]
    public void Punctuation_tokens_do_not_lower_a_words_confidence()
    {
        var words = WhisperTokenWords.ToWords(
        [
            new RawToken(" Boğaz", 0, 50, 0.9f),
            new RawToken("'", 50, 52, 0.03f),
            new RawToken("ın", 52, 60, 0.8f),
        ]);

        words.ShouldHaveSingleItem().Text.ShouldBe("Boğaz'ın");
        words[0].Confidence!.Value.ShouldBe(0.8, tolerance: 0.001);
    }
}
