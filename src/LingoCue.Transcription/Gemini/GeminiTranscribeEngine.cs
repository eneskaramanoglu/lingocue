using System.Diagnostics;
using LingoCue.Core.Security;
using LingoCue.Core.Subtitles;
using LingoCue.Media.Audio;
using Microsoft.Extensions.Logging;

namespace LingoCue.Transcription.Gemini;

/// <summary>
/// Cloud transcription with Gemini 3.5 Transcribe (verbatim, word timestamps, optional speakers).
/// Audio is sent as 16 kHz FLAC; anything longer than 25 minutes is split at silences because the
/// API silently truncates timestamped requests after 30 minutes.
/// </summary>
public sealed partial class GeminiTranscribeEngine(
    IAudioExtractor audio,
    GeminiTranscribeClient client,
    Func<string?> apiKey,
    ILogger<GeminiTranscribeEngine> logger) : ITranscriptionEngine
{
    public const string ModelId = "gemini-3.5-transcribe";
    private static readonly TimeSpan s_maxChunk = TimeSpan.FromMinutes(25);

    /// <summary>Published estimate (~$0.005 per audio minute, input + output).</summary>
    private const decimal UsdPerMinute = 0.005m;

    public string Id => "gemini";

    public string DisplayName => "Gemini 3.5 Transcribe (bulut)";

    public bool IsCloud => true;

    public bool SupportsDiarization => true;

    public IReadOnlyList<TranscriptionModel> Models { get; } =
    [
        new(ModelId, "Gemini 3.5 Transcribe", "Bulut; kelime zaman damgası ve konuşmacı ayrımı. ~0,005 $/dk."),
    ];

    public string DefaultModelId => ModelId;

    public static decimal EstimateCost(TimeSpan duration) => Math.Round((decimal)duration.TotalMinutes * UsdPerMinute, 4);

    public Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(string.IsNullOrWhiteSpace(apiKey())
            ? new EngineAvailability(false, "Gemini API anahtarı girilmemiş.", "Ayarlar → API anahtarları bölümünden Gemini anahtarınızı girin (Google AI Studio'dan alınır).")
            : EngineAvailability.Ready);

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request, IProgress<TranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new TranscriptionException("Gemini API key is missing.")
            {
                Remedy = "Ayarlar → API anahtarları bölümünden Gemini anahtarınızı girin.",
            };
        }

        var stopwatch = Stopwatch.StartNew();
        progress?.Report(new(TranscriptionStage.Preparing, 0));
        var silences = request.Duration > s_maxChunk
            ? await audio.DetectSilenceAsync(request.MediaPath, request.Duration, cancellationToken: cancellationToken)
            : [];
        var chunks = AudioChunkPlanner.Plan(request.Duration, s_maxChunk, silences);
        var languages = ToGeminiLanguages(request.Language);
        var words = new List<Word>();
        var work = Path.Combine(Path.GetTempPath(), "LingoCue");
        Directory.CreateDirectory(work);

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var share = 1.0 / chunks.Count;
            var baseFraction = chunk.Index * share;
            var detail = chunks.Count > 1 ? $"Parça {chunk.Index + 1}/{chunks.Count}" : null;
            var flac = Path.Combine(work, $"asr-{Guid.NewGuid():N}.flac");
            UploadedFile? uploaded = null;
            try
            {
                await audio.ExtractAsync(request.MediaPath, flac, AudioOutputFormat.Flac16kMono,
                    start: chunk.Start, duration: chunk.Duration,
                    progress: new Progress<double>(f => progress?.Report(new(TranscriptionStage.ExtractingAudio, baseFraction + (f * share * 0.15), detail))),
                    cancellationToken: cancellationToken);

                progress?.Report(new(TranscriptionStage.Uploading, baseFraction + (share * 0.2), detail));
                uploaded = await client.UploadAsync(key, flac, "audio/flac", cancellationToken);

                progress?.Report(new(TranscriptionStage.Recognizing, baseFraction + (share * 0.35), detail));
                var chunkWords = await client.TranscribeAsync(key, request.ModelId, uploaded, languages, request.Diarize, cancellationToken);
                words.AddRange(WordTimeline.Offset(chunkWords, chunk.Start));
                LogChunk(chunk.Index + 1, chunks.Count, chunkWords.Count);
            }
            finally
            {
                if (uploaded is not null)
                {
                    await client.DeleteAsync(key, uploaded);
                }

                TryDelete(flac);
            }
        }

        progress?.Report(new(TranscriptionStage.Finishing, 1));
        return new TranscriptionResult(
            WordTimeline.Normalize(words), Id, request.ModelId, request.Language, stopwatch.Elapsed, "Google Cloud", EstimateCost(request.Duration));
    }

    /// <summary>Gemini expects BCP 47 with region (<c>tr-TR</c>); null/auto = automatic detection.</summary>
    internal static IReadOnlyList<string>? ToGeminiLanguages(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (language.Contains('-', StringComparison.Ordinal))
        {
            return [language];
        }

        return [language.ToLowerInvariant() switch
        {
            "tr" => "tr-TR",
            "en" => "en-US",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "es" => "es-ES",
            "it" => "it-IT",
            "ar" => "ar-SA",
            "ru" => "ru-RU",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "zh" => "zh-CN",
            "pt" => "pt-BR",
            "nl" => "nl-NL",
            var other => other,
        }];
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Gemini chunk {Chunk}/{Total}: {Words} words")]
    private partial void LogChunk(int chunk, int total, int words);
}
