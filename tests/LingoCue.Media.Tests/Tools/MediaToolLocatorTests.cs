using LingoCue.Core.Platform;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Tests.Tools;

public class MediaToolLocatorTests
{
    private static readonly string s_brew = Path.Combine("opt", "homebrew", "bin");
    private static readonly string s_pathDir = Path.Combine("usr", "bin");

    private static MediaToolLocator Create(
        IPlatformInfo platform, ISet<string> existing, string? path = null, MediaToolOptions? options = null)
        => new(platform, options ?? new MediaToolOptions(), null, existing.Contains, () => path);

    [Fact]
    public void Finds_tools_in_platform_directories_before_path()
    {
        var platform = new FakePlatform(OsPlatform.MacOS, s_brew);
        var existing = new HashSet<string>
        {
            Path.Combine(s_brew, "ffmpeg"), Path.Combine(s_brew, "ffprobe"),
            Path.Combine(s_pathDir, "ffmpeg"), Path.Combine(s_pathDir, "ffprobe"),
        };

        var tools = Create(platform, existing, s_pathDir).Locate();

        tools.FfmpegPath.ShouldBe(Path.Combine(s_brew, "ffmpeg"));
        tools.FfprobePath.ShouldBe(Path.Combine(s_brew, "ffprobe"));
    }

    [Fact]
    public void Falls_back_to_path_and_uses_exe_suffix_on_windows()
    {
        var platform = new FakePlatform(OsPlatform.Windows, "nowhere");
        var existing = new HashSet<string> { Path.Combine(s_pathDir, "ffmpeg.exe"), Path.Combine(s_pathDir, "ffprobe.exe") };

        var tools = Create(platform, existing, $"other{Path.PathSeparator}{s_pathDir}").Locate();

        tools.FfmpegPath.ShouldBe(Path.Combine(s_pathDir, "ffmpeg.exe"));
    }

    [Fact]
    public void Prefers_ffprobe_next_to_the_chosen_ffmpeg()
    {
        var configured = Path.Combine("custom", "ffmpeg");
        var platform = new FakePlatform(OsPlatform.Linux, s_brew);
        var existing = new HashSet<string> { configured, Path.Combine("custom", "ffprobe"), Path.Combine(s_brew, "ffprobe") };

        var tools = Create(platform, existing, options: new MediaToolOptions { FfmpegPath = configured }).Locate();

        tools.FfprobePath.ShouldBe(Path.Combine("custom", "ffprobe"));
    }

    [Fact]
    public void Throws_with_searched_locations_when_missing()
    {
        var locator = Create(new FakePlatform(OsPlatform.MacOS, s_brew), new HashSet<string>(), s_pathDir);

        var ex = Should.Throw<MediaToolNotFoundException>(locator.Locate);

        ex.ToolName.ShouldBe("ffmpeg");
        ex.SearchedLocations.ShouldBe([Path.Combine(s_brew, "ffmpeg"), Path.Combine(s_pathDir, "ffmpeg")]);
        locator.TryLocate(out var tools).ShouldBeFalse();
        tools.ShouldBeNull();
    }
}
