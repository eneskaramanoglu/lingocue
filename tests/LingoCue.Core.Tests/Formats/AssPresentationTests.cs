using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using Shouldly;

namespace LingoCue.Core.Tests.Formats;

public class AssPresentationTests
{
    private static (Project Project, SubtitleTrack Track) Sample(string text, string language = "tr")
    {
        var project = new Project();
        var track = project.EnsureActiveTrack();
        track.Language = language;
        track.AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), text);
        return (project, track);
    }

    [Fact]
    public void Upper_case_uses_turkish_rules_and_keeps_tags()
    {
        var (project, track) = Sample("<i>istanbul</i> ılık\n{\\b1}şiir{\\b0}");
        project.Presentation = new PresentationOptions { UpperCase = true };

        var ass = AssScriptBuilder.Build(project, track);

        ass.ShouldContain(@"{\i1}İSTANBUL{\i0} ILIK\N{\b1}ŞİİR{\b0}");
    }

    [Fact]
    public void Upper_case_follows_the_track_language()
    {
        var (project, track) = Sample("big city", "en");
        project.Presentation = new PresentationOptions { UpperCase = true };

        AssScriptBuilder.Build(project, track).ShouldContain(",BIG CITY");
    }

    [Fact]
    public void Upper_case_follows_each_line_language_in_a_mixed_track()
    {
        var (project, track) = Sample("istanbul");
        track.AddCue(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "this is it").Language = "en";
        project.Presentation = new PresentationOptions { UpperCase = true };

        var ass = AssScriptBuilder.Build(project, track);

        ass.ShouldContain(",İSTANBUL");
        ass.ShouldContain(",THIS IS IT");
    }

    [Fact]
    public void Fade_is_prepended_to_every_event()
    {
        var (project, track) = Sample("Merhaba");
        project.Presentation = new PresentationOptions { FadeInMs = 150, FadeOutMs = 200 };

        AssScriptBuilder.Build(project, track).ShouldContain(@",{\fad(150,200)}Merhaba");
    }

    [Fact]
    public void No_presentation_options_leave_text_as_is()
    {
        var (project, track) = Sample("Merhaba");

        var ass = AssScriptBuilder.Build(project, track);

        ass.ShouldContain(",,Merhaba");
        ass.ShouldNotContain(@"\fad");
    }

    [Fact]
    public void Map_visible_skips_tags()
        => SubtitleText.MapVisible("a<b>b</b>{\\i1}c{\\i0}<x", s => s.ToUpperInvariant())
            .ShouldBe("A<b>B</b>{\\i1}C{\\i0}<X");

    [Fact]
    public void Presentation_round_trips_through_the_project_file()
    {
        var (project, _) = Sample("x");
        var secondary = Guid.NewGuid();
        project.Presentation = new PresentationOptions
        {
            UpperCase = true,
            FadeInMs = 100,
            Karaoke = new KaraokeOptions { Mode = KaraokeMode.ActiveWord, HighlightColor = new AssColor(1, 2, 3) },
            Bilingual = new BilingualOptions { Enabled = true, SecondaryTrackId = secondary, Gap = 20 },
        };

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        loaded.Presentation.ShouldBe(project.Presentation);
    }
}
