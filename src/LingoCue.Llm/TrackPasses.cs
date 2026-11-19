using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;
using LingoCue.Llm.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LingoCue.Llm;

public enum TrackPassKind
{
    Correction,
    Translation,
}

public sealed record TrackPassRequest(
    Project Project,
    SubtitleTrack Source,
    TrackPassKind Kind,
    PipelineOptions Options,
    bool RefreshContext,
    bool KeepCircumflex = false,
    string? TargetLanguage = null);

public sealed record TrackPassResult(SubtitleTrack Track, PipelineResult Pipeline, LlmUsage TotalUsage, int NewGlossarySuggestions);

/// <summary>
/// Applies a pipeline pass to a whole track: optional context pass (fills project context and glossary
/// suggestions), then line batches, then builds a new track whose cues copy the source timing exactly
/// and link back through <see cref="Cue.SourceCueId"/>. The source track is never modified.
/// </summary>
public sealed class TrackPassRunner(IChatClient client, ILoggerFactory loggers)
{
    public async Task<TrackPassResult> RunAsync(
        TrackPassRequest request, IProgress<PipelineProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var project = request.Project;
        var source = request.Source;
        var lines = source.Cues
            .Select(c => new LineItem(c.Id, c.Text, c.Duration, c.Speaker, c.Marks.HasFlag(CueMarks.Locked)))
            .ToList();

        var usage = LlmUsage.Zero;
        var suggestions = 0;
        if (request.RefreshContext || project.Context.Summary is null)
        {
            progress?.Report(new PipelineProgress("Bağlam çıkarılıyor", 0, 1));
            var context = await new ContextExtractor(client).ExtractAsync(lines, source.Language, project.Glossary, cancellationToken);
            project.Context = context.Context;
            var before = project.Glossary.Count;
            var merged = project.Glossary.MergeSuggestions(context.Suggestions, TurkishText.Culture);
            project.Glossary.Clear();
            project.Glossary.AddRange(merged);
            suggestions = project.Glossary.Count - before;
            usage = usage.Add(context.Usage);
        }

        var targetLanguage = request.Kind == TrackPassKind.Translation ? request.TargetLanguage ?? "en" : null;
        var taskContext = new TaskContext(project.Context, [.. project.Glossary.For(targetLanguage)], source.Language, targetLanguage);
        ILineTask task = request.Kind == TrackPassKind.Correction ? new CorrectionTask(request.KeepCircumflex) : new TranslationTask();

        var runner = new LineBatchRunner(client, loggers.CreateLogger<LineBatchRunner>());
        var pipeline = await runner.RunAsync(task, lines, taskContext, request.Options, progress, cancellationToken);
        usage = usage.Add(pipeline.Usage);

        var track = BuildTrack(source, pipeline, request.Kind, targetLanguage);
        return new TrackPassResult(track, pipeline, usage, suggestions);
    }

    internal static SubtitleTrack BuildTrack(SubtitleTrack source, PipelineResult pipeline, TrackPassKind kind, string? targetLanguage)
    {
        var byId = pipeline.Lines.ToDictionary(l => l.Id);
        var track = new SubtitleTrack
        {
            Name = kind == TrackPassKind.Correction ? "Düzeltilmiş" : $"Çeviri · {targetLanguage}",
            Language = kind == TrackPassKind.Correction ? source.Language : targetLanguage!,
            Kind = kind == TrackPassKind.Correction ? TrackKind.Corrected : TrackKind.Translation,
            SourceTrackId = source.Id,
        };

        foreach (var cue in source.Cues)
        {
            var copy = cue.Clone();
            copy.SourceCueId = cue.Id;
            if (byId.TryGetValue(cue.Id, out var result))
            {
                copy.Text = result.Text;
                copy.Marks &= ~(CueMarks.NeedsReview | CueMarks.LlmFailed);
                copy.Marks |= result.Status switch
                {
                    LineStatus.NeedsReview => CueMarks.NeedsReview,
                    LineStatus.Failed => CueMarks.LlmFailed,
                    _ => CueMarks.None,
                };
            }

            if (kind == TrackPassKind.Translation)
            {
                copy.Words.Clear(); // source-language word timings do not apply to the translation
            }

            track.Cues.Add(copy);
        }

        return track;
    }
}
