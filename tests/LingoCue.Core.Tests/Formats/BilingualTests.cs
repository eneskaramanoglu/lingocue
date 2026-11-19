using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using Shouldly;

namespace LingoCue.Core.Tests.Formats;

public class BilingualTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static (Project Project, SubtitleTrack Turkish, SubtitleTrack English) Sample(int alignment = 2)
    {
        var project = new Project { Style = new SubtitleStyle { FontSize = 50, Outline = 2, MarginV = 40, Alignment = alignment } };
        var tr = new SubtitleTrack { Name = "TR", Language = "tr" };
        tr.AddCue(S(1), S(3), "Merhaba dünya");
        var en = new SubtitleTrack { Name = "EN", Language = "en", Kind = TrackKind.Translation, SourceTrackId = tr.Id };
        en.AddCue(S(1), S(3), "Hello world\nsecond line");
        project.Tracks.AddRange([tr, en]);
        project.ActiveTrackId = tr.Id;
        project.Presentation = new PresentationOptions
        {
            Bilingual = new BilingualOptions
            {
                Enabled = true,
                SecondaryTrackId = en.Id,
                Gap = 10,
                SecondaryStyle = BilingualOptions.DefaultSecondaryStyle with { FontSize = 30, Outline = 1 },
            },
        };
        return (project, tr, en);
    }

    [Fact]
    public void Secondary_track_goes_below_on_its_own_layer_and_main_is_lifted_above_it()
    {
        var (project, tr, _) = Sample();

        var ass = AssScriptBuilder.Build(project, tr);

        // English: 2 lines × 30 + 2×1 outline = 62 px high, at the main bottom margin (40).
        ass.ShouldContain("Style: Secondary,Arial,30,");
        ass.ShouldContain(@"Dialogue: 0,0:00:01.00,0:00:03.00,Secondary,,0,0,40,,Hello world\Nsecond line");
        // Turkish: 40 + 62 + 10 gap = 112.
        ass.ShouldContain("Dialogue: 1,0:00:01.00,0:00:03.00,Default,,0,0,112,,Merhaba dünya");
    }

    [Fact]
    public void Top_alignment_puts_the_secondary_track_under_the_main_one()
    {
        var (project, tr, _) = Sample(alignment: 8);

        var ass = AssScriptBuilder.Build(project, tr);

        // Turkish: 1 line × 50 + 2×2 = 54 px at the top margin 40 → English at 40 + 54 + 10 = 104.
        ass.ShouldContain("Dialogue: 1,0:00:01.00,0:00:03.00,Default,,0,0,0,,Merhaba dünya");
        ass.ShouldContain("Dialogue: 0,0:00:01.00,0:00:03.00,Secondary,,0,0,104,,");
    }

    [Fact]
    public void Secondary_style_follows_the_main_alignment_and_side_margins()
    {
        var (project, tr, _) = Sample(alignment: 1);
        project.Style = project.Style with { MarginL = 80, MarginR = 70 };

        var document = AssScriptBuilder.BuildDocument(project, tr);

        var secondary = document.Styles.Single(s => s.Name == AssScriptBuilder.SecondaryStyleName);
        secondary.Alignment.ShouldBe(1);
        secondary.MarginL.ShouldBe(80);
        secondary.MarginR.ShouldBe(70);
    }

    [Fact]
    public void Main_cue_without_a_secondary_line_keeps_the_style_margin()
    {
        var (project, tr, en) = Sample();
        en.Cues.Clear();

        AssScriptBuilder.Build(project, tr).ShouldContain("Dialogue: 1,0:00:01.00,0:00:03.00,Default,,0,0,0,,Merhaba dünya");
    }

    [Fact]
    public void Off_missing_or_same_track_means_a_single_track_script()
    {
        var (project, tr, _) = Sample();

        project.Presentation = project.Presentation with { Bilingual = project.Presentation.Bilingual with { Enabled = false } };
        AssScriptBuilder.Build(project, tr).ShouldNotContain("Style: Secondary");

        project.Presentation = project.Presentation with { Bilingual = project.Presentation.Bilingual with { Enabled = true, SecondaryTrackId = Guid.NewGuid() } };
        AssScriptBuilder.Build(project, tr).ShouldNotContain("Style: Secondary");

        project.Presentation = project.Presentation with { Bilingual = project.Presentation.Bilingual with { SecondaryTrackId = tr.Id } };
        AssScriptBuilder.Build(project, tr).ShouldNotContain("Style: Secondary");
    }

    [Fact]
    public void Karaoke_applies_to_the_main_track_only()
    {
        var (project, tr, en) = Sample();
        project.Presentation = project.Presentation with { Karaoke = new KaraokeOptions { Mode = KaraokeMode.Fill } };
        tr.Cues[0].Words.AddRange([new Word("merhaba", S(1), S(1.5)), new Word("dünya", S(1.6), S(2))]);
        en.Cues[0].Words.AddRange([new Word("hello", S(1), S(1.5)), new Word("world", S(1.6), S(2))]);

        var ass = AssScriptBuilder.Build(project, tr);

        ass.ShouldContain(@"{\kf");
        ass.ShouldContain(",Secondary,,0,0,40,,Hello world");
    }

    [Fact]
    public void Long_lines_are_counted_as_wrapped()
        => AssScriptBuilder.TextHeight(new string('a', 200), new SubtitleStyle { FontSize = 40, Outline = 0, MarginL = 60, MarginR = 60 }, 1920)
            .ShouldBe(120); // 200 × 20 px ≈ 4000 px over 1800 px → 3 lines × 40
}
