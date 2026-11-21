using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Formats;

public class BackgroundBoxTests
{
    private static readonly AssColor s_magenta = new(255, 0, 255, 200);

    private static Project WithBackground(SubtitleBackground background, string text = "Merhaba")
    {
        var project = new Project { Style = new SubtitleStyle { FontSize = 60, MarginV = 40, Background = background } };
        project.EnsureActiveTrack().AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), text);
        return project;
    }

    private static SubtitleDocument Build(Project project) => AssScriptBuilder.BuildDocument(project, project.Tracks[0]);

    [Fact]
    public void Without_a_background_the_script_is_unchanged()
    {
        var document = Build(WithBackground(new SubtitleBackground()));

        document.Styles[0].BorderStyle.ShouldBe(AssBorderStyle.Outline);
        var cue = document.Cues.ShouldHaveSingleItem();
        cue.Text.ShouldBe("Merhaba");
        cue.Format.Layer.ShouldBe(0);
    }

    [Fact]
    public void A_block_box_is_one_libass_rectangle_with_its_own_padding()
    {
        var document = Build(WithBackground(new SubtitleBackground { Kind = BackgroundKind.BlockBox, Color = s_magenta, PaddingX = 30, PaddingY = 12 }));

        var style = document.Styles[0];
        style.BorderStyle.ShouldBe(AssBorderStyle.Box);
        style.BackColor.ShouldBe(s_magenta);
        style.Outline.ShouldBe(2.5); // the text keeps its outline
        document.Cues.ShouldHaveSingleItem().Text.ShouldBe(@"{\xshad30\yshad12}Merhaba");
        SubtitleFormats.Ass.Write(document).ShouldContain("Style: Default,Arial,60,&H00FFFFFF,&H0000FFFF,&H00000000,&H37FF00FF,0,0,0,0,100,100,0,0,4,");
    }

    [Fact]
    public void A_line_box_paints_each_line_with_the_box_color()
    {
        var document = Build(WithBackground(new SubtitleBackground { Kind = BackgroundKind.LineBox, Color = s_magenta, PaddingX = 20, PaddingY = 6 }));

        var style = document.Styles[0];
        style.BorderStyle.ShouldBe(AssBorderStyle.OpaqueBox);
        style.OutlineColor.ShouldBe(s_magenta);
        style.Shadow.ShouldBe(0);
        document.Cues.ShouldHaveSingleItem().Text.ShouldBe(@"{\xbord20\ybord6}Merhaba");
    }

    [Fact]
    public void A_frame_is_a_larger_box_with_invisible_text_on_the_layer_below()
    {
        var document = Build(WithBackground(new SubtitleBackground
        {
            Kind = BackgroundKind.BlockBox, PaddingX = 30, PaddingY = 12, FrameWidth = 4, FrameColor = AssColor.White,
        }));

        document.Cues.Count.ShouldBe(2);
        var frame = document.Cues[0];
        var text = document.Cues[1];
        frame.Format.Layer.ShouldBe(0);
        text.Format.Layer.ShouldBe(1);
        frame.Text.ShouldBe(@"{\1a&HFF&\2a&HFF&\3a&HFF&\4c&HFFFFFF&\4a&H00&}{\xshad34\yshad16}Merhaba");
        text.Text.ShouldBe(@"{\xshad30\yshad12}Merhaba");
    }

    [Fact]
    public void A_band_spans_the_video_width_behind_the_text()
    {
        var document = Build(WithBackground(new SubtitleBackground { Kind = BackgroundKind.Band, Color = s_magenta, PaddingY = 8 }, "Bir\nİki"));

        document.Cues.Count.ShouldBe(2);
        var band = document.Cues[0];
        // Two 60 px lines + 2 × 8 padding = 136 high; bottom of the text at 1080 - 40.
        band.Text.ShouldBe(@"{\an7\pos(0,912)\bord0\shad0\blur0\1c&HFF00FF&\1a&H37&\p1}m 0 0 l 1920 0 l 1920 136 l 0 136{\p0}");
        band.Format.Layer.ShouldBe(0);
        document.Cues[1].Text.ShouldBe("Bir\nİki");
        document.Cues[1].Format.Layer.ShouldBe(1);
    }

    [Fact]
    public void Bilingual_tracks_keep_separate_layers_with_backgrounds()
    {
        var project = WithBackground(new SubtitleBackground { Kind = BackgroundKind.Band });
        var english = new SubtitleTrack { Language = "en", Kind = TrackKind.Translation };
        english.AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Hello");
        project.Tracks.Add(english);
        project.Presentation = project.Presentation with { Bilingual = new BilingualOptions { Enabled = true, SecondaryTrackId = english.Id } };

        var layers = Build(project).Cues.Select(c => (c.Text.Contains(@"\p1", StringComparison.Ordinal), c.Format.Layer)).ToList();

        // Main track: band 2, text 3. Second track has no background: text 1 (above nothing of the main track).
        layers.ShouldContain((true, 2));
        layers.ShouldContain((false, 3));
        layers.ShouldContain((false, 1));
    }

    [Fact]
    public void Imported_borderstyle_4_is_kept()
    {
        const string ass = """
            [Script Info]
            ScriptType: v4.00+

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,4,2,6,2,10,10,10,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Merhaba
            """;

        var document = SubtitleFormats.Ass.Parse(ass);

        document.Styles.ShouldHaveSingleItem().BorderStyle.ShouldBe(AssBorderStyle.Box);
        SubtitleFormats.Ass.Write(document).ShouldContain(",0,0,4,2,6,2,");
    }

    [Fact]
    public void Normalizing_an_imported_style_scales_the_box_too()
    {
        var style = new SubtitleStyle { Background = new SubtitleBackground { Kind = BackgroundKind.BlockBox, PaddingX = 20, PaddingY = 10, FrameWidth = 2 } };

        var normalized = AssScriptBuilder.NormalizeStyle(style, 720).Background;

        (normalized.PaddingX, normalized.PaddingY, normalized.FrameWidth).ShouldBe((30, 15, 3));
    }
}
