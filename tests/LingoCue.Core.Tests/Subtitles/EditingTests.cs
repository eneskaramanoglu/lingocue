using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;
using Shouldly;

namespace LingoCue.Core.Tests.Subtitles;

public class CueEditingTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    private static SubtitleTrack Track(params (double Start, double End, string Text)[] cues)
    {
        var track = new SubtitleTrack();
        foreach (var (start, end, text) in cues)
        {
            track.AddCue(S(start), S(end), text);
        }

        return track;
    }

    [Fact]
    public void Split_uses_the_word_spoken_at_the_playhead()
    {
        var track = Track((1, 4, "bugün hava çok güzel"));
        var cue = track.Cues[0];
        cue.Words.AddRange([new Word("bugün", S(1), S(1.5)), new Word("hava", S(1.6), S(2)), new Word("çok", S(2.4), S(2.7)), new Word("güzel", S(2.8), S(3.5))]);

        var second = CueEditing.Split(track, cue, S(2.3))!;

        cue.Text.ShouldBe("bugün hava");
        (cue.Start, cue.End).ShouldBe((S(1), S(2.3)));
        second.Text.ShouldBe("çok güzel");
        (second.Start, second.End).ShouldBe((S(2.3), S(4)));
        second.Words.Select(w => w.Text).ShouldBe(["çok", "güzel"]);
        cue.Words.Count.ShouldBe(2);
        track.Cues.ShouldBe([cue, second]);
        second.Id.ShouldNotBe(cue.Id);
    }

    [Fact]
    public void Split_at_the_caret_or_in_the_middle_without_word_timings()
    {
        var track = Track((0, 4, "bir iki üç dört"), (5, 9, "beş altı yedi sekiz"));

        CueEditing.Split(track, track.Cues[0], S(2), textIndex: 4)!.Text.ShouldBe("iki üç dört");
        track.Cues[0].Text.ShouldBe("bir");

        var second = CueEditing.Split(track, track.Cues[2], S(100))!; // playhead outside: middle of the cue
        (second.Start).ShouldBe(S(7));
        track.Cues[2].Text.ShouldBe("beş altı");
        second.Text.ShouldBe("yedi sekiz");
    }

    [Fact]
    public void Too_short_cues_are_not_split()
        => CueEditing.Split(Track((0, 0.3, "kısa")), new SubtitleTrack().AddCue(S(0), S(0.3), "x"), S(0.1)).ShouldBeNull();

    [Fact]
    public void Merge_joins_on_one_line_when_it_fits_otherwise_two()
    {
        var track = Track((0, 1, "Merhaba"), (1.2, 2, "dünya."), (3, 4, "Bu çok uzun bir satır olacak ve sığmayacak"), (4.2, 5, "çünkü bu da uzun bir satır"));
        track.Cues[1].Marks = CueMarks.NeedsReview;

        CueEditing.MergeWithNext(track, track.Cues[0]).ShouldBeTrue();
        track.Cues[0].Text.ShouldBe("Merhaba dünya.");
        track.Cues[0].End.ShouldBe(S(2));
        track.Cues[0].Marks.ShouldBe(CueMarks.NeedsReview);

        CueEditing.MergeWithNext(track, track.Cues[1]).ShouldBeTrue();
        track.Cues[1].Text.ShouldBe("Bu çok uzun bir satır olacak ve sığmayacak\nçünkü bu da uzun bir satır");
        CueEditing.MergeWithNext(track, track.Cues[1]).ShouldBeFalse(); // last cue
    }

    [Fact]
    public void Nudge_moves_without_overlapping_neighbours()
    {
        var track = Track((0, 1, "a"), (1.5, 2.5, "b"), (2.6, 3, "c"));
        var b = track.Cues[1];

        CueEditing.Nudge(track, b, TimeSpan.FromMilliseconds(-100)).ShouldBe(TimeSpan.FromMilliseconds(-100));
        b.Start.ShouldBe(S(1.4));
        CueEditing.Nudge(track, b, TimeSpan.FromMilliseconds(500)).ShouldBe(S(2.6) - CueEditing.MinGap - S(2.4)); // stops before c
        b.End.ShouldBe(S(2.6) - CueEditing.MinGap);

        CueEditing.Nudge(track, track.Cues[0], S(-5)).ShouldBe(TimeSpan.Zero); // already at zero
    }

    [Fact]
    public void Nudging_only_the_start_keeps_a_minimum_duration()
    {
        var track = Track((1, 2, "a"));
        CueEditing.Nudge(track, track.Cues[0], S(5), startOnly: true);
        track.Cues[0].Start.ShouldBe(S(2) - CueEditing.MinDuration);
        track.Cues[0].End.ShouldBe(S(2));
    }

    [Fact]
    public void Dragging_the_body_stops_at_the_neighbours_and_media_bounds()
    {
        var track = Track((1, 2, "a"), (3, 4, "b"), (6, 7, "c"));
        var middle = track.Cues[1];

        CueEditing.ClampDrag(track, middle, DragHandle.Body, S(-5)).ShouldBe((S(2) + CueEditing.MinGap, S(3) + CueEditing.MinGap));
        CueEditing.ClampDrag(track, middle, DragHandle.Body, S(5)).ShouldBe((S(5) - CueEditing.MinGap, S(6) - CueEditing.MinGap));
        CueEditing.ClampDrag(track, middle, DragHandle.Body, S(0.5)).ShouldBe((S(3.5), S(4.5)));
        CueEditing.ClampDrag(track, track.Cues[2], DragHandle.Body, S(10), mediaEnd: S(8)).ShouldBe((S(7), S(8)));
        CueEditing.ClampDrag(track, track.Cues[0], DragHandle.Body, S(-3)).ShouldBe((S(0), S(1)));
        (middle.Start, middle.End).ShouldBe((S(3), S(4))); // nothing changed
    }

    [Fact]
    public void Dragging_an_edge_keeps_the_minimum_duration_and_gap()
    {
        var track = Track((1, 2, "a"), (3, 4, "b"));
        var second = track.Cues[1];

        CueEditing.ClampDrag(track, second, DragHandle.Start, S(-2)).ShouldBe((S(2) + CueEditing.MinGap, S(4)));
        CueEditing.ClampDrag(track, second, DragHandle.Start, S(2)).ShouldBe((S(4) - CueEditing.MinDuration, S(4)));
        CueEditing.ClampDrag(track, track.Cues[0], DragHandle.End, S(5)).ShouldBe((S(1), S(3) - CueEditing.MinGap));
        CueEditing.ClampDrag(track, track.Cues[0], DragHandle.End, S(-5)).ShouldBe((S(1), S(1) + CueEditing.MinDuration));
        CueEditing.ClampDrag(track, second, DragHandle.End, S(100)).ShouldBe((S(3), S(104))); // no media length known
    }

    [Fact]
    public void Dragging_never_pulls_a_cue_into_an_existing_overlap()
    {
        var track = Track((1, 3.5, "a"), (3, 4, "b")); // already overlapping
        CueEditing.ClampDrag(track, track.Cues[1], DragHandle.Body, S(-1)).ShouldBe((S(3), S(4)));
        CueEditing.ClampDrag(track, track.Cues[1], DragHandle.Body, S(1)).ShouldBe((S(4), S(5)));
    }
}

