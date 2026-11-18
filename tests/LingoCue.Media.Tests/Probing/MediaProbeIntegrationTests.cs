using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Tests.Probing;

public class MediaProbeIntegrationTests(TestMedia media) : IClassFixture<TestMedia>
{
    private MediaProbe CreateProbe()
    {
        var tools = media.RequireTools();
        return new MediaProbe(new FixedLocator(tools), new ProcessRunner());
    }

    [Fact]
    public async Task Probes_generated_landscape_clip()
    {
        var info = await CreateProbe().ProbeAsync(media.LandscapeClip, TestContext.Current.CancellationToken);

        info.Duration.TotalSeconds.ShouldBe(3, tolerance: 0.1);
        info.Video.ShouldNotBeNull();
        info.Video.Codec.ShouldBe("h264");
        info.Video.DisplayWidth.ShouldBe(1280);
        info.Video.DisplayHeight.ShouldBe(720);
        info.Video.FrameRate.ShouldBe(25);
        var audio = info.AudioStreams.ShouldHaveSingleItem();
        audio.Codec.ShouldBe("aac");
        audio.Language.ShouldBe("tur");
    }

    [Fact]
    public async Task Detects_portrait_rotation()
    {
        var info = await CreateProbe().ProbeAsync(media.PortraitClip, TestContext.Current.CancellationToken);

        info.Video!.IsPortrait.ShouldBeTrue();
        info.Video.DisplayWidth.ShouldBe(720);
    }

    [Fact]
    public async Task Rejects_non_media_file()
        => await Should.ThrowAsync<MediaProbeException>(
            () => CreateProbe().ProbeAsync(media.NotMedia, TestContext.Current.CancellationToken));

    [Fact]
    public async Task Missing_file_throws_file_not_found()
        => await Should.ThrowAsync<FileNotFoundException>(
            () => CreateProbe().ProbeAsync(Path.Combine(Path.GetTempPath(), "does-not-exist.mp4"), TestContext.Current.CancellationToken));

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
