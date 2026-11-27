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
        // A mixed-language run tags its words. The track's language is the first chosen one (the dialog starts
        // the choice from the project's language) when it was spoken, else the one spoken most.
        var mixed = LanguageSelection.Mixed(run.Language);
        var main = mixed.Count > 0 && run.Words.Any(w => w.Language == mixed[0]) ? mixed[0] : null;
        var language = main ?? MostSpoken(run.Words) ?? LanguageSelection.Main(run.Language) ?? run.DetectedLanguage ?? project.SourceLanguage;
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
