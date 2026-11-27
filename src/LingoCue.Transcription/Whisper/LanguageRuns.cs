namespace LingoCue.Transcription.Whisper;

/// <summary>A speech region and the language detected in it (with Whisper's probability for that language).</summary>
internal readonly record struct DetectedRegion(SpeechRegion Region, string Language, float Probability);

/// <summary>
/// Decides the language of each speech region in a mixed-language recording. Whisper's guess on a short
/// or uncertain region ("evet", "okay", a laugh) is unreliable, so such regions take the language of
/// their neighbours; then regions are grouped by language so each language is decoded in one pass.
/// </summary>
internal static class LanguageRuns
{
    /// <summary>Regions shorter than this keep a neighbour's language unless their own guess is sure.</summary>
    internal static readonly TimeSpan MinReliable = TimeSpan.FromSeconds(1.5);

    /// <summary>Below this probability a guess counts as uncertain.</summary>
    internal const float MinProbability = 0.5f;

    /// <summary>A guess this sure is kept even on a short region ("Evet.", "Okay, let's go.").</summary>
    internal const float SureProbability = 0.8f;

    /// <summary>The language of each region (same order) after neighbours settle the unreliable ones.</summary>
    public static IReadOnlyList<string> Smooth(IReadOnlyList<DetectedRegion> regions, string fallback)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var reliable = regions.Select(IsReliable).ToArray();
        var result = new string[regions.Count];
        for (var i = 0; i < regions.Count; i++)
        {
            if (reliable[i])
            {
                result[i] = regions[i].Language;
                continue;
            }

            var before = Nearest(regions, reliable, i, -1);
            var after = Nearest(regions, reliable, i, +1);
            result[i] = (before, after) switch
            {
                (null, null) => regions[i].Probability > 0 ? regions[i].Language : fallback,
                ({ } b, null) => b.Language,
                (null, { } a) => a.Language,
                ({ } b, { } a) when b.Language == a.Language => b.Language,
                // Between two languages: the closer neighbour, the earlier one on a tie (a reply follows its question).
                ({ } b, { } a) => regions[i].Region.Start - b.Region.End <= a.Region.Start - regions[i].Region.End ? b.Language : a.Language,
            };
        }

        return result;
    }

    /// <summary>Regions grouped by language, in time order inside each group; groups ordered by first appearance.</summary>
    public static IReadOnlyList<(string Language, IReadOnlyList<SpeechRegion> Regions)> Group(IReadOnlyList<SpeechRegion> regions, IReadOnlyList<string> languages)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(languages);
        return [.. regions.Zip(languages)
            .Select((pair, index) => (pair.First, pair.Second, index))
            .GroupBy(x => x.Second, StringComparer.Ordinal)
            .OrderBy(g => g.Min(x => x.index))
            .Select(g => (g.Key, (IReadOnlyList<SpeechRegion>)[.. g.OrderBy(x => x.First.Start).Select(x => x.First)]))];
    }

    private static bool IsReliable(DetectedRegion region)
        => region.Probability >= SureProbability ||
           (region.Region.End - region.Region.Start >= MinReliable && region.Probability >= MinProbability);

    private static DetectedRegion? Nearest(IReadOnlyList<DetectedRegion> regions, bool[] reliable, int from, int step)
    {
        for (var i = from + step; i >= 0 && i < regions.Count; i += step)
        {
            if (reliable[i])
            {
                return regions[i];
            }
        }

        return null;
    }
}
