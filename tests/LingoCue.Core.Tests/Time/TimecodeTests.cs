using LingoCue.Core.Time;

namespace LingoCue.Core.Tests.Time;

public class TimecodeTests
{
    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(1_234, "00:00:01.234")]
    [InlineData(83_456, "00:01:23.456")]
    [InlineData(36_000_000, "10:00:00.000")]
    [InlineData(-5, "00:00:00.000")]
    public void Formats_hours_minutes_seconds_milliseconds(long ms, string expected)
        => Timecode.Format(TimeSpan.FromMilliseconds(ms)).ShouldBe(expected);

    [Fact]
    public void Truncates_instead_of_rounding_up()
        => Timecode.Format(TimeSpan.FromTicks(TimeSpan.TicksPerSecond - 1)).ShouldBe("00:00:00.999");

    [Fact]
    public void Supports_more_than_99_hours()
        => Timecode.Format(TimeSpan.FromHours(100)).ShouldBe("100:00:00.000");
}
