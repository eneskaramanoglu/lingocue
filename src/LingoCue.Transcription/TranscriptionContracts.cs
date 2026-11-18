using LingoCue.Core.Subtitles;

namespace LingoCue.Transcription;

/// <summary>A model an engine can run, e.g. <c>large-v3-q5_0</c> or <c>gemini-3.5-transcribe</c>.</summary>
/// <param name="Id">Stable id stored in projects and settings.</param>
/// <param name="SizeBytes">Download size for local models; null for cloud models.</param>
public sealed record TranscriptionModel(string Id, string DisplayName, string Description, long? SizeBytes = null);

/// <param name="MediaPath">Video or audio file; the engine extracts what it needs.</param>
/// <param name="Duration">Media duration (from ffprobe), used for chunking and progress.</param>
/// <param name="Language">BCP 47 spoken language (<c>tr</c>); null = auto-detect.</param>
public sealed record TranscriptionRequest(
    string MediaPath,
    TimeSpan Duration,
    string? Language,
    string ModelId,
    bool Diarize = false);

public sealed record TranscriptionResult(
    IReadOnlyList<Word> Words,
    string Engine,
    string Model,
    string? DetectedLanguage,
    TimeSpan Elapsed,
    string? Device = null,
    decimal? EstimatedCostUsd = null);

public enum TranscriptionStage
{
    Preparing,
    DownloadingModel,
    ExtractingAudio,
    Uploading,
    Recognizing,
    Finishing,
}

/// <param name="Fraction">0..1 overall progress of the job.</param>
public sealed record TranscriptionProgress(TranscriptionStage Stage, double Fraction, string? Detail = null);

/// <summary>Whether an engine can run right now, and if not, what the user should do (Turkish, shown in the UI).</summary>
public sealed record EngineAvailability(bool IsAvailable, string? Problem = null, string? Remedy = null)
{
    public static EngineAvailability Ready { get; } = new(true);
}

public interface ITranscriptionEngine
{
    /// <summary>Stable id stored in projects (<c>whisper</c>, <c>gemini</c>).</summary>
    string Id { get; }

    string DisplayName { get; }

    bool IsCloud { get; }

    bool SupportsDiarization { get; }

    IReadOnlyList<TranscriptionModel> Models { get; }

    string DefaultModelId { get; }

    Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request, IProgress<TranscriptionProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class TranscriptionException : Exception
{
    public TranscriptionException()
    {
    }

    public TranscriptionException(string message)
        : base(message)
    {
    }

    public TranscriptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>What the user can do about it (Turkish), when known.</summary>
    public string? Remedy { get; init; }
}

public static class WordTimeline
{
    /// <summary>Shifts chunk-relative word times by the chunk start.</summary>
    public static IEnumerable<Word> Offset(IEnumerable<Word> words, TimeSpan offset)
        => words.Select(w => w with { Start = w.Start + offset, End = w.End + offset });

    /// <summary>
    /// Cleans engine output: trims, drops empty tokens, attaches stand-alone punctuation to the previous
    /// word, fixes inverted or overlapping times and sorts by start.
    /// </summary>
    public static List<Word> Normalize(IEnumerable<Word> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var result = new List<Word>();
        foreach (var raw in words.OrderBy(w => w.Start))
        {
            var text = raw.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var word = raw with
            {
                Text = text,
                End = raw.End < raw.Start ? raw.Start : raw.End,
            };

            if (result.Count > 0 && IsPunctuationOnly(text))
            {
                var previous = result[^1];
                result[^1] = previous with { Text = previous.Text + text, End = word.End > previous.End ? word.End : previous.End };
                continue;
            }

            if (result.Count > 0 && word.Start < result[^1].End)
            {
                // Overlap from engine rounding: trust the later word's start.
                result[^1] = result[^1] with { End = word.Start };
            }

            result.Add(word);
        }

        return result;
    }

    private static bool IsPunctuationOnly(string text) => text.All(c => char.IsPunctuation(c) || char.IsSymbol(c));
}
