using LingoCue.Media.Probing;

namespace LingoCue.Media.Tests.Probing;

public class FfprobeJsonParserTests
{
    // Trimmed from real ffprobe 9 output for a phone-style clip (display matrix rotation 90).
    private const string RotatedClip = """
        {
          "streams": [
            { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 320, "height": 240,
              "r_frame_rate": "30000/1001", "avg_frame_rate": "30000/1001", "duration": "0.967633",
              "disposition": { "attached_pic": 0 }, "tags": { "language": "und" },
              "side_data_list": [ { "side_data_type": "Display Matrix", "rotation": 90 } ] },
            { "index": 1, "codec_name": "aac", "codec_type": "audio", "sample_rate": "44100", "channels": 1,
              "r_frame_rate": "0/0", "avg_frame_rate": "0/0", "duration": "1.000000",
              "disposition": { "attached_pic": 0 }, "tags": { "language": "tur" } }
          ],
          "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "1.000000", "size": "18396" }
        }
        """;

    [Fact]
    public void Parses_format_video_and_audio()
    {
        var info = FfprobeJsonParser.Parse("clip.mp4", RotatedClip);

        info.FormatName.ShouldBe("mov,mp4,m4a,3gp,3g2,mj2");
        info.Duration.ShouldBe(TimeSpan.FromSeconds(1));
        info.SizeBytes.ShouldBe(18396);
        info.Video.ShouldNotBeNull();
        info.Video.Codec.ShouldBe("h264");
        info.Video.FrameRate.ShouldBe(29.97, tolerance: 0.001);
        info.AudioStreams.ShouldHaveSingleItem().Language.ShouldBe("tur");
        info.AudioStreams[0].SampleRate.ShouldBe(44100);
    }

    [Fact]
    public void Applies_rotation_to_display_size()
    {
        var video = FfprobeJsonParser.Parse("clip.mp4", RotatedClip).Video!;

        video.Rotation.ShouldBe(90);
        video.DisplayWidth.ShouldBe(240);
        video.DisplayHeight.ShouldBe(320);
        video.IsPortrait.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(180, 180)]
    [InlineData(-180, 180)]
    [InlineData(270, 270)]
    public void Normalizes_negative_and_large_rotations(int raw, int expected)
    {
        var json = $$"""
            { "streams": [ { "index": 0, "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080,
              "avg_frame_rate": "25/1", "side_data_list": [ { "rotation": {{raw}} } ] } ],
              "format": { "format_name": "mp4", "duration": "2.5" } }
            """;

        FfprobeJsonParser.Parse("x.mp4", json).Video!.Rotation.ShouldBe(expected);
    }

    [Fact]
    public void Skips_cover_art_and_falls_back_to_r_frame_rate()
    {
        const string json = """
            { "streams": [
                { "index": 0, "codec_type": "video", "codec_name": "mjpeg", "width": 600, "height": 600,
                  "disposition": { "attached_pic": 1 } },
                { "index": 1, "codec_type": "video", "codec_name": "hevc", "width": 1920, "height": 1080,
                  "avg_frame_rate": "0/0", "r_frame_rate": "50/1" } ],
              "format": { "format_name": "matroska,webm", "duration": "10" } }
            """;

        var video = FfprobeJsonParser.Parse("x.mkv", json).Video!;

        video.Index.ShouldBe(1);
        video.FrameRate.ShouldBe(50);
        video.FrameDuration.ShouldBe(TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public void Missing_fields_do_not_throw()
    {
        var info = FfprobeJsonParser.Parse("x", "{}");

        info.Duration.ShouldBe(TimeSpan.Zero);
        info.Video.ShouldBeNull();
        info.HasAudio.ShouldBeFalse();
    }
}
