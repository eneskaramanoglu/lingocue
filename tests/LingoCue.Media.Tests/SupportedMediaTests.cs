namespace LingoCue.Media.Tests;

public class SupportedMediaTests
{
    [Theory]
    [InlineData("a.mp4", true)]
    [InlineData("A.MP4", true)]
    [InlineData("b.mov", true)]
    [InlineData("c.mkv", true)]
    [InlineData("d.avi", false)]
    [InlineData("e.srt", false)]
    [InlineData("noext", false)]
    public void Recognizes_supported_video_case_insensitively(string path, bool expected)
        => SupportedMedia.IsSupportedVideo(path).ShouldBe(expected);
}
