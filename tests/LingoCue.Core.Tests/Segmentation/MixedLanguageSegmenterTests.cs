using LingoCue.Core.Projects;
using LingoCue.Core.Segmentation;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Segmentation;

public class MixedLanguageSegmenterTests
{
    private static List<Word> Words(string text, double start, string language, double perWord = 0.3)
    {
        var result = new List<Word>();
        var t = start;
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result.Add(new Word(token, TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + perWord - 0.02), 0.9, Language: language));
            t += perWord;
        }

        return result;
    }

    [Fact]
    public void A_language_switch_starts_a_new_cue_even_without_a_pause()
    {
        var words = Words("Bugün sizinle", 0, "tr").Concat(Words("let's get started", 0.6, "en")).ToList();

        var cues = Segmenter.Segment(words, new SegmentationOptions { Language = "tr" });

        cues.Count.ShouldBe(2);
        (cues[0].Text, cues[0].Language).ShouldBe(("Bugün sizinle", "tr"));
        (cues[1].Text, cues[1].Language).ShouldBe(("let's get started", "en"));
    }

    [Fact]
    public void Korean_lines_use_the_korean_line_length_inside_a_turkish_track()
    {
        // 30 Hangul characters: one line under the Turkish 42-character limit, too long for Korean (16 × 2 with half-width spaces).
        var korean = Words("안녕하세요 여러분 오늘은 서울에서 가장 유명한 시장을 함께 둘러보겠습니다", 0, "ko", 0.5);

        var cues = Segmenter.Segment(korean, new SegmentationOptions { Language = "tr" });

        cues.Count.ShouldBeGreaterThan(1);
        cues.ShouldAllBe(c => c.Language == "ko");
        cues.ShouldAllBe(c => c.Text.Split('\n').All(line => line.Length <= 20));
    }

    [Fact]
    public void Words_without_languages_leave_cue_languages_empty()
    {
        var words = new List<Word> { new("Merhaba.", TimeSpan.Zero, TimeSpan.FromSeconds(0.5)) };

        Segmenter.Segment(words).ShouldHaveSingleItem().Language.ShouldBeNull();
    }

    [Fact]
    public void A_mixed_run_becomes_a_track_in_the_language_spoken_most()
    {
        var project = new Project();
        var run = new AsrRun
        {
            Engine = "whisper",
            Model = "large-v3",
            Language = "tr+en+ko",
            Words = [.. Words("Merhaba arkadaşlar bugün çok güzel bir gün.", 0, "tr"), .. Words("Thank you.", 4, "en")],
        };

        var (track, _) = AsrTracks.Add(project, run, "Whisper (yerel)");

        track.Language.ShouldBe("tr");
        track.IsMixedLanguage().ShouldBeTrue();
        track.SpokenLanguages().ShouldBe(["tr", "en"]);
        track.LanguageOf(track.Cues[^1]).ShouldBe("en");
    }

    [Fact]
    public void A_mixed_track_splits_into_one_track_per_language_for_export()
    {
        var track = new SubtitleTrack { Name = "Kaynak", Language = "tr" };
        track.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Merhaba");
        track.AddCue(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Hello").Language = "en";
        track.AddCue(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), "Nasılsın?").Language = "tr";

        var parts = TrackOperations.SplitByLanguage(track);

        parts.Select(p => (p.Language, p.Cues.Count)).ShouldBe([("tr", 2), ("en", 1)]);
        parts.SelectMany(p => p.Cues).ShouldAllBe(c => c.Language == null);
        track.Cues[1].Language.ShouldBe("en"); // the original is untouched
        TrackOperations.SplitByLanguage(new SubtitleTrack()).ShouldHaveSingleItem();
    }

    [Fact]
    public void Split_cues_keep_their_language()
    {
        var track = new SubtitleTrack { Language = "tr" };
        var cue = track.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(4), "Thank you very much");
        cue.Language = "en";

        var second = CueEditing.Split(track, cue, TimeSpan.FromSeconds(2)).ShouldNotBeNull();

        second.Language.ShouldBe("en");
        cue.Clone().Language.ShouldBe("en");
    }
}
