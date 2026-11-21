using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;

namespace LingoCue.Llm.Pipeline;

/// <summary>One subtitle line sent to the model. <see cref="Id"/> keys the JSON round-trip.</summary>
/// <param name="Reference">
/// What the speech recognizer heard for this line in the original language (may contain recognition
/// errors). Given to translation as a hint; the edited <paramref name="Text"/> stays authoritative.
/// </param>
public sealed record LineItem(int Id, string Text, TimeSpan Duration, string? Speaker = null, bool Locked = false, int? MaxChars = null, string? Reference = null);

public enum LineStatus
{
    /// <summary>The model returned the same text.</summary>
    Unchanged,

    /// <summary>The model changed the text and it passed validation.</summary>
    Changed,

    /// <summary>Changed a lot (or broke a soft rule): accept but ask a human to look.</summary>
    NeedsReview,

    /// <summary>No valid answer after retries: the source text is kept.</summary>
    Failed,

    /// <summary>Locked by the user: not sent to the model.</summary>
    Skipped,
}

public sealed record LineResult(int Id, string Text, LineStatus Status, string? Note = null);

public sealed record LlmUsage(long InputTokens, long OutputTokens, int Requests)
{
    public static LlmUsage Zero { get; } = new(0, 0, 0);

    public LlmUsage Add(long input, long output) => new(InputTokens + input, OutputTokens + output, Requests + 1);

    public LlmUsage Add(LlmUsage other) => new(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens, Requests + other.Requests);
}

public sealed record PipelineOptions
{
    public int BatchSize { get; init; } = 30;

    public int Parallelism { get; init; } = 2;

    public int MaxRetries { get; init; } = 2;

    /// <summary>Already-processed lines shown before a batch (read-only context).</summary>
    public int PreviousContext { get; init; } = 4;

    /// <summary>Upcoming source lines shown after a batch (read-only context).</summary>
    public int NextContext { get; init; } = 2;

    public bool Reflect { get; init; }

    public bool ShortenOverlong { get; init; } = true;

    public ReadingRules Rules { get; init; } = ReadingRules.Default;

    /// <summary>Above this normalized edit distance a corrected line is flagged for review.</summary>
    public double ReviewChangeRatio { get; init; } = 0.6;

    /// <summary>
    /// Longest wait for one model answer. Without it a stalled request (seen live while Google was
    /// overloaded) kept a run on "Bağlam çıkarılıyor" for minutes; a timed-out call is retried.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Back-off for rate limits and transient server errors.</summary>
    public IReadOnlyList<TimeSpan> TransientDelays { get; init; } =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];

    public double Temperature { get; init; } = 0.2;

    /// <summary>Spaces requests for a free tier's per-minute limit; null = no pacing.</summary>
    public RequestPacer? Pacer { get; init; }

    /// <summary>Longest wait honored when the API asks to retry later (a longer hint is cut to this).</summary>
    public TimeSpan MaxRetryWait { get; init; } = TimeSpan.FromSeconds(90);
}

public sealed record PipelineProgress(string Stage, int Done, int Total)
{
    public double Fraction => Total > 0 ? (double)Done / Total : 0;
}

public sealed record PipelineResult(
    IReadOnlyList<LineResult> Lines,
    LlmUsage Usage,
    int FailedBatches)
{
    public int ChangedCount => Lines.Count(l => l.Status is LineStatus.Changed or LineStatus.NeedsReview);

    public int ReviewCount => Lines.Count(l => l.Status == LineStatus.NeedsReview);

    public int FailedCount => Lines.Count(l => l.Status == LineStatus.Failed);
}

/// <summary>Everything the task prompts may use about the project.</summary>
public sealed record TaskContext(
    ProjectContext Project,
    IReadOnlyList<GlossaryEntry> Glossary,
    string SourceLanguage,
    string? TargetLanguage = null);
