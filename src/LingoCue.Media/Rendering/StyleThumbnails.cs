using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Rendering;

/// <summary>What a style thumbnail shows: one line of text over a video frame (or a still image).</summary>
/// <param name="VideoPath">The project's video; null uses <paramref name="BackgroundImage"/>.</param>
/// <param name="At">The video frame to use.</param>
/// <param name="BackgroundImage">Still image when there is no video (an image file FFmpeg can read).</param>
public sealed record ThumbnailScene(string? VideoPath, TimeSpan At, string Text, int VideoWidth, int VideoHeight, string? BackgroundImage, string? ProjectFontDirectory);

/// <summary>
/// Renders a template onto a frame with FFmpeg's <c>ass</c> filter: the same libass and the same
/// <see cref="AssScriptBuilder"/> output as the preview and the burn-in, so the thumbnail is the real look.
/// Results are cached by template, text and frame.
/// </summary>
public sealed class StyleThumbnails(IMediaToolLocator locator, IProcessRunner runner, string cacheDirectory, Core.Fonts.IFontCatalog? fonts = null)
{
    public const int Width = 480;

    /// <summary>Changes whenever the rendering (crop, size) changes, so older cached pictures are not reused.</summary>
    private const string CacheVersion = "3";

    /// <returns>The PNG path, or null when FFmpeg could not render it (no FFmpeg, no libass, unreadable video).</returns>
    public async Task<string?> RenderAsync(StyleTemplate template, ThumbnailScene scene, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(scene);
        if (!locator.TryLocate(out var tools) || tools is null || (scene.VideoPath is null && scene.BackgroundImage is null))
        {
            return null;
        }

        var script = Script(template, scene, fonts is null ? null : new Core.Fonts.FontTextMeasurer(fonts, scene.ProjectFontDirectory));
        var key = Hash(CacheVersion + "|" + script + "|" + scene.VideoPath + "|" + scene.At.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + scene.BackgroundImage);
        var output = Path.Combine(cacheDirectory, key + ".png");
        if (File.Exists(output))
        {
            return output;
        }

        var work = Path.Combine(cacheDirectory, "work-" + key);
        Directory.CreateDirectory(Path.Combine(work, FfmpegCommands.FontsFolderName));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(work, FfmpegCommands.SubtitleFileName), script, cancellationToken);
            ExportJobs.CopyFonts(scene.ProjectFontDirectory, Path.Combine(work, FfmpegCommands.FontsFolderName));
            var partial = output + ".partial.png";
            var result = await runner.RunAsync(tools.FfmpegPath, Arguments(scene, partial, template.Style.Alignment), workingDirectory: work, cancellationToken: cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(partial))
            {
                return null;
            }

            File.Move(partial, output, overwrite: true);
            return output;
        }
        finally
        {
            TryDelete(work);
        }
    }

    /// <summary>
    /// The template's script for one line visible from the first frame on. Input seeking resets the
    /// frame time to 0, so the line spans 0 to an hour. Bilingual layout and karaoke need other tracks
    /// or word timings and are left out of the thumbnail.
    /// </summary>
    internal static string Script(StyleTemplate template, ThumbnailScene scene, Core.Fonts.ITextMeasurer? measurer = null)
    {
        var project = new Project();
        StyleTemplates.Apply(template, project);
        project.Presentation = project.Presentation with
        {
            Bilingual = project.Presentation.Bilingual with { Enabled = false },
            Karaoke = new KaraokeOptions(),
            FadeInMs = 0,
            FadeOutMs = 0,
        };
        var track = project.EnsureActiveTrack();
        track.AddCue(TimeSpan.Zero, TimeSpan.FromHours(1), scene.Text);
        return AssScriptBuilder.Build(project, track, scene.VideoWidth, scene.VideoHeight, measurer: measurer);
    }

    /// <summary>Share of the frame a thumbnail shows: the subtitle area, enlarged so the text can be read.</summary>
    internal const double Zoom = 0.56;

    internal static IReadOnlyList<string> Arguments(ThumbnailScene scene, string output, int alignment = 2)
    {
        // A 16:9 window over the subtitle: at the bottom, middle or top following the numpad alignment.
        var y = alignment switch { >= 7 => "0", >= 4 => "(ih-oh)/2", _ => "ih-oh" };
        // Centered styles are usually large (Shorts): a wider window keeps their lines whole.
        var zoom = (alignment is >= 4 and <= 6 ? 0.8 : Zoom).ToString(CultureInfo.InvariantCulture);
        var filter = $"ass={FfmpegCommands.SubtitleFileName}:fontsdir={FfmpegCommands.FontsFolderName},crop=iw*{zoom}:ih*{zoom}:(iw-ow)/2:{y},scale={Width}:-2";
        string[] input = scene.VideoPath is not null
            ? ["-ss", scene.At.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", scene.VideoPath]
            : ["-loop", "1", "-i", scene.BackgroundImage!];
        return ["-nostdin", "-hide_banner", "-v", "error", "-y", .. input, "-frames:v", "1", "-vf", filter, output];
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..24];

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
