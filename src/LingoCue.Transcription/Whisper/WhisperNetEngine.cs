using System.Diagnostics;
using LingoCue.Core.Platform;
using LingoCue.Core.Subtitles;
using LingoCue.Media.Audio;
using LingoCue.Transcription.Models;
using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace LingoCue.Transcription.Whisper;

/// <summary>
/// Local transcription with whisper.cpp through Whisper.net. GPU runtime order: CUDA → Vulkan → CPU
/// on Windows, CoreML → CPU on macOS; both macOS runtimes run on the GPU through Metal (the
/// Whisper.net.Runtime.Metal package ships the shader) and the CoreML one falls back to Metal
/// without its encoder model. The device actually used is read from whisper.cpp's log. Models are
/// downloaded on first use.
/// </summary>
public sealed partial class WhisperNetEngine : ITranscriptionEngine, IDisposable
{
    private static readonly TimeSpan s_vadPadding = TimeSpan.FromMilliseconds(300);

    /// <summary>Speech regions closer than this are decoded together, with their real pause.</summary>
    private static readonly TimeSpan s_mergeGap = TimeSpan.FromSeconds(1);

    /// <summary>Pause left between regions that were further apart, so Whisper still hears a break.</summary>
    private static readonly TimeSpan s_joinGap = TimeSpan.FromMilliseconds(600);
    private readonly IAudioExtractor _audio;
    private readonly ModelDownloader _downloader;
    private readonly IPlatformInfo _platform;
    private readonly ILogger<WhisperNetEngine> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private string? _factoryModel;

