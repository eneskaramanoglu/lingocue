using LingoCue.Media.Probing;
using LingoCue.Media.Rendering;

namespace LingoCue.Media.Tests.Rendering;

public class FfmpegCommandsTests
{
    private static MediaInfo Source(string? audioCodec = "aac")
        => new("in.mp4", "mov,mp4", TimeSpan.FromSeconds(10), 1000, new VideoStreamInfo(0, "h264", 1920, 1080, 30, 0),
            audioCodec is null ? [] : [new AudioStreamInfo(1, audioCodec, 2, 48000, "tur")]);

    private static string Joined(FfmpegCommand c) => string.Join(' ', c.Arguments);

    [Fact]
    public void Burn_in_uses_relative_ass_and_fonts_and_runs_in_the_work_folder()
    {
        var command = FfmpegCommands.BurnIn(new BurnInRequest(@"C:\Videolar\Çekim 1.mp4", @"D:\Çıktı\film.mp4", Source(), @"C:\tmp\job"));

        command.WorkingDirectory.ShouldBe(@"C:\tmp\job");
        command.Arguments.ShouldContain("ass=subtitles.ass:fontsdir=fonts");
        command.Arguments[^1].ShouldBe(@"D:\Çıktı\film.mp4");
        Joined(command).ShouldContain("-c:v libx264 -preset veryfast -crf 19 -pix_fmt yuv420p");
        Joined(command).ShouldContain("-c:a copy");
        Joined(command).ShouldContain("-fps_mode passthrough");
        Joined(command).ShouldContain("-movflags +faststart -f mp4");
        Joined(command).ShouldContain("-map 0:a:0?");
    }

    [Theory]
    [InlineData("pcm_s16le")]
    [InlineData("vorbis")]
    public void Audio_mp4_cannot_hold_is_reencoded_to_aac(string codec)
        => Joined(FfmpegCommands.BurnIn(new BurnInRequest("in.mp4", "out.mp4", Source(codec), "w"))).ShouldContain("-c:a aac -b:a 192k");

    [Fact]
    public void Video_without_audio_still_works()
        => Joined(FfmpegCommands.BurnIn(new BurnInRequest("in.mp4", "out.mp4", Source(null), "w"))).ShouldContain("-c:a copy");

    [Theory]
    [InlineData(VideoEncoder.Nvenc, "-c:v h264_nvenc -preset p5 -tune hq -rc vbr -cq 21 -b:v 0")]
    [InlineData(VideoEncoder.VideoToolbox, "-c:v h264_videotoolbox -q:v 69")]
    [InlineData(VideoEncoder.X264, "-c:v libx264 -preset veryfast -crf 19")]
    public void Encoders_map_the_quality(VideoEncoder encoder, string expected)
        => Joined(FfmpegCommands.BurnIn(new BurnInRequest("in.mp4", "out.mp4", Source(), "w", encoder, 19))).ShouldContain(expected);

