using LingoCue.Core.Projects;
using LingoCue.Media.Processes;
using LingoCue.Media.Rendering;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Tests.Rendering;

public sealed class StyleThumbnailsTests(TestMedia media) : IClassFixture<TestMedia>, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-thumbs-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Locator(MediaTools tools) : IMediaToolLocator
    {
        public MediaTools Locate() => tools;

        public bool TryLocate(out MediaTools? result)
        {
            result = tools;
            return true;
        }
    }

    private static async Task<(int Width, int Height)> SizeAsync(ProcessRunner runner, MediaTools tools, string png)
    {
        var r = await runner.RunAsync(tools.FfprobePath, ["-v", "error", "-select_streams", "v", "-show_entries", "stream=width,height", "-of", "csv=p=0", png],
            cancellationToken: TestContext.Current.CancellationToken);
        var parts = r.StandardOutput.Trim().Split(',');
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Renders_each_template_on_a_video_frame_and_caches_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var tools = media.RequireTools();
        var runner = new ProcessRunner();
        var thumbs = new StyleThumbnails(new Locator(tools), runner, _dir);
        var scene = new ThumbnailScene(media.LandscapeClip, TimeSpan.FromSeconds(1), "Merhaba, bugün İstanbul'dayız.", 1280, 720, null, null);

        foreach (var template in StyleTemplates.BuiltIn)
        {
            var png = await thumbs.RenderAsync(template, scene, ct);

            png.ShouldNotBeNull(template.Name);
            (await SizeAsync(runner, tools, png)).ShouldBe((StyleThumbnails.Width, 270));
        }

        var first = await thumbs.RenderAsync(StyleTemplates.BuiltIn[0], scene, ct);
        var again = await thumbs.RenderAsync(StyleTemplates.BuiltIn[0], scene, ct);
        again.ShouldBe(first); // cached
        Directory.GetDirectories(_dir).ShouldBeEmpty(); // work folders cleaned up
    }

    [Fact]
    public async Task Uses_a_still_image_without_a_video()
    {
        var ct = TestContext.Current.CancellationToken;
        var tools = media.RequireTools();
        var runner = new ProcessRunner();
        var background = Path.Combine(_dir, "bg.png");
        (await runner.RunAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i", "color=c=0x223355:s=1280x720", "-frames:v", "1", background], cancellationToken: ct)).ExitCode.ShouldBe(0);

        var png = await new StyleThumbnails(new Locator(tools), runner, _dir)
            .RenderAsync(StyleTemplates.BuiltIn[^1], new ThumbnailScene(null, TimeSpan.Zero, "Örnek satır", 1280, 720, background, null), ct);

        png.ShouldNotBeNull();
    }

    [Fact]
    public void The_line_is_visible_from_the_first_frame_without_bilingual_or_karaoke()
    {
        var script = StyleThumbnails.Script(StyleTemplates.BuiltIn[0], new ThumbnailScene(null, TimeSpan.Zero, "Satır", 1920, 1080, "bg.png", null));

        script.ShouldContain("Dialogue: ");
        script.ShouldContain("0:00:00.00,1:00:00.00");
        script.ShouldNotContain(@"\k");
    }
}
