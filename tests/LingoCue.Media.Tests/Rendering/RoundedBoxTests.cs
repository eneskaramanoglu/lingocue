using LingoCue.Core.Fonts;
using LingoCue.Core.Formats;
using LingoCue.Core.Platform;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Media.Processes;
using LingoCue.Media.Rendering;

namespace LingoCue.Media.Tests.Rendering;

/// <summary>
/// The rounded block box is drawn from text measured in the font file. Rendered with libass, its edges
/// must land where libass's own (square) BorderStyle 4 box is, so a rounded box looks the same size.
/// </summary>
public sealed class RoundedBoxTests(TestMedia media) : IClassFixture<TestMedia>, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-rounded-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly string[] s_arialBold =
    [
        @"C:\Windows\Fonts\arialbd.ttf",
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        "/Library/Fonts/Arial Bold.ttf",
    ];

    /// <summary>Box of the magenta pixels in a 1920×1080 frame: (left, top, right, bottom).</summary>
    private async Task<(int L, int T, int R, int B)> BoxAsync(string ass, string fonts, string name)
    {
        var tools = media.RequireTools();
        var work = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(work, "fonts"));
        foreach (var font in Directory.GetFiles(fonts))
        {
            File.Copy(font, Path.Combine(work, "fonts", Path.GetFileName(font)));
        }

        await File.WriteAllTextAsync(Path.Combine(work, "s.ass"), ass, TestContext.Current.CancellationToken);
        var raw = Path.Combine(work, "frame.rgb");
        var result = await new ProcessRunner().RunAsync(tools.FfmpegPath,
            ["-v", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=1920x1080:d=1", "-vf", "ass=s.ass:fontsdir=fonts", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", raw],
            workingDirectory: work, cancellationToken: TestContext.Current.CancellationToken);
        result.ExitCode.ShouldBe(0, result.StandardError);
        var px = await File.ReadAllBytesAsync(raw, TestContext.Current.CancellationToken);
        int l = 1920, t = 1080, r = -1, b = -1;
        for (var y = 0; y < 1080; y++)
        {
            for (var x = 0; x < 1920; x++)
            {
                var i = ((y * 1920) + x) * 3;
                if (px[i] > 200 && px[i + 1] < 60 && px[i + 2] > 200)
                {
                    (l, t, r, b) = (Math.Min(l, x), Math.Min(t, y), Math.Max(r, x), Math.Max(b, y));
                }
            }
        }

        return (l, t, r, b);
    }

    [Theory]
    [InlineData("Merhaba, bugün İstanbul'dayız.")]
    [InlineData("Boğaz'ın kıyısında ılık bir rüzgâr esiyor\nve martılar uçuşuyor.")]
    [InlineData("WWW mmm iii")]
    public async Task A_measured_box_matches_libass_box(string text)
    {
        var font = s_arialBold.FirstOrDefault(File.Exists);
        if (font is null)
        {
            Assert.Skip("Arial Bold is not installed here.");
        }

        var fonts = Path.Combine(_dir, "fonts");
        Directory.CreateDirectory(fonts);
        File.Copy(font, Path.Combine(fonts, Path.GetFileName(font)), overwrite: true);
        var measurer = new FontTextMeasurer(new FontCatalog(new FakePlatform(OsPlatform.Windows)), fonts);

        Project ProjectWith(double radius)
        {
            var project = new Project
            {
                Style = new SubtitleStyle
                {
                    FontName = "Arial", FontSize = 56, Bold = true, Outline = 2, Shadow = 0, MarginV = 60,
                    Background = new SubtitleBackground { Kind = BackgroundKind.BlockBox, Color = new AssColor(255, 0, 255), PaddingX = 20, PaddingY = 10, CornerRadius = radius },
                },
            };
            project.EnsureActiveTrack().AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(5), text);
            return project;
        }

        var square = ProjectWith(0);
        var libass = await BoxAsync(AssScriptBuilder.Build(square, square.Tracks[0], 1920, 1080), fonts, "libass");
        var rounded = ProjectWith(0.01); // a measured box with corners too small to see
        var script = AssScriptBuilder.Build(rounded, rounded.Tracks[0], 1920, 1080, measurer: measurer);
        script.ShouldContain(@"\p1}m "); // drawn by us, not the libass box
        var ours = await BoxAsync(script, fonts, "measured");

        libass.R.ShouldBeGreaterThan(0);
        // Kerning is not applied and libass measures ink rather than advances: allow a few pixels.
        Math.Abs(ours.L - libass.L).ShouldBeLessThanOrEqualTo(6, $"left {ours} vs {libass}");
        Math.Abs(ours.R - libass.R).ShouldBeLessThanOrEqualTo(6, $"right {ours} vs {libass}");
        Math.Abs(ours.T - libass.T).ShouldBeLessThanOrEqualTo(6, $"top {ours} vs {libass}");
        Math.Abs(ours.B - libass.B).ShouldBeLessThanOrEqualTo(6, $"bottom {ours} vs {libass}");
    }
}
