using System.Text.Json;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Rendering;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Tests.Rendering;

public sealed class ExportJobsTests(TestMedia media) : IClassFixture<TestMedia>, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-export-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string JobsRoot => Path.Combine(_dir, "jobs");

    private (VideoRenderer Renderer, MediaProbe Probe, ProcessRunner Runner, MediaTools Tools) Create()
    {
        var tools = media.RequireTools();
        var locator = new Locator(tools);
        var runner = new ProcessRunner();
        return (new VideoRenderer(locator, runner), new MediaProbe(locator, runner), runner, tools);
    }

    private static Project TwoTrackProject(out SubtitleTrack turkish, out SubtitleTrack english)
    {
        var project = new Project
        {
            // Red opaque box in the top-left corner: easy to find in the rendered frame.
            Style = new SubtitleStyle
            {
                FontSize = 60,
                Alignment = 7,
                MarginL = 20,
                MarginV = 20,
                BorderStyle = AssBorderStyle.OpaqueBox,
                Outline = 30,
                Shadow = 0,
                OutlineColor = new AssColor(255, 0, 0),
            },
        };
        turkish = new SubtitleTrack { Name = "Kaynak", Language = "tr" };
        turkish.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(3), "ŞİĞ");
        english = new SubtitleTrack { Name = "Çeviri · en", Language = "en", Kind = TrackKind.Translation };
        english.AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Hello");
        project.Tracks.AddRange([turkish, english]);
        project.ActiveTrackId = turkish.Id;
        return project;
    }

    [Fact]
    public async Task Burn_in_job_renders_the_project_style_and_cleans_its_work_folder()
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, runner, tools) = Create();
        var source = await probe.ProbeAsync(media.LandscapeClip, ct);
        var project = TwoTrackProject(out var turkish, out _);
        var output = Path.Combine(_dir, "çıktı", "film.mp4");

        var job = await ExportJobs.PrepareBurnInAsync(project, turkish, source, output, VideoEncoder.X264, 19, null, JobsRoot, ct);
        File.ReadAllText(Path.Combine(job.WorkDirectory, FfmpegCommands.SubtitleFileName)).ShouldContain("ŞİĞ");
        await renderer.RenderAsync(job.Command, job.Duration, cancellationToken: ct);
        ExportJobs.Cleanup(job);

        Directory.Exists(job.WorkDirectory).ShouldBeFalse();
        var raw = Path.Combine(_dir, "px.rgb");
        var r = await runner.RunAsync(tools.FfmpegPath, ["-v", "error", "-y", "-ss", "1", "-i", output, "-frames:v", "1", "-vf", "crop=2:2:6:6", "-f", "rawvideo", "-pix_fmt", "rgb24", raw], cancellationToken: ct);
        r.ExitCode.ShouldBe(0, r.StandardError);
        var pixel = await File.ReadAllBytesAsync(raw, ct);
        pixel[0].ShouldBeGreaterThan((byte)200);
        pixel[1].ShouldBeLessThan((byte)60);
    }

    [Fact]
    public async Task Soft_sub_mkv_job_writes_one_styled_ass_per_track_without_bilingual_stacking()
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, runner, tools) = Create();
        var source = await probe.ProbeAsync(media.LandscapeClip, ct);
        var project = TwoTrackProject(out var turkish, out var english);
        project.Presentation = new PresentationOptions { Bilingual = new BilingualOptions { Enabled = true, SecondaryTrackId = english.Id } };
        var output = Path.Combine(_dir, "film.mkv");

        var job = await ExportJobs.PrepareSoftSubAsync(project, [turkish, english], source, output, SoftSubContainer.Mkv, null, JobsRoot, ct);
        var firstAss = File.ReadAllText(Path.Combine(job.WorkDirectory, "track1.ass"));
        firstAss.ShouldContain("ŞİĞ");
        firstAss.ShouldNotContain("Style: Secondary");
        await renderer.RenderAsync(job.Command, job.Duration, cancellationToken: ct);

        var probeResult = await runner.RunAsync(tools.FfprobePath, ["-v", "error", "-show_streams", "-of", "json", output], cancellationToken: ct);
        using var json = JsonDocument.Parse(probeResult.StandardOutput);
        var subs = json.RootElement.GetProperty("streams").EnumerateArray().Where(s => s.GetProperty("codec_type").GetString() == "subtitle").ToList();
        subs.Select(s => s.GetProperty("tags").GetProperty("language").GetString()).ShouldBe(["tur", "eng"]);
        subs.Select(s => s.GetProperty("tags").GetProperty("title").GetString()).ShouldBe(["Türkçe", "English (Çeviri · en)"]);
        ExportJobs.Cleanup(job);
    }

    [Fact]
    public void Hdr_sources_get_a_warning()
    {
        var hdr = new MediaInfo("x.mov", "mov", TimeSpan.FromSeconds(1), null, new VideoStreamInfo(0, "hevc", 1920, 1080, 30, 0, "yuv420p10le", "arib-std-b67"), []);
        ExportJobs.Warnings(hdr).ShouldHaveSingleItem().ShouldContain("HDR");
    }

    [Fact]
    public async Task Project_fonts_are_copied_into_the_job_and_attached_to_mkv()
    {
        var ct = TestContext.Current.CancellationToken;
        var fonts = Path.Combine(_dir, "proje", "fonts");
        Directory.CreateDirectory(fonts);
        await File.WriteAllBytesAsync(Path.Combine(fonts, "Başlık.ttf"), [0, 1, 2], ct);
        await File.WriteAllTextAsync(Path.Combine(fonts, "notlar.txt"), "x", ct);
        var source = new MediaInfo("in.mp4", "mov", TimeSpan.FromSeconds(1), null, new VideoStreamInfo(0, "h264", 1280, 720, 25, 0), []);
        var project = TwoTrackProject(out var turkish, out _);

        var burn = await ExportJobs.PrepareBurnInAsync(project, turkish, source, Path.Combine(_dir, "o.mp4"), VideoEncoder.X264, 19, fonts, JobsRoot, ct);
        var mkv = await ExportJobs.PrepareSoftSubAsync(project, [turkish], source, Path.Combine(_dir, "o.mkv"), SoftSubContainer.Mkv, fonts, JobsRoot, ct);

        Directory.GetFiles(Path.Combine(burn.WorkDirectory, FfmpegCommands.FontsFolderName)).Select(Path.GetFileName).ShouldBe(["Başlık.ttf"]);
        mkv.Command.Arguments.ShouldContain(Path.Combine(fonts, "Başlık.ttf"));
        mkv.Command.Arguments.ShouldNotContain(Path.Combine(fonts, "notlar.txt"));
    }

    private sealed class Locator(MediaTools tools) : IMediaToolLocator
    {
        public MediaTools Locate() => tools;

        public bool TryLocate(out MediaTools? result)
        {
            result = tools;
            return true;
        }
    }
}
