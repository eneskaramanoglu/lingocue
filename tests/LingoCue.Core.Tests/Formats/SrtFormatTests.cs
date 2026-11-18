using LingoCue.Core.Formats;

namespace LingoCue.Core.Tests.Formats;

public class SrtFormatTests
{
    private readonly SrtFormat _srt = new();

    private const string Sample = """
        1
        00:00:01,000 --> 00:00:03,500
        Merhaba, İstanbul!
        <i>Güzel bir gün.</i>

        2
        00:00:04,000 --> 00:00:06,250
        Şimdi ılık bir çay içelim.

        """;

    [Fact]
    public void Parses_cues_with_turkish_text_and_tags()
    {
        var doc = _srt.Parse(Sample);

        doc.Cues.Count.ShouldBe(2);
        doc.Cues[0].Start.ShouldBe(TimeSpan.FromSeconds(1));
        doc.Cues[0].End.ShouldBe(TimeSpan.FromSeconds(3.5));
        doc.Cues[0].Text.ShouldBe("Merhaba, İstanbul!\n<i>Güzel bir gün.</i>");
        doc.Cues[1].Text.ShouldBe("Şimdi ılık bir çay içelim.");
        doc.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Round_trips_exactly()
    {
        var first = _srt.Parse(Sample);
        var written = _srt.Write(first);
        var second = _srt.Parse(written);

        // Every cue, including the last, is followed by a blank line.
        written.ShouldBe(Sample.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        second.Cues.Select(c => (c.Start, c.End, c.Text)).ShouldBe(first.Cues.Select(c => (c.Start, c.End, c.Text)));
    }

    [Fact]
    public void Tolerates_crlf_bom_missing_indexes_dots_and_extra_blank_lines()
    {
        const string messy = "﻿\r\n\r\n00:00:01.000 --> 00:00:02.000\r\nbir\r\n\r\n\r\n\r\n7\r\n00:00:03,000-->00:00:04,000 X1:10 X2:20 Y1:5 Y2:9\r\niki\r\n";

        var doc = _srt.Parse(messy);

        doc.Cues.Select(c => c.Text).ShouldBe(["bir", "iki"]);
        doc.Cues[1].Start.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Keeps_numeric_text_lines()
    {
        const string text = """
            1
            00:00:01,000 --> 00:00:02,000
            Yıl
            2024

            2
            00:00:03,000 --> 00:00:04,000
            2025
            """;

        var doc = _srt.Parse(text);

        doc.Cues[0].Text.ShouldBe("Yıl\n2024");
        doc.Cues[1].Text.ShouldBe("2025");
    }

    [Fact]
    public void Keeps_blank_line_inside_text_but_writes_it_safely()
    {
        const string text = "1\n00:00:01,000 --> 00:00:02,000\nüst\n\nalt\n\n2\n00:00:03,000 --> 00:00:04,000\nsonraki\n";

        var doc = _srt.Parse(text);

        doc.Cues[0].Text.ShouldBe("üst\n\nalt");
        _srt.Write(doc).ShouldContain("üst\nalt\n\n2\n");
    }

    [Fact]
    public void Swaps_reversed_times_with_a_warning()
    {
        var doc = _srt.Parse("1\n00:00:05,000 --> 00:00:04,000\nters\n");

        doc.Cues[0].Start.ShouldBe(TimeSpan.FromSeconds(4));
        doc.Warnings.ShouldHaveSingleItem();
    }

    [Fact]
    public void Writes_in_time_order_with_fresh_indexes()
    {
        var doc = _srt.Parse("5\n00:00:05,000 --> 00:00:06,000\nB\n\n9\n00:00:01,000 --> 00:00:02,000\nA\n");

        _srt.Write(doc).ShouldStartWith("1\n00:00:01,000 --> 00:00:02,000\nA\n\n2\n");
    }

    [Theory]
    [InlineData("1\n00:00:01,000 --> 00:00:02,000\nx\n", true)]
    [InlineData("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nx\n", false)]
    [InlineData("[Script Info]\n", false)]
    public void Sniffs_content(string content, bool expected)
        => _srt.LooksLike(content).ShouldBe(expected);
}