    [Fact]
    public void Quality_is_clamped()
    {
        Joined(FfmpegCommands.BurnIn(new BurnInRequest("in.mp4", "out.mp4", Source(), "w", Quality: 2))).ShouldContain("-crf 10");
        FfmpegCommands.VideoToolboxQuality(35).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Soft_sub_mp4_converts_tracks_to_mov_text_with_language_title_and_default()
    {
        var command = FfmpegCommands.SoftSub(new SoftSubRequest("in.mp4", "out.mp4", Source(), SoftSubContainer.Mp4,
            [new SoftSubTrack("tr.srt", "tur", "Türkçe", true), new SoftSubTrack("en.srt", "eng", "English", false)], "w"));
        var joined = Joined(command);

        joined.ShouldContain("-i in.mp4 -i tr.srt -i en.srt");
        joined.ShouldContain("-map 0:v:0 -map 0:a? -map 1:0 -map 2:0");
        joined.ShouldContain("-c:v copy -c:a copy -c:s mov_text");
        joined.ShouldContain("-metadata:s:s:0 language=tur -metadata:s:s:0 title=Türkçe -disposition:s:0 default");
        joined.ShouldContain("-metadata:s:s:1 language=eng -metadata:s:s:1 title=English -disposition:s:1 0");
        joined.ShouldEndWith("-f mp4 out.mp4");
    }

    [Fact]
    public void Soft_sub_mkv_copies_ass_attaches_fonts_and_keeps_any_audio()
    {
        var command = FfmpegCommands.SoftSub(new SoftSubRequest("in.mov", "out.mkv", Source("pcm_s16le"), SoftSubContainer.Mkv,
            [new SoftSubTrack("tr.ass", "tur", "Türkçe", true)], "w", [@"C:\p\fonts\Baslik.otf", @"C:\p\fonts\Govde.ttf"]));
        var joined = Joined(command);

        joined.ShouldContain("-c:a copy -c:s copy");
        joined.ShouldContain(@"-attach C:\p\fonts\Baslik.otf -metadata:s:t:0 mimetype=font/otf");
        joined.ShouldContain(@"-attach C:\p\fonts\Govde.ttf -metadata:s:t:1 mimetype=font/ttf");
        joined.ShouldEndWith("-f matroska out.mkv");
    }

    [Fact]
    public void Soft_sub_needs_a_track()
        => Should.Throw<ArgumentException>(() => FfmpegCommands.SoftSub(new SoftSubRequest("in.mp4", "out.mp4", Source(), SoftSubContainer.Mp4, [], "w")));

    [Fact]
    public void Partial_file_sits_next_to_the_target()
        => VideoRenderer.PartialPath(Path.Combine("dir", "film.mp4")).ShouldBe(Path.Combine("dir", "film.lingocue-partial.mp4"));

    [Fact]
    public void Progress_reports_once_per_block_with_speed_and_time_left()
    {
        var reports = new List<RenderProgress>();
        var parser = new RenderProgressParser(TimeSpan.FromSeconds(10), new SyncProgress<RenderProgress>(reports));

        foreach (var line in new[] { "frame=10", "out_time_us=2000000", "speed=2.0x", "progress=continue", "out_time_us=4000000", "speed= 4x", "progress=continue", "progress=end" })
        {
            parser.OnLine(line);
        }

        reports.Count.ShouldBe(3);
        reports[0].Fraction.ShouldBe(0.2, 1e-9);
        reports[0].Remaining.ShouldBe(TimeSpan.FromSeconds(4));
        reports[1].Speed.ShouldBe(4);
        reports[1].Remaining.ShouldBe(TimeSpan.FromSeconds(1.5));
        reports[2].Fraction.ShouldBe(1);
    }

    [Fact]
    public void Old_nvidia_driver_is_explained()
    {
        // Real output on a machine with an NVIDIA GPU and a driver older than FFmpeg's NVENC SDK.
        const string stderr = """
            [h264_nvenc @ 000001a30f837300] Driver does not support the required nvenc API version. Required: 13.1 Found: 13.0
            [h264_nvenc @ 000001a30f837300] The minimum required Nvidia driver for nvenc is 610.00 or newer
            """;

        var status = HardwareEncoderStatus.FromProbe(VideoEncoder.Nvenc, 1, stderr);

        status.Available.ShouldBeFalse();
        status.Present.ShouldBeTrue();
        status.Problem!.ShouldContain("610.00");
    }

    [Theory]
    [InlineData("Unknown encoder 'h264_videotoolbox'")]
    [InlineData("[h264_nvenc] Cannot load nvcuda.dll")]
    [InlineData("[h264_nvenc] No capable devices found")]
    public void Encoders_this_machine_does_not_have_are_not_offered(string stderr)
    {
        var status = HardwareEncoderStatus.FromProbe(VideoEncoder.Nvenc, 1, stderr);
        status.Present.ShouldBeFalse();
        status.Problem.ShouldBeNull();
    }

    [Fact]
    public void Ass_filter_is_found_in_the_filter_list()
    {
        // Lines as printed by "ffmpeg -hide_banner -filters" (FFmpeg 9).
        const string withLibass = """
             .. aresample         A->A       Resample audio data.
             .. ass               V->V       Render ASS subtitles onto input video using the libass library.
             .. scale             V->V       Scale the input video size and/or convert the image format.
            """;
        const string withoutLibass = """
             .. aresample         A->A       Resample audio data.
             .. asetpts           A->A       Set PTS for the output audio frame.
             .. scale             V->V       Scale the input video size and/or convert the image format.
            """;

        VideoRenderer.HasAssFilter(withLibass).ShouldBeTrue();
        VideoRenderer.HasAssFilter(withoutLibass).ShouldBeFalse();
    }

    [Fact]
    public void Working_encoder_is_available()
        => HardwareEncoderStatus.FromProbe(VideoEncoder.Nvenc, 0, string.Empty).ShouldBe(new HardwareEncoderStatus(VideoEncoder.Nvenc, true, true, null));

    [Fact]
    public void High_bit_depth_and_hdr_are_detected()
    {
        var hdr = new VideoStreamInfo(0, "hevc", 3840, 2160, 30, 0, "yuv420p10le", "arib-std-b67");
        hdr.IsHighBitDepth.ShouldBeTrue();
        hdr.IsHdr.ShouldBeTrue();
        new VideoStreamInfo(0, "h264", 1920, 1080, 30, 0, "yuv420p", "bt709").IsHdr.ShouldBeFalse();
    }
}
