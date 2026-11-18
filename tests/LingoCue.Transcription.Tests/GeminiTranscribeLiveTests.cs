using LingoCue.Core.Segmentation;
using LingoCue.Infrastructure.Platform;
using LingoCue.Infrastructure.Security;
using LingoCue.Media.Audio;
using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;
using LingoCue.Transcription.Gemini;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Transcription.Tests;

/// <summary>
/// Calls the real Gemini API (costs ~$0.005 per audio minute). Runs only when a key is available
/// (GEMINI_API_KEY or the OS secret store) and LINGOCUE_GEMINI_TEST_AUDIO points to a Turkish recording.
/// </summary>
public class GeminiTranscribeLiveTests
{
    [Fact]
    public async Task Transcribes_turkish_speech_with_word_timestamps()
    {
        var audio = Environment.GetEnvironmentVariable("LINGOCUE_GEMINI_TEST_AUDIO");
        var key = new ApiKeyProvider(SecretStoreFactory.Create()).GetGeminiKey();
        if (string.IsNullOrWhiteSpace(audio) || !File.Exists(audio) || string.IsNullOrWhiteSpace(key))
        {
            Assert.Skip("Set LINGOCUE_GEMINI_TEST_AUDIO and a Gemini key to run the live test.");
        }

        var ct = TestContext.Current.CancellationToken;
        var platform = new PlatformInfo();
        var locator = new MediaToolLocator(platform, new MediaToolOptions(), NullLogger<MediaToolLocator>.Instance);
        var runner = new ProcessRunner();
        var info = await new MediaProbe(locator, runner).ProbeAsync(audio, ct);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var engine = new GeminiTranscribeEngine(
            new AudioExtractor(locator, runner),
            new GeminiTranscribeClient(http, NullLogger<GeminiTranscribeClient>.Instance),
            () => key,
            NullLogger<GeminiTranscribeEngine>.Instance);

        var result = await engine.TranscribeAsync(new TranscriptionRequest(audio, info.Duration, "tr", engine.DefaultModelId, Diarize: true), cancellationToken: ct);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var log = new System.Text.StringBuilder();
        log.AppendLine(inv, $"elapsed {result.Elapsed}; cost ~${result.EstimatedCostUsd}");
        log.AppendLine(string.Join(' ', result.Words.Select(w => w.Text)));
        foreach (var cue in Segmenter.Segment(result.Words))
        {
            log.AppendLine(inv, $"{cue.Start:mm\\:ss\\.fff} -> {cue.End:mm\\:ss\\.fff} [{cue.Speaker}] {cue.Text.Replace('\n', '|')}");
        }

        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "lingocue-gemini-test.txt"), log.ToString(), ct);

        result.Words.Count.ShouldBeGreaterThan(10);
        string.Join(' ', result.Words.Select(w => w.Text)).ShouldContain("İstanbul", Case.Insensitive);
        result.Words.Zip(result.Words.Skip(1)).ShouldAllBe(p => p.Second.Start >= p.First.Start);
    }
}
