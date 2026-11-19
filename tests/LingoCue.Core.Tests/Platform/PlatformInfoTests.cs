using LingoCue.Core.Platform;
using LingoCue.Infrastructure.Platform;

namespace LingoCue.Core.Tests.Platform;

public class PlatformInfoTests
{
    private static readonly string s_home = Path.Combine(Path.GetTempPath(), "lingocue-home");

    [Fact]
    public void MacOS_uses_application_support_and_library_logs()
    {
        var info = new PlatformInfo(OsPlatform.MacOS, s_home);

        info.AppDataDirectory.ShouldBe(Path.Combine(s_home, "Library", "Application Support", "LingoCue"));
        info.LogDirectory.ShouldBe(Path.Combine(s_home, "Library", "Logs", "LingoCue"));
    }

    [Fact]
    public void Windows_keeps_logs_under_app_data()
    {
        var info = new PlatformInfo(OsPlatform.Windows, s_home);

        Path.GetFileName(info.AppDataDirectory).ShouldBe("LingoCue");
        info.LogDirectory.ShouldBe(Path.Combine(info.AppDataDirectory, "logs"));
    }

    [Fact]
    public void Font_folders_include_the_users_own_fonts()
    {
        new PlatformInfo(OsPlatform.MacOS, s_home).FontDirectories
            .ShouldBe(["/System/Library/Fonts", "/System/Library/Fonts/Supplemental", "/Library/Fonts", Path.Combine(s_home, "Library", "Fonts")]);
        var windows = new PlatformInfo(OsPlatform.Windows, s_home).FontDirectories;
        windows.Count.ShouldBe(2);
        windows[1].ShouldEndWith(Path.Combine("Microsoft", "Windows", "Fonts"));
    }

    [Fact]
    public void Detects_the_current_os()
    {
        var expected = OperatingSystem.IsWindows() ? OsPlatform.Windows
            : OperatingSystem.IsMacOS() ? OsPlatform.MacOS
            : OperatingSystem.IsLinux() ? OsPlatform.Linux
            : OsPlatform.Unknown;

        new PlatformInfo().Platform.ShouldBe(expected);
    }
}

public class PlatformInfoToolTests
{
    private static readonly string s_home = Path.Combine(Path.GetTempPath(), "lingocue-home");

    [Fact]
    public void Windows_appends_exe_once()
    {
        var info = new PlatformInfo(OsPlatform.Windows, s_home);

        info.GetExecutableFileName("ffmpeg").ShouldBe("ffmpeg.exe");
        info.GetExecutableFileName("ffmpeg.exe").ShouldBe("ffmpeg.exe");
    }

    [Fact]
    public void MacOS_searches_homebrew_because_finder_apps_have_no_shell_path()
    {
        var info = new PlatformInfo(OsPlatform.MacOS, s_home);

        info.GetExecutableFileName("ffmpeg").ShouldBe("ffmpeg");
        info.ToolSearchDirectories.ShouldContain("/opt/homebrew/bin");
        info.ToolSearchDirectories.ShouldContain("/opt/homebrew/opt/ffmpeg-full/bin");
        info.ToolSearchDirectories.ToList().IndexOf("/opt/homebrew/opt/ffmpeg-full/bin")
            .ShouldBeLessThan(info.ToolSearchDirectories.ToList().IndexOf("/opt/homebrew/bin"));
        info.ToolSearchDirectories[0].ShouldBe(AppContext.BaseDirectory);
    }
}
