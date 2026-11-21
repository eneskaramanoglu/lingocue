using LingoCue.Core.Fonts;
using LingoCue.Core.Projects;
using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;

namespace LingoCue.Llm.Pipeline;

/// <summary>How a correction or translation run is configured and how its result joins the project.</summary>
public static class PassPlanning
{
    /// <summary>A local model may load for a minute and generate slowly on the CPU.</summary>
    public static TimeSpan LocalRequestTimeout { get; } = TimeSpan.FromMinutes(10);

    public static TimeSpan CloudRequestTimeout { get; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Pipeline options from the settings. Local models: no rate limits but slow first loads, so their own
    /// batch settings and a long timeout. Free tier: one request at a time, small batches, paced.
    /// </summary>
    /// <param name="outputLanguage">The language of the lines produced; shortening follows its reading rules.</param>
    public static PipelineOptions Options(AppSettings settings, bool local, bool reflect, string? outputLanguage)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var llm = settings.Llm;
        return new PipelineOptions
        {
            BatchSize = local ? Math.Clamp(llm.LocalBatchSize, 5, 40)
                : llm.FreeTier ? Math.Min(llm.BatchSize, LlmSettings.FreeTierBatchSize) : llm.BatchSize,
            Parallelism = local ? Math.Clamp(llm.LocalParallelism, 1, 4) : llm.FreeTier ? 1 : llm.Parallelism,
            Pacer = !local && llm.FreeTier ? RequestPacer.ForRate(llm.RequestsPerMinute) : null,
            RequestTimeout = local ? LocalRequestTimeout : CloudRequestTimeout,
            MaxRetries = llm.MaxRetries,
            Reflect = reflect,
            ShortenOverlong = llm.ShortenOverlongLines,
            Rules = settings.Reading.ToRules(outputLanguage),
        };
    }

    /// <summary>
    /// Adds a finished run's track to the project and makes it active. A repeated run gets a number
    /// ("Düzeltilmiş 2"); a first translation becomes the bilingual partner; a Korean partner gets a
    /// font with Hangul when the current one has none.
    /// </summary>
    public static PassCounts AddTrack(Project project, TrackPassResult result, TrackPassKind kind, IFontCatalog? fonts = null, string? fontsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(result);
        var track = result.Track;
        var existing = project.Tracks.Count(t => t.Kind == track.Kind && t.SourceTrackId == track.SourceTrackId && t.Language == track.Language);
        if (existing > 0)
        {
            track.Name = $"{track.Name} {existing + 1}";
        }

        if (kind == TrackPassKind.Translation && project.Presentation.Bilingual.SecondaryTrackId is null)
        {
            project.Presentation = project.Presentation with
            {
                Bilingual = project.Presentation.Bilingual with { SecondaryTrackId = track.Id },
            };
        }

        // libass draws boxes for missing glyphs, in the preview and in burn-in alike.
        var bilingual = project.Presentation.Bilingual;
        if (bilingual.SecondaryTrackId == track.Id && track.Language?.StartsWith("ko", StringComparison.OrdinalIgnoreCase) == true &&
            fonts is not null && !fonts.Check(bilingual.SecondaryStyle.FontName, fontsDirectory).HasHangul &&
            fonts.SuggestHangulFamily(fontsDirectory) is { } hangulFont)
        {
            project.Presentation = project.Presentation with
            {
                Bilingual = bilingual with { SecondaryStyle = bilingual.SecondaryStyle with { FontName = hangulFont } },
            };
        }

        project.Tracks.Add(track);
        project.ActiveTrackId = track.Id;
        return PassCounts.From(result.Pipeline);
    }
}

/// <summary>What happened to the lines of a run.</summary>
public sealed record PassCounts(int Total, int Changed, int NeedsReview, int Failed)
{
    public static PassCounts From(PipelineResult pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        var lines = pipeline.Lines;
        return new PassCounts(
            lines.Count,
            lines.Count(l => l.Status == LineStatus.Changed),
            lines.Count(l => l.Status == LineStatus.NeedsReview),
            lines.Count(l => l.Status == LineStatus.Failed));
    }
}