    public WhisperNetEngine(IAudioExtractor audio, ModelDownloader downloader, IPlatformInfo platform, ILogger<WhisperNetEngine> logger)
    {
        _audio = audio;
        _downloader = downloader;
        _platform = platform;
        _logger = logger;
        RuntimeOptions.RuntimeLibraryOrder = platform.Platform == OsPlatform.MacOS
            ? [RuntimeLibrary.CoreML, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]
            : [RuntimeLibrary.Cuda, RuntimeLibrary.Cuda12, RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
    }

    public string Id => "whisper";

    public string DisplayName => "Whisper (yerel)";

    public bool IsCloud => false;

    public bool SupportsDiarization => false;

    public IReadOnlyList<TranscriptionModel> Models => WhisperModels.All;

    public string DefaultModelId => WhisperModels.LargeV3Q5;

    public string ModelDirectory => Path.Combine(_platform.AppDataDirectory, "models");

    public bool IsModelDownloaded(string modelId) => ModelDownloader.IsDownloaded(ModelDirectory, WhisperModels.FileFor(modelId));

    public Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(EngineAvailability.Ready); // CPU always works; the device is reported after loading

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request, IProgress<TranscriptionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        await _gate.WaitAsync(cancellationToken); // one native model in memory at a time
        var wav = Path.Combine(Path.GetTempPath(), "LingoCue", $"asr-{Guid.NewGuid():N}.wav");
        try
        {
            progress?.Report(new(TranscriptionStage.Preparing, 0));

            var modelFile = WhisperModels.FileFor(request.ModelId);
            var modelPath = await _downloader.EnsureAsync(ModelDirectory, modelFile,
                Scale(progress, TranscriptionStage.DownloadingModel, 0, 0.2, modelFile.FileName), cancellationToken);
            var vadPath = await _downloader.EnsureAsync(ModelDirectory, WhisperModels.SileroVad, cancellationToken: cancellationToken);
            if (_platform.Platform == OsPlatform.MacOS)
            {
                // whisper.cpp uses the Core ML encoder when its folder sits next to the model, Metal otherwise.
                if (request.UseNeuralEngine)
                {
                    await CoreMlEncoder.EnsureAsync(ModelDirectory, request.ModelId, _downloader,
                        Scale(progress, TranscriptionStage.DownloadingModel, 0, 0.2, "Neural Engine kodlayıcısı (~1,2 GB, bir kez)"), cancellationToken);
                }
                else
                {
                    CoreMlEncoder.SetEnabled(ModelDirectory, request.ModelId, enabled: false);
                }
            }

            await _audio.ExtractAsync(request.MediaPath, wav, AudioOutputFormat.Wav16kMono, totalForProgress: request.Duration,
                progress: new Progress<double>(f => progress?.Report(new(TranscriptionStage.ExtractingAudio, 0.2 + (f * 0.1)))),
                cancellationToken: cancellationToken);

            var speech = await DetectSpeechAsync(vadPath, wav, cancellationToken);
            // Core ML compiles the encoder for this Mac on its first use; that can take minutes.
            progress?.Report(new(TranscriptionStage.Recognizing, 0.32,
                request.UseNeuralEngine && _platform.Platform == OsPlatform.MacOS ? "Neural Engine hazırlanıyor (ilk seferde birkaç dakika sürebilir)" : null));

            // whisper.cpp picks its backend while the model and state load; its log says which one.
            var nativeLog = new List<string>();
            using var logSubscription = LogProvider.AddLogger((level, message) =>
            {
                if (message is null)
                {
                    return;
                }

                lock (nativeLog)
                {
                    nativeLog.Add(message);
                }

                if (level is WhisperLogLevel.Error or WhisperLogLevel.Warning)
                {
                    LogNative(level.ToString(), message.TrimEnd());
                }
            });

            var factory = GetFactory(modelPath, request.ModelId);
            var device = "?";
            var language = ToWhisperLanguage(request.Language);

            var builder = factory.CreateBuilder()
                .WithLanguage(language)
                .WithTokenTimestamps()
                .WithProbabilities()
                .WithProgressHandler(p => progress?.Report(new(TranscriptionStage.Recognizing, 0.32 + (p / 100.0 * 0.66), device)));
            if (PromptFor(language, request.Hints) is { } prompt)
            {
                builder = builder.WithPrompt(prompt);
                if (request.Hints is { Count: > 0 })
                {
                    // Keep the names in front of every 30-second window, not only the first one.
                    builder = builder.WithCarryInitialPrompt(true);
                }
            }

            if (request.BeamSearch)
            {
                builder = builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(BeamSize));
            }

            await using var processor = builder.Build();
            device = DescribeDevice(nativeLog);
            LogStarting(request.ModelId, device, language);

            // Only the speech regions are decoded (see SpeechTimeline); times are mapped back afterwards.
            var (samples, sampleRate) = WavSamples.Read(wav);
            var timeline = speech.Count > 0
                ? SpeechTimeline.Build(speech, request.Duration > TimeSpan.Zero ? request.Duration : TimeSpan.FromSeconds(samples.Length / (double)sampleRate),
                    s_vadPadding, s_mergeGap, s_joinGap)
                : null;
            var input = timeline?.Pack(samples, sampleRate) ?? samples;
            var originalLength = TimeSpan.FromSeconds(samples.Length / (double)sampleRate);
            var packedLength = TimeSpan.FromSeconds(input.Length / (double)sampleRate);
            LogPacked(originalLength, packedLength, timeline?.Pieces.Count ?? 0);

            var words = new List<Word>();
            string? detected = null;
            string? previous = null;
            var dropped = 0;
            var suspicious = 0;
            await foreach (var segment in processor.ProcessAsync(input, cancellationToken))
            {
                detected ??= segment.Language;
                var start = timeline?.ToSource(segment.Start) ?? segment.Start;
                var end = timeline?.ToSource(segment.End) ?? segment.End;
                if (!WhisperTokenWords.OverlapsSpeech(start, end, speech, s_vadPadding))
                {
                    dropped++;
                    continue;
                }

                var tokens = segment.Tokens.Select(t => new RawToken(t.Text ?? string.Empty, t.Start, t.End, t.Probability, t.DtwTimestamp));
                var segmentWords = WhisperTokenWords.ToWords(tokens);
                var doubtful = WhisperHallucinations.IsSuspicious(segment.Text ?? string.Empty, previous, segment.NoSpeechProbability, segment.Probability);
                suspicious += doubtful ? 1 : 0;
                previous = segment.Text;
                foreach (var word in segmentWords)
                {
                    var mapped = timeline is null ? word : word with { Start = timeline.ToSource(word.Start), End = timeline.ToSource(word.End) };
                    words.Add(doubtful ? mapped with { Confidence = 0 } : mapped);
                }
            }

            progress?.Report(new(TranscriptionStage.Finishing, 1, device));
            LogFinished(words.Count, dropped, suspicious, stopwatch.Elapsed);
            return new TranscriptionResult(WordTimeline.Normalize(words), Id, request.ModelId, detected, stopwatch.Elapsed, device, 0m);
        }
        catch (Exception ex) when (WhisperLoadFailure(ex))
        {
            throw new TranscriptionException($"Whisper could not run: {ex.Message}", ex)
            {
                Remedy = "Model yüklenemedi. GPU belleği yetmiyor olabilir: daha küçük 'large-v3-turbo' modelini deneyin. " +
                         "Sorun sürerse ekran kartı sürücünüzü güncelleyin.",
            };
        }
        finally
        {
            TryDelete(wav);
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _gate.Dispose();
    }

