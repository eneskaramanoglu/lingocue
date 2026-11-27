using System.Text.Json;
using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Rendering;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Tests.Rendering;

/// <summary>Reports synchronously (Progress&lt;T&gt; would post to the thread pool).</summary>
internal sealed class SyncProgress<T>(List<T> values) : IProgress<T>
{
    public void Report(T value)
    {
        lock (values)
        {
            values.Add(value);
        }
    }
}

public sealed class RenderIntegrationTests(TestMedia media) : IClassFixture<TestMedia>, IDisposable
{
    // Turkish characters and spaces in the output folder on purpose.
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-render-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string OutputDir()
    {
        var dir = Path.Combine(_dir, "Çıktı ğüşiö");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private (VideoRenderer Renderer, MediaProbe Probe, ProcessRunner Runner, MediaTools Tools) Create()
    {
        var tools = media.RequireTools();
        var locator = new FixedLocator(tools);
        var runner = new ProcessRunner();
        return (new VideoRenderer(locator, runner), new MediaProbe(locator, runner), runner, tools);
    }

    /// <summary>A work folder with an ASS script that paints a red 200×200 square in the top-left corner.</summary>
    private string WorkFolderWithRedSquare()
    {
        var work = Path.Combine(_dir, "iş");
        Directory.CreateDirectory(Path.Combine(work, FfmpegCommands.FontsFolderName));
        File.WriteAllText(Path.Combine(work, FfmpegCommands.SubtitleFileName), """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 1280
            PlayResY: 720

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,40,&H000000FF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:03.00,Default,,0,0,0,,{\an7\pos(0,0)\p1}m 0 0 l 200 0 200 200 0 200{\p0}
            """);
        return work;
    }

    private async Task<byte[]> PixelAsync(ProcessRunner runner, MediaTools tools, string video, int x, int y)
    {
        var raw = Path.Combine(_dir, "pixel.rgb");
        var result = await runner.RunAsync(tools.FfmpegPath,
            ["-v", "error", "-y", "-ss", "1.5", "-i", video, "-frames:v", "1", "-vf", $"crop=2:2:{x}:{y}", "-f", "rawvideo", "-pix_fmt", "rgb24", raw],
            cancellationToken: TestContext.Current.CancellationToken);
        result.ExitCode.ShouldBe(0, result.StandardError);
        return await File.ReadAllBytesAsync(raw, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Burn_in_draws_the_ass_into_the_picture_and_keeps_audio()
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, runner, tools) = Create();
        (await renderer.SupportsBurnInAsync(ct)).ShouldBeTrue("this FFmpeg has no libass; on macOS install ffmpeg-full");
        var source = await probe.ProbeAsync(media.LandscapeClip, ct);
        var output = Path.Combine(OutputDir(), "Gömülü film.mp4");
        var reports = new List<RenderProgress>();

        await renderer.RenderAsync(FfmpegCommands.BurnIn(new BurnInRequest(media.LandscapeClip, output, source, WorkFolderWithRedSquare())),
            source.Duration, new SyncProgress<RenderProgress>(reports), ct);

        var result = await probe.ProbeAsync(output, ct);
        result.Video!.Codec.ShouldBe("h264");
        result.Duration.TotalSeconds.ShouldBe(3, tolerance: 0.15);
        result.AudioStreams.ShouldHaveSingleItem().Codec.ShouldBe("aac");
        reports.ShouldNotBeEmpty();
        reports[^1].Fraction.ShouldBe(1);

        var inside = await PixelAsync(runner, tools, output, 100, 100);
        inside[0].ShouldBeGreaterThan((byte)200); // red
        inside[1].ShouldBeLessThan((byte)60);
        inside[2].ShouldBeLessThan((byte)60);
        Directory.GetFiles(Path.GetDirectoryName(output)!).ShouldBe([output]); // no partial file left
    }

    [Theory]
    [InlineData(OverlayCodec.Animation, "qtrle")]
    [InlineData(OverlayCodec.ProRes4444, "prores")]
    public async Task Overlay_is_transparent_except_the_subtitles_and_matches_the_source(OverlayCodec codec, string expectedCodec)
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, runner, tools) = Create();
        var source = await probe.ProbeAsync(media.LandscapeClip, ct);
        var video = source.Video!;
        var output = Path.Combine(OutputDir(), $"Altyazı katmanı {codec}.mov");

