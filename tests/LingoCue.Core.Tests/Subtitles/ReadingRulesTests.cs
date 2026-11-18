using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Subtitles;

public class ReadingRulesTests
{
    private static Cue Make(double seconds, string text) => new(1, TimeSpan.Zero, TimeSpan.FromSeconds(seconds), text);

    [Fact]
    public void A_comfortable_cue_has_no_issues()
        => ReadingRules.Default.Evaluate(Make(2, "Merhaba, nasılsın?")).ShouldBe(CueIssues.None);

    [Theory]
    [InlineData(1, 18, CueIssues.ReadingSpeedHigh)]     // 18 CPS
    [InlineData(1, 25, CueIssues.ReadingSpeedTooHigh)]  // 25 CPS
    public void Flags_reading_speed(double seconds, int chars, CueIssues expected)
        => ReadingRules.Default.Evaluate(Make(seconds, new string('a', chars))).HasFlag(expected).ShouldBeTrue();

    [Fact]
    public void Flags_long_lines_too_many_lines_and_durations()
    {
        var rules = ReadingRules.Default;

        rules.Evaluate(Make(5, new string('a', 43))).HasFlag(CueIssues.LineTooLong).ShouldBeTrue();
        rules.Evaluate(Make(5, "a\nb\nc")).HasFlag(CueIssues.TooManyLines).ShouldBeTrue();
        rules.Evaluate(Make(0.5, "a")).HasFlag(CueIssues.TooShort).ShouldBeTrue();
        rules.Evaluate(Make(8, "a")).HasFlag(CueIssues.TooLong).ShouldBeTrue();
        rules.Evaluate(Make(2, "{\\i1} {\\i0}")).HasFlag(CueIssues.Empty).ShouldBeTrue();
    }

    [Fact]
    public void Tags_do_not_count_toward_line_length()
        => ReadingRules.Default.Evaluate(Make(5, "<i>" + new string('a', 42) + "</i>")).HasFlag(CueIssues.LineTooLong).ShouldBeFalse();
}

public class AssScriptBuilderTests
{
    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(1280, 720, 1920, 1080)]
    [InlineData(1080, 1920, 1080, 1920)] // Shorts 9:16
    [InlineData(1440, 1080, 1440, 1080)] // 4:3
    [InlineData(0, 0, 1920, 1080)]
    public void Play_res_keeps_1080_reference(int w, int h, int expectedX, int expectedY)
        => AssScriptBuilder.PlayResFor(w, h).ShouldBe((expectedX, expectedY));

    [Fact]
    public void Builds_script_with_project_style_and_video_aspect()
    {
        var project = new Project { Style = new SubtitleStyle { FontName = "Inter", FontSize = 60 } };
        var track = project.EnsureActiveTrack();
        track.AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "<i>Merhaba</i>\nİstanbul");
        track.AddCue(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), "sıfır süre atlanır");
        track.Cues[0].Format = new CueFormatData { Style = "Eski" };

        var ass = AssScriptBuilder.Build(project, track, 1080, 1920);

        ass.ShouldContain("PlayResX: 1080\nPlayResY: 1920");
        ass.ShouldContain("Style: Default,Inter,60,");
        ass.ShouldContain(@"Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\i1}Merhaba{\i0}\Nİstanbul");
        ass.ShouldNotContain("sıfır süre");
        track.Cues[0].Format.Style.ShouldBe("Eski"); // the project itself is not modified
    }

    [Fact]
    public void Normalizes_imported_style_to_1080_reference()
    {
        var style = new SubtitleStyle { FontSize = 36, Outline = 2, MarginV = 20 };

        var normalized = AssScriptBuilder.NormalizeStyle(style, 720);

        normalized.FontSize.ShouldBe(54);
        normalized.Outline.ShouldBe(3);
        normalized.MarginV.ShouldBe(30);
        AssScriptBuilder.NormalizeStyle(style, 1080).ShouldBe(style);
    }
}
