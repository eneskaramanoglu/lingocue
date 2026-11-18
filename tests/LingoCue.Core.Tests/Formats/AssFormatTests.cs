using LingoCue.Core.Formats;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Formats;

public class AssFormatTests
{
    private readonly AssFormat _ass = new();

    private const string Sample = """
        [Script Info]
        ; comment
        Title: Örnek
        ScriptType: v4.00+
        PlayResX: 1280
        PlayResY: 720
        WrapStyle: 0
        ScaledBorderAndShadow: yes
        YCbCr Matrix: TV.709

        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Default,Arial,54,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,-1,0,0,0,100,100,0,0,1,3,1,2,40,40,50,1
        Style: Kutu,Inter,44,&H0000FFFF,&H000000FF,&H00000000,&HA0000000,0,-1,0,0,100,100,1.5,0,3,8,0,8,40,40,40,162

        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:01.00,0:00:03.50,Default,Ayşe,0,0,0,,Merhaba, İstanbul!\NGüzel bir gün.
        Dialogue: 1,0:00:04.00,0:00:06.25,Kutu,,10,20,30,Fade,{\k40}Kelime {\k40}kelime
        Comment: 0,0:00:07.00,0:00:08.00,Default,,0,0,0,,yorum satırı
        """;

    [Fact]
    public void Parses_script_info_styles_and_events()
    {
        var doc = _ass.Parse(Sample);

        doc.ScriptInfo!.Title.ShouldBe("Örnek");
        doc.ScriptInfo.PlayResX.ShouldBe(1280);
        doc.ScriptInfo.Extra.ShouldHaveSingleItem().Key.ShouldBe("YCbCr Matrix");

        doc.Styles.Count.ShouldBe(2);
        var box = doc.Styles[1];
        box.FontName.ShouldBe("Inter");
        box.Italic.ShouldBeTrue();
        box.Bold.ShouldBeFalse();
        box.BorderStyle.ShouldBe(AssBorderStyle.OpaqueBox);
        box.PrimaryColor.ShouldBe(new AssColor(255, 255, 0));
        box.BackColor.A.ShouldBe((byte)0x5F);
        box.Spacing.ShouldBe(1.5);
        box.Alignment.ShouldBe(8);

        doc.Cues.Count.ShouldBe(2); // Comment lines are not cues
        doc.Cues[0].Text.ShouldBe("Merhaba, İstanbul!\nGüzel bir gün."); // comma inside text survives
        doc.Cues[0].Speaker.ShouldBe("Ayşe");
        doc.Cues[1].Format.ShouldBe(new CueFormatData { Style = "Kutu", Layer = 1, MarginL = 10, MarginR = 20, MarginV = 30, Effect = "Fade" });
        doc.Cues[1].Text.ShouldBe("{\\k40}Kelime {\\k40}kelime");
    }

    [Fact]
    public void Round_trips_styles_events_and_extra_info()
    {
        var first = _ass.Parse(Sample);
        var second = _ass.Parse(_ass.Write(first));

        second.ScriptInfo.ShouldBe(first.ScriptInfo! with { Extra = second.ScriptInfo!.Extra });
        second.ScriptInfo!.Extra.ShouldBe(first.ScriptInfo!.Extra);
        second.Styles.ShouldBe(first.Styles);
        second.Cues.Select(c => (c.Start, c.End, c.Text, c.Speaker, c.Format))
            .ShouldBe(first.Cues.Select(c => (c.Start, c.End, c.Text, c.Speaker, c.Format)));
    }

    [Fact]
    public void Millisecond_times_move_at_most_5ms()
    {
        var doc = new SubtitleDocument();
        doc.Cues.Add(new Cue(1, TimeSpan.FromMilliseconds(1_234), TimeSpan.FromMilliseconds(5_678), "x"));

        var back = _ass.Parse(_ass.Write(doc)).Cues[0];

        (back.Start - TimeSpan.FromMilliseconds(1_234)).Duration().ShouldBeLessThanOrEqualTo(TimeSpan.FromMilliseconds(5));
        (back.End - TimeSpan.FromMilliseconds(5_678)).Duration().ShouldBeLessThanOrEqualTo(TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void Converts_html_tags_and_writes_a_default_style_when_none()
    {
        var doc = new SrtFormat().Parse("1\n00:00:01,000 --> 00:00:02,000\n<i>eğik</i>, <font color=\"#FF0000\">kırmızı</font>\nikinci\n");

        var written = _ass.Write(doc);

        written.ShouldContain("Style: Default,Arial,48,");
        written.ShouldContain(@"{\i1}eğik{\i0}; {\c&H0000FF&}kırmızı{\c}\Nikinci".Replace("; ", ", ", StringComparison.Ordinal));
    }

    [Fact]
    public void Commas_in_names_do_not_shift_fields()
    {
        var doc = new SubtitleDocument();
        doc.Cues.Add(new Cue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "metin") { Speaker = "Ali, Veli" });

        _ass.Parse(_ass.Write(doc)).Cues[0].Text.ShouldBe("metin");
    }

    [Fact]
    public void Reads_legacy_ssa_alignment()
    {
        const string ssa = """
            [Script Info]
            ScriptType: v4.00
            [V4 Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, TertiaryColour, BackColour, Bold, Italic, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, AlphaLevel, Encoding
            Style: Default,Arial,20,16777215,65535,0,0,0,0,1,2,0,6,10,10,10,0,0
            """;

        _ass.Parse(ssa).Styles[0].Alignment.ShouldBe(8); // SSA 6 = top center
    }
}

public class TagConverterTests
{
    [Theory]
    [InlineData("<i>a</i>", @"{\i1}a{\i0}")]
    [InlineData("<B>kalın</B>", @"{\b1}kalın{\b0}")]
    [InlineData("<font color=\"#0f0\">yeşil</font>", @"{\c&H00FF00&}yeşil{\c}")]
    [InlineData("<span>x</span>", "x")]
    [InlineData("düz metin", "düz metin")]
    public void Html_to_ass(string input, string expected)
        => TagConverter.HtmlToAss(input).ShouldBe(expected);

    [Theory]
    [InlineData(@"{\i1}a{\i0}", "<i>a</i>")]
    [InlineData(@"{\an8\b1}üst", "<b>üst</b>")]
    [InlineData(@"{\k40}Kelime {\k40}kelime", "Kelime kelime")]
    [InlineData("düz", "düz")]
    public void Ass_to_html(string input, string expected)
        => TagConverter.AssToHtml(input).ShouldBe(expected);
}
