using LingoCue.Core.Formats;

namespace LingoCue.Core.Tests.Formats;

public class SubtitleTimeTests
{
    [Theory]
    [InlineData("00:01:02,345", 62_345)]
    [InlineData("00:01:02.345", 62_345)]
    [InlineData("1:2:3,4", 3_723_400)]
    [InlineData("01:02:03:456", 3_723_456)] // ':' before ms seen in broken files
    [InlineData("02:03.456", 123_456)]      // VTT without hours
    [InlineData("100:00:00,000", 360_000_000)]
    [InlineData(" 00:00:01,000 ", 1_000)]
    public void Parses_srt_and_vtt_times_leniently(string text, long expectedMs)
    {
        SubtitleTime.TryParseSrtOrVtt(text, out var value).ShouldBeTrue();
        value.ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("00:61:00,000")]
    [InlineData("00:00:60,000")]
    public void Rejects_invalid_times(string text)
        => SubtitleTime.TryParseSrtOrVtt(text, out _).ShouldBeFalse();

    [Fact]
    public void Formats_srt_and_vtt()
    {
        var t = TimeSpan.FromMilliseconds(3_723_456);

        SubtitleTime.FormatSrt(t).ShouldBe("01:02:03,456");
        SubtitleTime.FormatVtt(t).ShouldBe("01:02:03.456");
        SubtitleTime.FormatSrt(TimeSpan.FromSeconds(-1)).ShouldBe("00:00:00,000");
    }

    [Theory]
    [InlineData(3_723_454, "1:02:03.45")]
    [InlineData(3_723_455, "1:02:03.46")] // half rounds away from zero
    [InlineData(59_999, "0:01:00.00")]    // carries into minutes
    public void Formats_ass_with_centisecond_rounding(long ms, string expected)
        => SubtitleTime.FormatAss(TimeSpan.FromMilliseconds(ms)).ShouldBe(expected);

    [Theory]
    [InlineData("0:00:01.50", 1_500)]
    [InlineData("1:02:03.45", 3_723_450)]
    [InlineData("0:00:01.5", 1_500)]
    public void Parses_ass_times(string text, long expectedMs)
    {
        SubtitleTime.TryParseAss(text, out var value).ShouldBeTrue();
        value.ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }
}
