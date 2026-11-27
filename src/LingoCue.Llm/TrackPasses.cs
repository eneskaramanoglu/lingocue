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
    string? TargetLanguage = null,
    IReadOnlySet<int>? OnlySourceCueIds = null);

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
        // Translation gets the recognized original speech as a hint next to the edited text.
        var withReference = request.Kind == TrackPassKind.Translation;
        var targetLanguage = request.Kind == TrackPassKind.Translation ? request.TargetLanguage ?? "en" : null;
        var spoken = source.SpokenLanguages();
        var mixed = spoken.Count > 1;
        var lines = source.Cues
            .Where(c => request.OnlySourceCueIds is null || request.OnlySourceCueIds.Contains(c.Id))
            .Select(c => new LineItem(c.Id, c.Text, c.Duration, c.Speaker,
                c.Marks.HasFlag(CueMarks.Locked) || AlreadyInTarget(source, c, targetLanguage),
                Reference: withReference ? TrackOperations.OriginalText(project, source, c) : null,
                Language: mixed ? LanguageCodes.Primary(source.LanguageOf(c)) : null))
            .ToList();

        var usage = LlmUsage.Zero;
        var suggestions = 0;
        if (request.RefreshContext || project.Context.Summary is null)
        {
            progress?.Report(new PipelineProgress("Bağlam çıkarılıyor", 0, 1));
            var context = await new ContextExtractor(client) { Policy = request.Options }.ExtractAsync(lines, source.Language, project.Glossary, cancellationToken);
            project.Context = context.Context;
            var before = project.Glossary.Count;
            var merged = project.Glossary.MergeSuggestions(context.Suggestions, TurkishText.Culture);
            project.Glossary.Clear();
            project.Glossary.AddRange(merged);
            suggestions = project.Glossary.Count - before;
            usage = usage.Add(context.Usage);
        }

        var taskContext = new TaskContext(project.Context, [.. project.Glossary.For(targetLanguage)], source.Language, targetLanguage)
        {
            SourceLanguages = mixed ? [.. spoken.Select(LanguageCodes.Primary)] : null,
        };
        ILineTask task = request.Kind == TrackPassKind.Correction ? new CorrectionTask(request.KeepCircumflex) : new TranslationTask();

        var runner = new LineBatchRunner(client, loggers.CreateLogger<LineBatchRunner>());
        var pipeline = await runner.RunAsync(task, lines, taskContext, request.Options, progress, cancellationToken);
        usage = usage.Add(pipeline.Usage);

        var track = BuildTrack(source, pipeline, request.Kind, targetLanguage);
        return new TrackPassResult(track, pipeline, usage, suggestions);
    }

    /// <summary>
    /// Writes the results of a pass over some lines (<see cref="TrackPassRequest.OnlySourceCueIds"/>) into an
    /// existing derived track, matching cues by <see cref="Cue.SourceCueId"/>. Lines that failed again keep
    /// their text and their failed mark. Returns how many lines were fixed and how many still fail.
    /// </summary>
    public static (int Fixed, int StillFailed) MergeInto(SubtitleTrack target, PipelineResult pipeline)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(pipeline);
        var byId = pipeline.Lines.ToDictionary(l => l.Id);
        int fixedCount = 0, failed = 0;
        foreach (var cue in target.Cues)
        {
            if (cue.SourceCueId is not { } id || !byId.TryGetValue(id, out var result))
            {
                continue;
            }

            if (result.Status == LineStatus.Failed)
            {
                failed++;
                continue;
            }

            cue.Text = result.Text;
            cue.Marks &= ~(CueMarks.NeedsReview | CueMarks.LlmFailed);
            if (result.Status == LineStatus.NeedsReview)
            {
                cue.Marks |= CueMarks.NeedsReview;
            }

            fixedCount++;
        }

        return (fixedCount, failed);
    }

    /// <summary>A line of a mixed track already spoken in the target language is kept as it is, not translated.</summary>
    internal static bool AlreadyInTarget(SubtitleTrack source, Cue cue, string? targetLanguage)
        => targetLanguage is not null && LanguageCodes.Primary(source.LanguageOf(cue)) == LanguageCodes.Primary(targetLanguage);

    /// <summary>
    /// Writes a pass over some lines into a translation track, adding the lines it does not have yet
    /// (used to fill the translations a subtitle plan needs). Failed lines are not added, so the plan
    /// still reports them. Returns how many lines were added or updated and how many failed.
    /// </summary>
    public static (int Written, int Failed) MergeOrAdd(SubtitleTrack target, SubtitleTrack source, PipelineResult pipeline)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pipeline);
        var sourceCues = source.Cues.ToDictionary(c => c.Id);
        var existing = target.Cues.Where(c => c.SourceCueId is not null).GroupBy(c => c.SourceCueId!.Value).ToDictionary(g => g.Key, g => g.First());
        int written = 0, failed = 0;
        foreach (var line in pipeline.Lines)
        {
            if (line.Status == LineStatus.Failed)
            {
                failed++;
                continue;
            }

            if (!existing.TryGetValue(line.Id, out var cue))
            {
                if (!sourceCues.TryGetValue(line.Id, out var from))
                {
                    continue;
                }

                cue = new Cue(target.NextCueId(), from.Start, from.End, line.Text) { SourceCueId = from.Id, Speaker = from.Speaker };
                target.Cues.Add(cue);
            }

            cue.Text = line.Text;
            cue.Marks &= ~(CueMarks.NeedsReview | CueMarks.LlmFailed);
            if (line.Status == LineStatus.NeedsReview)
            {
                cue.Marks |= CueMarks.NeedsReview;
            }

            written++;
        }

        target.Sort();
        return (written, failed);
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
                copy.Language = null; // every line of a translation is in its language
            }

            track.Cues.Add(copy);
        }

        return track;
    }
}
