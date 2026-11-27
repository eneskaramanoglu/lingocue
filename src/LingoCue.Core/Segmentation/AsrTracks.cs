using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;

namespace LingoCue.Core.Segmentation;

public static class AsrTracks
{
    /// <summary>
    /// Turns a recognition run into a source track: readable lines, doubtful lines marked for review.
    /// The empty placeholder track a new project starts with is replaced; the new track becomes active.
    /// </summary>
    /// <returns>The new track and how many of its lines were marked for review.</returns>
    public static (SubtitleTrack Track, int Doubtful) Add(Project project, AsrRun run, string engineName)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(engineName);
        // A mixed-language run tags its words; the track's language is the one spoken most.
        var language = MostSpoken(run.Words) ?? LanguageSelection.Main(run.Language) ?? run.DetectedLanguage ?? project.SourceLanguage;
        var cues = Segmenter.Segment(run.Words, new SegmentationOptions { Language = language });
        var doubtful = AsrReview.FlagDoubtful(cues);
        var track = new SubtitleTrack
        {
            Name = $"Kaynak · {engineName.Split(' ')[0]} {run.Model}",
            Language = language,
            Kind = TrackKind.Source,
            Cues = [.. cues],
        };

        if (project.ActiveTrack is { Cues.Count: 0 } empty)
        {
            project.Tracks.Remove(empty);
        }

        project.Tracks.Add(track);
        project.ActiveTrackId = track.Id;
        return (track, doubtful);
    }

    /// <summary>The language with the most words, when the words carry languages.</summary>
    internal static string? MostSpoken(IReadOnlyList<Word> words)
        => words.Where(w => !string.IsNullOrWhiteSpace(w.Language))
            .GroupBy(w => w.Language!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key).FirstOrDefault();
}