public class QualityCheckTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Reports_reading_rules_overlaps_and_small_gaps_with_line_numbers()
    {
        var track = new SubtitleTrack();
        track.AddCue(S(0), S(1), "Bu satır bir saniyede okunamayacak kadar uzun bir metin");
        track.AddCue(S(0.9), S(2), "çakışan");
        track.AddCue(S(2.03), S(4), "yakın");
        track.AddCue(S(5), S(6), "  ");

        var issues = QualityCheck.Check(track, ReadingRules.Default);

        issues.ShouldContain(i => i.Number == 1 && i.Kind == nameof(CueIssues.ReadingSpeedTooHigh) && i.Severity == QaSeverity.Error);
        issues.ShouldContain(i => i.Number == 1 && i.Kind == "Overlap" && i.Message.Contains("100 ms"));
        issues.ShouldContain(i => i.Number == 2 && i.Kind == "SmallGap");
        issues.ShouldContain(i => i.Number == 4 && i.Kind == nameof(CueIssues.Empty));
        issues.ShouldNotContain(i => i.Number == 3 && i.Kind == "SmallGap");
    }

    [Fact]
    public void Quick_fixes_remove_overlaps_and_lengthen_fast_lines()
    {
        var track = new SubtitleTrack();
        track.AddCue(S(0), S(1.2), "çakışıyor");
        track.AddCue(S(1), S(2), "Bu satır epey uzun bir cümle ve hızlı okunuyor");
        track.AddCue(S(6), S(7), "son");

        QualityCheck.FixOverlaps(track).ShouldBe(1);
        track.Cues[0].End.ShouldBe(S(1) - CueEditing.MinGap);

        QualityCheck.ExtendForReadingSpeed(track, ReadingRules.Default).ShouldBe(1);
        track.Cues[1].CharactersPerSecond.ShouldBeLessThanOrEqualTo(ReadingRules.Default.TargetCps + 0.01);
        track.Cues[1].End.ShouldBeLessThan(S(6));
    }
}

public class TextSearchTests
{
    private static List<Cue> Cues(params string[] texts)
        => [.. texts.Select((t, i) => new Cue(i + 1, TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1), t))];

    [Fact]
    public void Ignores_case_with_turkish_rules()
    {
        var cues = Cues("İSTANBUL güzel", "Istanbul değil", "istanbul'da");

        var matches = TextSearch.FindAll(cues, "istanbul");

        matches.Select(m => m.Cue.Id).ShouldBe([1, 3]); // "Istanbul" (dotless I) is a different word
        TextSearch.FindAll(cues, "ISTANBUL", matchCase: true).ShouldBeEmpty();
        TextSearch.FindAll(cues, "ılık").ShouldBeEmpty();
    }

    [Fact]
    public void Whole_word_and_replace_all()
    {
        var cues = Cues("kalem kalemlik kalem", "Kalem");

        TextSearch.FindAll(cues, "kalem", wholeWord: true).Count.ShouldBe(3);
        TextSearch.ReplaceAll(cues, "kalem", "defter", wholeWord: true).ShouldBe(3);

        cues[0].Text.ShouldBe("defter kalemlik defter");
        cues[1].Text.ShouldBe("defter");
    }
}
