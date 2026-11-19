using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using Shouldly;

namespace LingoCue.Core.Tests.Projects;

public class TrackOperationsTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    /// <summary>ASR → Düzeltilmiş → English, each cue linked to the one before.</summary>
    private static (Project Project, SubtitleTrack Asr, SubtitleTrack Corrected, SubtitleTrack English) Chain()
    {
        var project = new Project();
        var asr = new SubtitleTrack { Name = "Kaynak", Language = "tr" };
        asr.AddCue(S(1), S(2), "bogaz");
        asr.AddCue(S(3), S(4), "merhaba");
        var corrected = Derived(asr, TrackKind.Corrected, "tr", t => t.ToUpperInvariant());
        var english = Derived(corrected, TrackKind.Translation, "en", t => "EN " + t);
        project.Tracks.AddRange([asr, corrected, english]);
        project.ActiveTrackId = english.Id;
        return (project, asr, corrected, english);

        static SubtitleTrack Derived(SubtitleTrack source, TrackKind kind, string language, Func<string, string> text)
        {
            var track = new SubtitleTrack { Name = kind.ToString(), Language = language, Kind = kind, SourceTrackId = source.Id };
            foreach (var cue in source.Cues)
            {
                var copy = cue.Clone();
                copy.SourceCueId = cue.Id;
                copy.Text = text(cue.Text);
                track.Cues.Add(copy);
            }

            return track;
        }
    }

    [Fact]
    public void Deleting_a_middle_track_relinks_its_children_to_the_origin()
    {
        var (project, asr, corrected, english) = Chain();

        TrackOperations.Delete(project, corrected);

        project.Tracks.ShouldBe([asr, english]);
        english.SourceTrackId.ShouldBe(asr.Id);
        english.Cues.Select(c => c.SourceCueId).ShouldBe(asr.Cues.Select(c => (int?)c.Id));
        TrackOperations.OriginalText(project, english, english.Cues[0]).ShouldBe("bogaz");
    }

    [Fact]
    public void Deleting_the_first_track_unlinks_its_children_and_fixes_active_and_bilingual()
    {
        var (project, asr, corrected, english) = Chain();
        project.ActiveTrackId = asr.Id;
        project.Presentation = new PresentationOptions { Bilingual = new BilingualOptions { Enabled = true, SecondaryTrackId = asr.Id } };

        TrackOperations.Delete(project, asr);

        corrected.SourceTrackId.ShouldBeNull();
        corrected.Cues.ShouldAllBe(c => c.SourceCueId == null);
        english.SourceTrackId.ShouldBe(corrected.Id); // untouched
        project.ActiveTrackId.ShouldBe(corrected.Id);
        project.Presentation.Bilingual.Enabled.ShouldBeFalse();
        project.Presentation.Bilingual.SecondaryTrackId.ShouldBeNull();
    }

    [Fact]
    public void Timing_fixed_in_the_source_can_be_pulled_into_a_derived_track()
    {
        var (project, _, corrected, english) = Chain();
        corrected.Cues[0].Start = S(0.8);
        corrected.Cues[0].End = S(2.4);
        english.Cues.Add(new Cue(99, S(5), S(6), "extra line without origin"));

        var (updated, skipped) = TrackOperations.SyncTimingFromSource(project, english);

        updated.ShouldBe(1);
        skipped.ShouldBe(1);
        (english.Cues[0].Start, english.Cues[0].End).ShouldBe((S(0.8), S(2.4)));
        english.Cues[0].Text.ShouldBe("EN BOGAZ"); // text untouched
    }

    [Fact]
    public void Duplicate_copies_cues_and_links_and_is_placed_after_the_original()
    {
        var (project, _, corrected, english) = Chain();

        var copy = TrackOperations.Duplicate(project, corrected);

        project.Tracks.IndexOf(copy).ShouldBe(project.Tracks.IndexOf(corrected) + 1);
        copy.Id.ShouldNotBe(corrected.Id);
        copy.Name.ShouldBe("Corrected (kopya)");
        copy.Cues.Select(c => c.Text).ShouldBe(corrected.Cues.Select(c => c.Text));
        copy.Cues[0].ShouldNotBeSameAs(corrected.Cues[0]);
        copy.SourceTrackId.ShouldBe(corrected.SourceTrackId);
        project.ActiveTrackId.ShouldBe(copy.Id);
        english.SourceTrackId.ShouldBe(corrected.Id); // children stay on the original
    }

    [Fact]
    public void Move_reorders_within_bounds()
    {
        var (project, asr, corrected, english) = Chain();

        TrackOperations.Move(project, english, -1);
        project.Tracks.ShouldBe([asr, english, corrected]);
        TrackOperations.Move(project, asr, -1);
        project.Tracks.ShouldBe([asr, english, corrected]);
    }

    [Theory]
    [InlineData("film.en.srt", "en")]
    [InlineData("film.pt-BR.vtt", "pt-BR")]
    [InlineData("Gezi 2025.tur.ass", "tur")]
    [InlineData("film.srt", null)]
    [InlineData("film.final.srt", null)]
    [InlineData("v1.2.srt", null)]
    public void Language_is_read_from_the_file_name(string name, string? expected)
        => TrackOperations.LanguageFromFileName(name).ShouldBe(expected);

    [Fact]
    public void Imported_cues_become_a_new_active_track()
    {
        var project = new Project();
        project.EnsureActiveTrack().AddCue(S(0), S(1), "mevcut");

        var track = TrackOperations.AddImported(project, [new Cue(5, S(3), S(4), "b"), new Cue(7, S(1), S(2), "a")], "film.en.srt");

        project.Tracks.Count.ShouldBe(2);
        project.ActiveTrackId.ShouldBe(track.Id);
        track.Language.ShouldBe("en");
        track.Name.ShouldBe("film.en");
        track.Cues.Select(c => c.Text).ShouldBe(["a", "b"]);
        project.Tracks[0].Cues.ShouldHaveSingleItem().Text.ShouldBe("mevcut");
    }
}
