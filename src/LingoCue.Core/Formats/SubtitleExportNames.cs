using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Formats;

public static class SubtitleExportNames
{
    /// <summary>
    /// <c>film.tr</c>, or <c>film.tr.Düzeltilmiş</c> when two of the exported tracks share a language
    /// (characters not allowed in file names become "_", " · " becomes "-").
    /// </summary>
    public static string For(string baseName, IReadOnlyList<SubtitleTrack> exported, SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(exported);
        ArgumentNullException.ThrowIfNull(track);
        var name = $"{baseName}.{track.Language}";
        if (exported.Count(t => t.Language == track.Language) > 1)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = string.Concat(track.Name.Select(c => invalid.Contains(c) || c is ':' or '/' or '\\' ? '_' : c)).Trim();
            name += "." + safe.Replace(" · ", "-", StringComparison.Ordinal);
        }

        return name;
    }

    /// <summary>The file name stem for exports: the video's name, else the project's, else "altyazi".</summary>
    public static string BaseName(string? videoPath, string? projectPath)
        => Path.GetFileNameWithoutExtension(videoPath ?? projectPath ?? "altyazi");
}
