using LingoCue.Core.Fonts;
using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;
using LingoCue.Media.Probing;

namespace LingoCue.Media.Rendering;

/// <summary>A prepared export: the command, its private work folder and anything the user should know.</summary>
public sealed record RenderJob(string Title, FfmpegCommand Command, TimeSpan Duration, string WorkDirectory, IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a project into ready-to-run export jobs. Each job gets its own work folder with a snapshot of
/// the subtitles taken now (later edits do not change a queued job) and a copy of the project fonts,
/// so the burn-in renders with exactly what the preview showed.
/// </summary>
public static class ExportJobs
{
    public static async Task<RenderJob> PrepareBurnInAsync(
        Project project, SubtitleTrack track, MediaInfo media, string outputPath,
        VideoEncoder encoder, int quality, string? projectFontDirectory, string jobsRoot, Core.Fonts.ITextMeasurer? measurer = null,
        VideoFormat format = VideoFormat.H264, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(media);
        var work = CreateWorkDirectory(jobsRoot);
        var (width, height) = media.Video is { } v ? (v.DisplayWidth, v.DisplayHeight) : (0, 0);
        await File.WriteAllTextAsync(Path.Combine(work, FfmpegCommands.SubtitleFileName), AssScriptBuilder.Build(project, track, width, height, measurer: measurer), cancellationToken);
        CopyFonts(projectFontDirectory, Path.Combine(work, FfmpegCommands.FontsFolderName));

        var command = FfmpegCommands.BurnIn(new BurnInRequest(media.FilePath, outputPath, media, work, encoder, quality, format));
        return new RenderJob($"Gömülü video · {Path.GetFileName(outputPath)}", command, media.Duration, work, Warnings(media));
    }

    /// <summary>
    /// A transparent subtitle video for an editor's upper track: the source's size, frame rate and length,
    /// rendered with the same script as the preview (the subtitle plan and bilingual lines included).
    /// </summary>
    public static async Task<RenderJob> PrepareOverlayAsync(
        Project project, SubtitleTrack track, MediaInfo media, string outputPath, OverlayCodec codec,
        string? projectFontDirectory, string jobsRoot, Core.Fonts.ITextMeasurer? measurer = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(media);
        if (media.Video is not { } video)
        {
            throw new ArgumentException("A subtitle overlay needs a video.", nameof(media));
        }

        var work = CreateWorkDirectory(jobsRoot);
        await File.WriteAllTextAsync(Path.Combine(work, FfmpegCommands.SubtitleFileName),
            WithExactColors(AssScriptBuilder.Build(project, track, video.DisplayWidth, video.DisplayHeight, measurer: measurer)), cancellationToken);
        CopyFonts(projectFontDirectory, Path.Combine(work, FfmpegCommands.FontsFolderName));

        var command = FfmpegCommands.Overlay(new OverlayRequest(outputPath, video.DisplayWidth, video.DisplayHeight, video.RateForFfmpeg, media.Duration, work, codec));
        return new RenderJob($"Altyazı katmanı · {Path.GetFileName(outputPath)}", command, media.Duration, work, []);
    }

    /// <summary>
    /// Marks a script's colors as plain RGB ("YCbCr Matrix: None"). Without it FFmpeg's ass filter treats
    /// them as TV-range video colors and draws pure white as 235 on an RGB canvas, so overlays came out dimmer.
    /// </summary>
    public static string WithExactColors(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var lines = script.Split('\n').Where(l => !l.TrimStart().StartsWith("YCbCr Matrix:", StringComparison.OrdinalIgnoreCase));
        var text = string.Join('\n', lines);
        var at = text.IndexOf("[Script Info]", StringComparison.Ordinal);
        if (at < 0)
        {
            return text;
        }

        var lineEnd = text.IndexOf('\n', at);
        return lineEnd < 0 ? text + "\nYCbCr Matrix: None" : text.Insert(lineEnd + 1, "YCbCr Matrix: None\n");
    }

    /// <param name="tracks">In player order; the first one is marked as the default track.</param>
    public static async Task<RenderJob> PrepareSoftSubAsync(
        Project project, IReadOnlyList<SubtitleTrack> tracks, MediaInfo media, string outputPath,
        SoftSubContainer container, string? projectFontDirectory, string jobsRoot, Core.Fonts.ITextMeasurer? measurer = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(media);
        if (tracks.Count == 0)
        {
            throw new ArgumentException("Choose at least one track.", nameof(tracks));
        }

        var work = CreateWorkDirectory(jobsRoot);
        var (width, height) = media.Video is { } v ? (v.DisplayWidth, v.DisplayHeight) : (0, 0);
        var files = new List<SoftSubTrack>();
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            string path;
            if (container == SoftSubContainer.Mkv)
            {
                // Styled ASS per track (each track is its own file, so no bilingual stacking).
                path = Path.Combine(work, $"track{i + 1}.ass");
                await File.WriteAllTextAsync(path, AssScriptBuilder.Build(project, track, width, height, allowBilingual: false, measurer: measurer), cancellationToken);
            }
            else
            {
                path = Path.Combine(work, $"track{i + 1}.srt");
                var shown = SubtitlePlans.AppliesTo(project, track) ? SubtitlePlans.Flatten(project, track) : track;
                await SubtitleFile.WriteAsync(path, SubtitleDocument.FromTrack(shown), SubtitleFormats.Srt, cancellationToken: cancellationToken);
            }

            files.Add(new SoftSubTrack(path, LanguageCodes.ToIso6392(track.Language), TrackTitle(track), i == 0));
        }

        var fonts = container == SoftSubContainer.Mkv ? ProjectFontFiles(projectFontDirectory) : [];
        var command = FfmpegCommands.SoftSub(new SoftSubRequest(media.FilePath, outputPath, media, container, files, work, fonts));
        return new RenderJob($"Altyazılı video · {Path.GetFileName(outputPath)}", command, media.Duration, work, []);
    }

    /// <summary>Removes the job's work folder (best effort).</summary>
    public static void Cleanup(RenderJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        try
        {
            if (Directory.Exists(job.WorkDirectory))
            {
                Directory.Delete(job.WorkDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder; the OS cleans it eventually.
        }
    }

    /// <summary>Track name shown in players: the language name, plus the track name when it adds something.</summary>
    internal static string TrackTitle(SubtitleTrack track)
    {
        var language = LanguageCodes.DisplayName(track.Language);
        return track.Kind == TrackKind.Source || string.IsNullOrWhiteSpace(track.Name) ? language : $"{language} ({track.Name})";
    }

    public static IReadOnlyList<string> Warnings(MediaInfo media)
        => media.Video is { } video && (video.IsHdr || video.IsHighBitDepth)
            ? ["Kaynak video HDR/10-bit. Çıktı 8-bit SDR H.264 olur; renkler biraz soluk görünebilir."]
            : [];

    private static string CreateWorkDirectory(string jobsRoot)
    {
        var work = Path.Combine(jobsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(work, FfmpegCommands.FontsFolderName));
        return work;
    }

    internal static void CopyFonts(string? from, string to)
    {
        foreach (var file in ProjectFontFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static List<string> ProjectFontFiles(string? directory)
        => string.IsNullOrEmpty(directory) || !Directory.Exists(directory)
            ? []
            : [.. Directory.EnumerateFiles(directory).Where(FontFileReader.IsFontFile).Order(StringComparer.Ordinal)];
}
