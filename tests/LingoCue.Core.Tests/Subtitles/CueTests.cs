using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Subtitles;

public class CueTests
{
    private static Cue At(double start, double end, string text = "x", int id = 1)
        => new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    [Fact]
    public void Cps_counts_visible_characters_only()
    {
        // "Merhaba dünya" (13) + "nasılsın?" (9) = 22 chars over 2 s; tags and the line break don't count.
        var cue = At(1, 3, "{\\i1}Merhaba dünya{\\i0}\n<b>nasılsın?</b>");

        cue.VisibleLength.ShouldBe(22);
        cue.CharactersPerSecond.ShouldBe(11);
    }

    [Fact]
    public void Zero_length_cue_has_zero_cps()
        => At(2, 2, "abc").CharactersPerSecond.ShouldBe(0);

    [Theory]
    [InlineData(0, 2, 1, 3, true)]
    [InlineData(0, 2, 2, 3, false)] // touching is not overlapping
    [InlineData(1, 3, 0, 1, false)]
    [InlineData(0, 5, 1, 2, true)]
    public void Overlap_uses_half_open_intervals(double s1, double e1, double s2, double e2, bool expected)
        => At(s1, e1).Overlaps(At(s2, e2)).ShouldBe(expected);

    [Fact]
    public void Shift_clamps_at_zero()
    {
        var cue = At(1, 3);

        cue.Shift(TimeSpan.FromSeconds(-2));

        cue.Start.ShouldBe(TimeSpan.Zero);
        cue.End.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Clone_copies_everything_and_can_assign_new_id()
    {
        var cue = At(1, 2, "a");
        cue.Speaker = "S1";
        cue.Marks = CueMarks.NeedsReview;
        cue.Words.Add(new Word("a", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

        var copy = cue.Clone(newId: 7);

        copy.Id.ShouldBe(7);
        copy.Speaker.ShouldBe("S1");
        copy.Marks.ShouldBe(CueMarks.NeedsReview);
        copy.Words.ShouldHaveSingleItem();
        copy.Words.ShouldNotBeSameAs(cue.Words);
    }
}

public class SubtitleTrackTests
{
    [Fact]
    public void CueAt_prefers_latest_start_when_overlapping()
    {
        var track = new SubtitleTrack();
        track.AddCue(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5), "a");
        var b = track.AddCue(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "b");

        track.CueAt(TimeSpan.FromSeconds(3.5)).ShouldBe(b);
        track.CueAt(TimeSpan.FromSeconds(5)).ShouldBeNull();
    }

    [Fact]
    public void Ids_increase_and_renumber_follows_order()
    {
        var track = new SubtitleTrack();
        var first = track.AddCue(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "later");
        var second = track.AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "earlier");
        second.Id.ShouldBe(2);

        track.Sort();
        track.Renumber();

        track.Cues[0].ShouldBe(second);
        second.Id.ShouldBe(1);
        first.Id.ShouldBe(2);
    }

    [Fact]
    public void Finds_overlapping_neighbours()
    {
        var track = new SubtitleTrack();
        track.AddCue(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "a");
        track.AddCue(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3), "b");
        track.AddCue(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "c");

        track.FindOverlaps().ShouldHaveSingleItem().Second.Text.ShouldBe("b");
    }
}

public class SubtitleTextTests
{
    [Theory]
    [InlineData("a\r\nb\rc  \n", "a\nb\nc")]
    [InlineData("\n\nx\n\n", "x")]
    public void Normalizes_line_endings(string input, string expected)
        => SubtitleText.NormalizeLines(input).ShouldBe(expected);

    [Theory]
    [InlineData("{\\an8\\c&H00FFFF&}Üst", "Üst")]
    [InlineData("<i>eğik</i> <font color=\"#fff\">renk</font>", "eğik renk")]
    [InlineData("<v Ayşe>Merhaba", "Merhaba")]
    [InlineData("3 < 5 ve 6 > 2", "3 < 5 ve 6 > 2")]
    [InlineData("{not a tag}", "{not a tag}")]
    public void Strips_ass_and_html_tags_but_keeps_plain_text(string input, string expected)
        => SubtitleText.StripTags(input).ShouldBe(expected);

    [Fact]
    public void Longest_line_ignores_tags()
        => SubtitleText.LongestLineLength("<i>kısa</i>\nbiraz daha uzun").ShouldBe(15);
}

public class AssColorTests
{
    [Fact]
    public void Round_trips_style_format_with_inverted_alpha()
    {
        var color = AssColor.ParseAss("&H80112233");

        color.ShouldBe(new AssColor(0x33, 0x22, 0x11, 0x7F));
        color.ToAssStyle().ShouldBe("&H80112233");
    }

    [Theory]
    [InlineData("&H00FFFF&", 255, 255, 0)]
    [InlineData("&H0000FFFF", 255, 255, 0)]
    [InlineData("65535", 255, 255, 0)]
    public void Parses_override_hex_and_legacy_decimal(string input, byte r, byte g, byte b)
    {
        var color = AssColor.ParseAss(input);

        (color.R, color.G, color.B, color.A).ShouldBe((r, g, b, (byte)255));
    }

    [Fact]
    public void Formats_override_and_hex()
    {
        var color = new AssColor(0x12, 0x34, 0x56);

        color.ToAssOverride().ShouldBe("&H563412&");
        color.ToHex().ShouldBe("#123456");
    }
}