    /// <summary>
    /// Whisper continues the style of its prompt. A short, punctuated Turkish sentence nudges it toward
    /// punctuated output with Turkish characters instead of a bare word stream.
    /// </summary>
    internal static string? PromptFor(string whisperLanguage, IReadOnlyList<string>? hints = null)
    {
        var style = whisperLanguage switch
        {
            "tr" => "Merhaba, hoş geldiniz. Bugün neler yapacağız? Evet, başlayalım.",
            "en" => "Hello, welcome. What are we doing today? Yes, let's begin.",
            _ => null,
        };

        // Names from the glossary, spelled the way they should come out. Whisper reads at most 224
        // prompt tokens (and the carried prompt takes room from the context), so the list is capped.
        var names = new List<string>();
        var length = 0;
        foreach (var hint in hints ?? [])
        {
            var name = hint.Trim();
            if (name.Length == 0 || name.Length > 40 || names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (length + name.Length + 2 > MaxHintCharacters)
            {
                break;
            }

            names.Add(name);
            length += name.Length + 2;
        }

        if (names.Count == 0)
        {
            return style;
        }

        var list = string.Join(", ", names) + ".";
        return style is null ? list : $"{style} {list}";
    }

    /// <summary>Beam width used by the "En iyi" accuracy mode (the value OpenAI's Whisper uses).</summary>
    internal const int BeamSize = 5;

    /// <summary>Room for glossary names in the prompt (about 60 tokens of Turkish text).</summary>
    internal const int MaxHintCharacters = 180;

    internal static string ToWhisperLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : language.Split('-', '_')[0].ToLowerInvariant();

    private static async Task<IReadOnlyList<SpeechRegion>> DetectSpeechAsync(string vadPath, string wav, CancellationToken cancellationToken)
    {
        using var vadFactory = WhisperVadFactory.FromPath(vadPath);
        using var vad = vadFactory.CreateBuilder().WithThreshold(0.5f).Build();
        await using var stream = File.OpenRead(wav);
        var segments = await vad.DetectSpeechAsync(stream, cancellationToken);
        return [.. segments.Select(s => new SpeechRegion(s.Start, s.End))];
    }

    private WhisperFactory GetFactory(string modelPath, string modelId)
    {
        if (_factory is not null && _factoryModel == modelId)
        {
            return _factory;
        }

        _factory?.Dispose();
        // DTW aligns every token against the audio; whisper's default token times collapse to
        // segment edges in fast, overlapping speech (docs/reports/asr-comparison.md).
        _factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions
        {
            UseDtwTimeStamps = true,
            HeadsPreset = modelId == WhisperModels.LargeV3TurboQ5 ? WhisperAlignmentHeadsPreset.LargeV3Turbo : WhisperAlignmentHeadsPreset.LargeV3,
        });
        _factoryModel = modelId;
        return _factory;
    }

    private string DescribeDevice(List<string> nativeLog)
    {
        var loaded = RuntimeOptions.LoadedLibrary;
        string text;
        lock (nativeLog)
        {
            text = WhisperDevice.Describe(loaded?.ToString(), nativeLog);
        }

        // NVIDIA driver present but CUDA runtime not used: CUDA Toolkit is missing.
        if (_platform.Platform == OsPlatform.Windows && loaded is not (RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12) &&
            File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll")))
        {
            text += " — NVIDIA CUDA için CUDA Toolkit 13 kurulabilir (daha hızlı)";
        }

        return text;
    }

    private static bool WhisperLoadFailure(Exception ex)
        => ex is WhisperModelLoadException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;

    private static Progress<DownloadProgress>? Scale(IProgress<TranscriptionProgress>? progress, TranscriptionStage stage, double from, double to, string detail)
        => progress is null ? null : new Progress<DownloadProgress>(p => progress.Report(new(stage, from + (p.Fraction * (to - from)), detail)));

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "whisper.cpp {Level}: {Message}")]
    private partial void LogNative(string level, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Whisper {Model} on {Device}, language {Language}")]
    private partial void LogStarting(string model, string device, string language);

    [LoggerMessage(Level = LogLevel.Information, Message = "Whisper finished: {Words} words, {Dropped} segments dropped by VAD, {Suspicious} flagged as possible hallucinations, {Elapsed}")]
    private partial void LogFinished(int words, int dropped, int suspicious, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Speech regions: {Pieces}, audio {Original} packed to {Packed}")]
    private partial void LogPacked(TimeSpan original, TimeSpan packed, int pieces);
}
