using LingoCue.Core.Platform;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Tests;

/// <summary>
/// Generates sample media with FFmpeg at test time (no media files in the repo).
/// Tests that need FFmpeg call <see cref="RequireTools"/>, which skips the test when it is missing.
/// </summary>
public sealed class TestMedia : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lingocue-tests", Guid.NewGuid().ToString("N"));

    public MediaTools? Tools { get; private set; }

    public string LandscapeClip => Path.Combine(_directory, "landscape.mp4");

    public string PortraitClip => Path.Combine(_directory, "portrait.mov");

    public string NotMedia => Path.Combine(_directory, "not-media.mp4");

    public MediaTools RequireTools()
    {
        if (Tools is null)
        {
            // CI sets this so a broken FFmpeg install fails the build instead of silently skipping.
            if (Environment.GetEnvironmentVariable("LINGOCUE_REQUIRE_FFMPEG") == "1")
            {
                Assert.Fail("FFmpeg/ffprobe not found but LINGOCUE_REQUIRE_FFMPEG=1.");
            }

            Assert.Skip("FFmpeg/ffprobe not found; install it to run media integration tests.");
        }

        return Tools;
    }

    public async ValueTask InitializeAsync()
    {
        var locator = new MediaToolLocator(CreatePlatform(), new MediaToolOptions(), null, File.Exists,
            () => Environment.GetEnvironmentVariable("PATH"));
        if (!locator.TryLocate(out var tools) || tools is null)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        var runner = new ProcessRunner();

        // 3 s 1280x720 @ 25 fps with a 440 Hz Turkish-tagged tone.
        await Run(runner, tools.FfmpegPath,
            "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=25:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-shortest", "-metadata:s:a:0", "language=tur", LandscapeClip);

        // Same clip re-muxed with a 90 degree display rotation, like a phone recording.
        await Run(runner, tools.FfmpegPath, "-display_rotation", "90", "-i", LandscapeClip, "-c", "copy", PortraitClip);

        await File.WriteAllTextAsync(NotMedia, "this is not a video");
        Tools = tools;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of temp files.
        }

        return ValueTask.CompletedTask;
    }

    private static async Task Run(ProcessRunner runner, string ffmpeg, params string[] args)
    {
        var result = await runner.RunAsync(ffmpeg, ["-v", "error", "-y", .. args]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed: {result.StandardError}");
        }
    }

    // Mirrors the real search directories without referencing Infrastructure.
    private static FakePlatform CreatePlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new FakePlatform(OsPlatform.Windows, Path.Combine(local, "Microsoft", "WinGet", "Links"), @"C:\ProgramData\chocolatey\bin");
        }

        return new FakePlatform(OperatingSystem.IsMacOS() ? OsPlatform.MacOS : OsPlatform.Linux, "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin");
    }
}
