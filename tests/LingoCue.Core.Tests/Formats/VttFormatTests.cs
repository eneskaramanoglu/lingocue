using LingoCue.Core.Formats;

namespace LingoCue.Core.Tests.Formats;

public class VttFormatTests
{
    private readonly VttFormat _vtt = new();

    private const string Sample = """
        WEBVTT - LingoCue örnek

        STYLE
        ::cue { color: yellow; }

        NOTE bu bir yorum

        giris
        00:00:01.000 --> 00:00:03.500 line:85% align:center
        <v Ayşe>Merhaba &amp; hoş geldiniz!
        <i>İkinci satır</i>

        00:01:04.250 --> 00:01:06.000
        3 &lt; 5

        """;

    [Fact]
    public void Parses_header_identifiers_settings_voice_and_entities()
    {
        var doc = _vtt.Parse(Sample);

        doc.VttHeader.ShouldBe("- LingoCue örnek");
        doc.VttHeaderBlocks.ShouldHaveSingleItem().ShouldStartWith("STYLE");
        doc.Cues.Count.ShouldBe(2);

        var first = doc.Cues[0];
        first.Format.VttIdentifier.ShouldBe("giris");
        first.Format.VttSettings.ShouldBe("line:85% align:center");
        first.Speaker.ShouldBe("Ayşe");
        first.Text.ShouldBe("Merhaba & hoş geldiniz!\n<i>İkinci satır</i>");

        doc.Cues[1].Start.ShouldBe(TimeSpan.FromMilliseconds(64_250));
        doc.Cues[1].Text.ShouldBe("3 < 5");
    }

    [Fact]
    public void Round_trips_without_losing_cue_data()
    {
        var first = _vtt.Parse(Sample);
        var second = _vtt.Parse(_vtt.Write(first));

        second.VttHeader.ShouldBe(first.VttHeader);
        second.VttHeaderBlocks.ShouldBe(first.VttHeaderBlocks);
        second.Cues.Select(c => (c.Start, c.End, c.Text, c.Speaker, c.Format))
            .ShouldBe(first.Cues.Select(c => (c.Start, c.End, c.Text, c.Speaker, c.Format)));
    }

    [Fact]
    public void Writes_escaped_text_and_keeps_formatting_tags()
    {
        var written = _vtt.Write(_vtt.Parse(Sample));

        written.ShouldContain("<v Ayşe>Merhaba &amp; hoş geldiniz!\n<i>İkinci satır</i>");
        written.ShouldContain("3 &lt; 5");
        written.ShouldStartWith("WEBVTT - LingoCue örnek\n\nSTYLE");
    }

    [Fact]
    public void Accepts_times_without_hours_and_missing_header()
    {
        var doc = _vtt.Parse("00:01.000 --> 00:02.500\nkısa\n");

        doc.Cues.ShouldHaveSingleItem().End.ShouldBe(TimeSpan.FromMilliseconds(2_500));
        doc.Warnings.ShouldContain("Missing WEBVTT header.");
    }
}
