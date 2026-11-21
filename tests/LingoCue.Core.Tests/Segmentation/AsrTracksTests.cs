using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Segmentation;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Segmentation;

public class AsrTracksTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void A_run_becomes_the_active_source_track_and_replaces_the_empty_placeholder()
    {
        var project = new Project();
        project.EnsureActiveTrack(); // the empty track a new project starts with
        var run = new AsrRun
        {
            Engine = "whisper",
            Model = "large-v3-q5_0",
            Language = "tr",
            Words = [new Word("Merhaba,", S(0), S(0.5), 0.9), new Word("dünya.", S(0.5), S(1), 0.1)],
        };

        var (track, doubtful) = AsrTracks.Add(project, run, "Whisper (yerel)");

        project.Tracks.ShouldHaveSingleItem().ShouldBe(track);
        project.ActiveTrackId.ShouldBe(track.Id);
        track.Name.ShouldBe("Kaynak · Whisper large-v3-q5_0");
        (track.Kind, track.Language).ShouldBe((TrackKind.Source, "tr"));
        doubtful.ShouldBe(1); // "dünya." is under the review threshold
    }

    [Fact]
    public void Export_names_add_the_track_name_only_when_languages_repeat()
    {
        var tr = new SubtitleTrack { Name = "Kaynak · Whisper", Language = "tr" };
        var fixedTr = new SubtitleTrack { Name = "Düzeltilmiş", Language = "tr" };
        var en = new SubtitleTrack { Name = "Çeviri · en", Language = "en" };

        SubtitleExportNames.For("film", [tr, en], en).ShouldBe("film.en");
        SubtitleExportNames.For("film", [tr, fixedTr, en], tr).ShouldBe("film.tr.Kaynak-Whisper");
        SubtitleExportNames.For("film", [tr, fixedTr], fixedTr).ShouldBe("film.tr.Düzeltilmiş");
        SubtitleExportNames.BaseName(null, @"C:\x\proje.lingocue").ShouldBe("proje");
        SubtitleExportNames.BaseName(null, null).ShouldBe("altyazi");
    }
}