        await renderer.RenderAsync(FfmpegCommands.Overlay(new OverlayRequest(output, video.DisplayWidth, video.DisplayHeight, video.RateForFfmpeg,
            source.Duration, ExactColors(WorkFolderWithRedSquare()), codec)), source.Duration, cancellationToken: ct);

        var result = await probe.ProbeAsync(output, ct);
        result.Video!.Codec.ShouldBe(expectedCodec);
        (result.Video.Width, result.Video.Height).ShouldBe((video.Width, video.Height));
        result.Video.FrameRate.ShouldBe(video.FrameRate, tolerance: 0.01);
        result.Duration.TotalSeconds.ShouldBe(source.Duration.TotalSeconds, tolerance: 0.1);
        result.AudioStreams.ShouldBeEmpty();

        var inside = await RgbaAsync(runner, tools, output, 100, 100);
        (inside[0], inside[3]).ShouldBe(((byte)255, (byte)255)); // opaque red
        inside[1].ShouldBeLessThan((byte)20);
        var outside = await RgbaAsync(runner, tools, output, 600, 400);
        outside[3].ShouldBe((byte)0); // transparent: the editor's picture shows through untouched
    }

    private static string ExactColors(string work)
    {
        var script = Path.Combine(work, FfmpegCommands.SubtitleFileName);
        File.WriteAllText(script, ExportJobs.WithExactColors(File.ReadAllText(script)));
        return work;
    }

    private async Task<byte[]> RgbaAsync(ProcessRunner runner, MediaTools tools, string video, int x, int y)
    {
        var raw = Path.Combine(_dir, "pixel.rgba");
        var result = await runner.RunAsync(tools.FfmpegPath,
            ["-v", "error", "-y", "-ss", "1.5", "-i", video, "-frames:v", "1", "-vf", $"crop=1:1:{x}:{y},format=rgba", "-f", "rawvideo", raw],
            cancellationToken: TestContext.Current.CancellationToken);
        result.ExitCode.ShouldBe(0, result.StandardError);
        return await File.ReadAllBytesAsync(raw, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Failed_render_leaves_no_file_and_reports_ffmpegs_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, _, _) = Create();
        var source = await probe.ProbeAsync(media.LandscapeClip, ct);
        var output = Path.Combine(OutputDir(), "olmayacak.mp4");
        var emptyWork = Path.Combine(_dir, "boş");
        Directory.CreateDirectory(emptyWork); // no subtitles.ass → the ass filter fails

        var ex = await Should.ThrowAsync<RenderException>(() =>
            renderer.RenderAsync(FfmpegCommands.BurnIn(new BurnInRequest(media.LandscapeClip, output, source, emptyWork)), source.Duration, cancellationToken: ct));

        ex.Message.ShouldContain("FFmpeg");
        Directory.GetFiles(Path.GetDirectoryName(output)!).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelled_render_leaves_no_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, _, _) = Create();
        var source = await probe.ProbeAsync(media.LandscapeClip, ct);
        var output = Path.Combine(OutputDir(), "iptal.mp4");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var progress = new CancelOnFirstReport(cts);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            renderer.RenderAsync(FfmpegCommands.BurnIn(new BurnInRequest(media.LandscapeClip, output, source, WorkFolderWithRedSquare(), Quality: 10)),
                source.Duration, progress, cts.Token));

        await Task.Delay(300, ct); // the killed process releases the file
        Directory.GetFiles(Path.GetDirectoryName(output)!).ShouldBeEmpty();
    }

    [Fact]
    public async Task Output_cannot_overwrite_the_source()
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, _, _) = Create();
        var source = await probe.ProbeAsync(media.LandscapeClip, ct);

        await Should.ThrowAsync<RenderException>(() => renderer.RenderAsync(
            FfmpegCommands.BurnIn(new BurnInRequest(media.LandscapeClip, media.LandscapeClip, source, WorkFolderWithRedSquare())), source.Duration, cancellationToken: ct));
    }

    [Theory]
    [InlineData(SoftSubContainer.Mp4, "mov_text", ".srt", ".mp4")]
    [InlineData(SoftSubContainer.Mkv, "ass", ".ass", ".mkv")]
    public async Task Soft_sub_adds_switchable_tracks_with_languages(SoftSubContainer container, string codec, string extension, string outputExtension)
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, runner, tools) = Create();
        var source = await probe.ProbeAsync(media.LandscapeClip, ct);
        var tr = Path.Combine(_dir, "tr" + extension);
        var en = Path.Combine(_dir, "en" + extension);
        await File.WriteAllTextAsync(tr, Subtitle(extension, "Merhaba dünya, ğüşıöç İ"), ct);
        await File.WriteAllTextAsync(en, Subtitle(extension, "Hello world"), ct);
        var output = Path.Combine(OutputDir(), "Altyazılı" + outputExtension);

        await renderer.RenderAsync(FfmpegCommands.SoftSub(new SoftSubRequest(media.LandscapeClip, output, source, container,
            [new SoftSubTrack(tr, "tur", "Türkçe", true), new SoftSubTrack(en, "eng", "English", false)], _dir)), source.Duration, cancellationToken: ct);

        var probeResult = await runner.RunAsync(tools.FfprobePath, ["-v", "error", "-show_streams", "-of", "json", output], cancellationToken: ct);
        using var json = JsonDocument.Parse(probeResult.StandardOutput);
        var subs = json.RootElement.GetProperty("streams").EnumerateArray()
            .Where(s => s.GetProperty("codec_type").GetString() == "subtitle").ToList();
        subs.Count.ShouldBe(2);
        subs.ShouldAllBe(s => s.GetProperty("codec_name").GetString() == codec);
        subs.Select(s => s.GetProperty("tags").GetProperty("language").GetString()).ShouldBe(["tur", "eng"]);
        subs[0].GetProperty("disposition").GetProperty("default").GetInt32().ShouldBe(1);
        subs[1].GetProperty("disposition").GetProperty("default").GetInt32().ShouldBe(0);
        json.RootElement.GetProperty("streams").EnumerateArray().Count(s => s.GetProperty("codec_type").GetString() == "video").ShouldBe(1);
    }

    [Fact]
    public async Task Hardware_probe_never_throws_and_working_encoders_render()
    {
        var ct = TestContext.Current.CancellationToken;
        var (renderer, probe, _, _) = Create();

        var statuses = await renderer.HardwareEncodersAsync(ct);
        (await renderer.HardwareEncodersAsync(ct)).ShouldBeSameAs(statuses); // cached
        statuses.Select(s => s.Encoder).ShouldBe([VideoEncoder.Nvenc, VideoEncoder.Amf, VideoEncoder.Qsv, VideoEncoder.VideoToolbox]);
        var available = statuses.Where(s => s.Available).Select(s => s.Encoder).ToList();

        var source = await probe.ProbeAsync(media.LandscapeClip, ct);
        foreach (var encoder in available)
        {
            var output = Path.Combine(OutputDir(), $"{encoder}.mp4");
            await renderer.RenderAsync(FfmpegCommands.BurnIn(new BurnInRequest(media.LandscapeClip, output, source, WorkFolderWithRedSquare(), encoder)),
                source.Duration, cancellationToken: ct);
            (await probe.ProbeAsync(output, ct)).Video!.Codec.ShouldBe("h264");
        }
    }

    private static string Subtitle(string extension, string text) => extension == ".srt"
        ? $"1\n00:00:00,500 --> 00:00:02,500\n{text}\n"
        : $"""
           [Script Info]
           ScriptType: v4.00+

           [V4+ Styles]
           Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
           Style: Default,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1

           [Events]
           Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
           Dialogue: 0,0:00:00.50,0:00:02.50,Default,,0,0,0,,{text}
           """;

    private sealed class CancelOnFirstReport(CancellationTokenSource cts) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => cts.Cancel();
    }

    private sealed class FixedLocator(MediaTools tools) : IMediaToolLocator
    {
        public MediaTools Locate() => tools;

        public bool TryLocate(out MediaTools? result)
        {
            result = tools;
            return true;
        }
    }
}
