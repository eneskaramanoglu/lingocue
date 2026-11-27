using LingoCue.Core.Segmentation;
using LingoCue.Core.Subtitles;
using LingoCue.Infrastructure.Platform;
using LingoCue.Media.Audio;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;
using LingoCue.Transcription.Models;
using LingoCue.Transcription.Whisper;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Transcription.Tests;

/// <summary>
/// Runs real whisper.cpp on a recording that mixes languages. Opt-in (downloads the model once): set
/// LINGOCUE_WHISPER_MIXED_AUDIO to the recording and LINGOCUE_WHISPER_MIXED_LANGUAGES to its languages
/// (default <c>tr+en</c>); LINGOCUE_WHISPER_TEST_MODEL picks the model. Optional
/// LINGOCUE_WHISPER_MIXED_EXPECTED lists the expected language per line (<c>tr,en,tr,…</c>) to score it.
/// The result is written to <c>lingocue-whisper-mixed.txt</c> in the temp folder.
/// </summary>
public class WhisperMixedLanguageIntegrationTests
{
    [Fact]
    public async Task Tags_each_stretch_with_its_language()
    {
        var audio = Environment.GetEnvironmentVariable("LINGOCUE_WHISPER_MIXED_AUDIO");
        if (string.IsNullOrWhiteSpace(audio) || !File.Exists(audio))
        {
            Assert.Skip("Set LINGOCUE_WHISPER_MIXED_AUDIO to run the real mixed-language Whisper test.");
        }

        var languages = Environment.GetEnvironmentVariable("LINGOCUE_WHISPER_MIXED_LANGUAGES") ?? "tr+en";
        var ct = TestContext.Current.CancellationToken;
        var platform = new PlatformInfo();
        var locator = new MediaToolLocator(platform, new MediaToolOptions(), NullLogger<MediaToolLocator>.Instance);
        var runner = new ProcessRunner();
        var info = await new LingoCue.Media.Probing.MediaProbe(locator, runner).ProbeAsync(audio, ct);
        var engineLog = new ListLogger<WhisperNetEngine>();
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        using var engine = new WhisperNetEngine(
            new AudioExtractor(locator, runner),
            new ModelDownloader(http, NullLogger<ModelDownloader>.Instance),
            platform,
            engineLog);
        var model = Environment.GetEnvironmentVariable("LINGOCUE_WHISPER_TEST_MODEL") ?? engine.DefaultModelId;

        var result = await engine.TranscribeAsync(new TranscriptionRequest(audio, info.Duration, languages, model), cancellationToken: ct);

        var cues = Segmenter.Segment(result.Words, new SegmentationOptions { Language = result.DetectedLanguage ?? "tr" });
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var log = new System.Text.StringBuilder();
        log.AppendLine(inv, $"Device: {result.Device}; elapsed {result.Elapsed}; main language {result.DetectedLanguage}; languages {languages}");
        foreach (var cue in cues)
        {
            var doubtful = cue.Words.Any(w => w.Confidence < 0.25) ? "  [kontrol]" : string.Empty;
            log.AppendLine(inv, $"{cue.Start:mm\\:ss\\.fff} -> {cue.End:mm\\:ss\\.fff}  [{cue.Language}] {cue.Text.Replace('\n', '|')}{doubtful}");
        }

        var expected = Environment.GetEnvironmentVariable("LINGOCUE_WHISPER_MIXED_EXPECTED")?.Split(',', StringSplitOptions.TrimEntries);
        if (expected is not null)
        {
            // Score sentences (consecutive cues of one language form one spoken sentence in the test clip).
            var runs = new List<string>();
            foreach (var cue in cues)
            {
                if (runs.Count == 0 || runs[^1] != cue.Language)
                {
                    runs.Add(cue.Language ?? "?");
                }
            }

            var matches = runs.Zip(expected).Count(p => p.First == p.Second);
            log.AppendLine(inv, $"Language runs: {string.Join(",", runs)}");
            log.AppendLine(inv, $"Expected:      {string.Join(",", expected)}");
            log.AppendLine(inv, $"Matching runs: {matches}/{expected.Length}");
        }

        log.AppendLine("Engine log:");
        foreach (var line in engineLog.Lines)
        {
            log.AppendLine(line);
        }

        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "lingocue-whisper-mixed.txt"), log.ToString(), ct);

        result.Words.ShouldNotBeEmpty();
        result.Words.ShouldAllBe(w => w.Language != null);
        cues.Select(c => c.Language).Distinct().Count().ShouldBeGreaterThan(1);
    }
}

/// <summary>Keeps every log line (all levels) so the opt-in report can show the engine's decisions.</summary>
internal sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines)
        {
            Lines.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }
}
