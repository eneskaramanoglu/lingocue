using LingoCue.Core.Segmentation;
using LingoCue.Infrastructure.Platform;
using LingoCue.Media.Audio;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;
using LingoCue.Transcription.Models;
using LingoCue.Transcription.Whisper;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Transcription.Tests;

/// <summary>
/// Runs real whisper.cpp on a Turkish recording. Opt-in because it downloads a ~1 GB model:
/// set LINGOCUE_WHISPER_TEST_AUDIO to a Turkish audio/video file (optionally LINGOCUE_WHISPER_TEST_MODEL).
/// The expected words match the sample sentence used during development ("Merhaba, bugün İstanbul'dayız…").
/// </summary>
public class WhisperNetEngineIntegrationTests
{
    [Fact]
    public async Task Transcribes_turkish_speech_with_word_timestamps()
    {
        var audio = Environment.GetEnvironmentVariable("LINGOCUE_WHISPER_TEST_AUDIO");
        if (string.IsNullOrWhiteSpace(audio) || !File.Exists(audio))
        {
            Assert.Skip("Set LINGOCUE_WHISPER_TEST_AUDIO to run the real Whisper test.");
        }

        var ct = TestContext.Current.CancellationToken;
        var platform = new PlatformInfo();
        var locator = new MediaToolLocator(platform, new MediaToolOptions(), NullLogger<MediaToolLocator>.Instance);
        var runner = new ProcessRunner();
        var info = await new LingoCue.Media.Probing.MediaProbe(locator, runner).ProbeAsync(audio, ct);
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(1) };
        using var engine = new WhisperNetEngine(
            new AudioExtractor(locator, runner),
            new ModelDownloader(http, NullLogger<ModelDownloader>.Instance),
            platform,
            NullLogger<WhisperNetEngine>.Instance);
        var model = Environment.GetEnvironmentVariable("LINGOCUE_WHISPER_TEST_MODEL") ?? engine.DefaultModelId;
        var stages = new List<TranscriptionStage>();

        var result = await engine.TranscribeAsync(
            new TranscriptionRequest(audio, info.Duration, "tr", model),
            new Progress<TranscriptionProgress>(p => stages.Add(p.Stage)),
            ct);

        var text = string.Join(' ', result.Words.Select(w => w.Text));
        var log = new System.Text.StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        log.AppendLine(inv, $"Device: {result.Device}; elapsed {result.Elapsed}; language {result.DetectedLanguage}").AppendLine(text);
        foreach (var cue in Segmenter.Segment(result.Words))
        {
            log.AppendLine(inv, $"{cue.Start:mm\\:ss\\.fff} -> {cue.End:mm\\:ss\\.fff}  {cue.Text.Replace('\n', '|')}");
        }

        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "lingocue-whisper-test.txt"), log.ToString(), ct);

        result.Words.Count.ShouldBeGreaterThan(15);
        text.ShouldContain("İstanbul", Case.Insensitive);
        text.ShouldContain("Görüşmek", Case.Insensitive);
        result.Words.ShouldAllBe(w => w.End >= w.Start);
        result.Words.Zip(result.Words.Skip(1)).ShouldAllBe(p => p.Second.Start >= p.First.Start);
        result.Words[^1].End.ShouldBeLessThanOrEqualTo(info.Duration + TimeSpan.FromSeconds(1));
        result.DetectedLanguage.ShouldBe("tr");
    }
}
