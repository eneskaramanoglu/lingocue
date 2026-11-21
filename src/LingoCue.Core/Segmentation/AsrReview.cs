using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Segmentation;

/// <summary>Ties speech recognition to the project: glossary spelling hints in, doubtful lines flagged out.</summary>
public static class AsrReview
{
    /// <summary>
    /// A word below this confidence marks its line for review. Whisper's token probabilities sit
    /// around 0.8–1.0 for clear speech; a hallucinated segment gets 0.
    /// </summary>
    public const double DoubtfulConfidence = 0.25;

    /// <summary>Source-language glossary terms as the recognizer should spell them (target-language entries are left out).</summary>
    public static IReadOnlyList<string> SpellingHints(IEnumerable<GlossaryEntry> glossary)
    {
        ArgumentNullException.ThrowIfNull(glossary);
        return [.. glossary
            .Where(e => string.IsNullOrWhiteSpace(e.TargetLanguage))
            .OrderBy(e => e.Suggested) // confirmed entries first: the hint list is capped
            .Select(e => e.Effective.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Marks lines holding a doubtful word for review; returns how many were marked.</summary>
    public static int FlagDoubtful(IEnumerable<Cue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        var flagged = 0;
        foreach (var cue in cues)
        {
            if (cue.Words.Any(w => w.Confidence < DoubtfulConfidence))
            {
                cue.Marks |= CueMarks.NeedsReview;
                flagged++;
            }
        }

        return flagged;
    }
}
