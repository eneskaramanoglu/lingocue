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

    [Fact]
    public void Korean_counts_latin_spaces_and_punctuation_as_half()
    {
        var korean = Core.Settings.ReadingRulesSettings.KoreanRules(ReadingRules.Default);

        korean.TextLength("안녕하세요").ShouldBe(5);
        korean.TextLength("안녕, OK!").ShouldBe(2 + 0.5 + 0.5 + 1 + 0.5);  // 안녕 + "," + " " + "OK" + "!"
        korean.TextLength("첫 줄\n둘째").ShouldBe(2.5 + 2);                // 첫 + space + 줄; the line break is not counted
        korean.LongestLine("<i>가나다라마바사아자차카타파하가나</i>").ShouldBe(16);
    }

    [Fact]
    public void Korean_rules_use_16_chars_per_line_and_12_cps()
    {
        var korean = new Core.Settings.ReadingRulesSettings().ToRules("ko");

        korean.MaxCharsPerLine.ShouldBe(16);
        korean.MaxCps.ShouldBe(12);
        korean.Counting.ShouldBe(LengthCounting.HalfWidthLatin);
        korean.Evaluate(Make(5, new string('가', 17))).HasFlag(CueIssues.LineTooLong).ShouldBeTrue();
        korean.Evaluate(Make(5, new string('가', 16))).HasFlag(CueIssues.LineTooLong).ShouldBeFalse();
        korean.Evaluate(Make(1, new string('가', 13))).HasFlag(CueIssues.ReadingSpeedTooHigh).ShouldBeTrue();

        // The same 30 Latin letters are fine in Korean counting (15) but too long for 16 chars in Turkish counting.
        korean.Evaluate(Make(5, new string('a', 30))).HasFlag(CueIssues.LineTooLong).ShouldBeFalse();
        new Core.Settings.ReadingRulesSettings().ToRules("tr").Counting.ShouldBe(LengthCounting.Characters);
    }
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
