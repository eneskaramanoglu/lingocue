using LingoCue.Media.Audio;
using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Tests.Audio;

public sealed class AudioExtractorIntegrationTests(TestMedia media) : IClassFixture<TestMedia>, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-audio-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (AudioExtractor Extractor, MediaProbe Probe) Create()
    {
        var locator = new FixedLocator(media.RequireTools());
        var runner = new ProcessRunner();
        return (new AudioExtractor(locator, runner), new MediaProbe(locator, runner));
    }

    [Theory]
    [InlineData(AudioOutputFormat.Wav16kMono, ".wav", "pcm_s16le")]
    [InlineData(AudioOutputFormat.Flac16kMono, ".flac", "flac")]
    public async Task Extracts_16k_mono_audio_with_progress(AudioOutputFormat format, string extension, string codec)
    {
        var ct = TestContext.Current.CancellationToken;
        var (extractor, probe) = Create();
        var output = Path.Combine(_dir, "out" + extension);
        var reports = new List<double>();

        await extractor.ExtractAsync(media.LandscapeClip, output, format, totalForProgress: TimeSpan.FromSeconds(3),
            progress: new SyncProgress(reports), cancellationToken: ct);

        var info = await probe.ProbeAsync(output, ct);
        var audio = info.AudioStreams.ShouldHaveSingleItem();
        audio.Codec.ShouldBe(codec);
        audio.SampleRate.ShouldBe(16000);
        audio.Channels.ShouldBe(1);
        info.Duration.TotalSeconds.ShouldBe(3, tolerance: 0.1);
        reports.ShouldNotBeEmpty();
        reports[^1].ShouldBe(1);
    }

    [Fact]
    public async Task Extracts_a_slice()
    {
        var ct = TestContext.Current.CancellationToken;
        var (extractor, probe) = Create();
        var output = Path.Combine(_dir, "slice.wav");

        await extractor.ExtractAsync(media.LandscapeClip, output, AudioOutputFormat.Wav16kMono,
            start: TimeSpan.FromSeconds(1), duration: TimeSpan.FromSeconds(1.5), cancellationToken: ct);

        (await probe.ProbeAsync(output, ct)).Duration.TotalSeconds.ShouldBe(1.5, tolerance: 0.05);
    }

    [Fact]
    public async Task Detects_the_silent_second()
    {
        var ct = TestContext.Current.CancellationToken;
        var (extractor, _) = Create();

        var silences = await extractor.DetectSilenceAsync(media.ToneSilenceTone, TimeSpan.FromSeconds(3), cancellationToken: ct);

        var silence = silences.ShouldHaveSingleItem();
        silence.Start.TotalSeconds.ShouldBe(1, tolerance: 0.05);
        silence.End.TotalSeconds.ShouldBe(2, tolerance: 0.05);
    }

    [Fact]
    public async Task Cancellation_stops_ffmpeg()
    {
        var (extractor, _) = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => extractor.ExtractAsync(
            media.LandscapeClip, Path.Combine(_dir, "cancel.wav"), AudioOutputFormat.Wav16kMono, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Failing_input_raises_extraction_exception()
        => await Should.ThrowAsync<AudioExtractionException>(() => Create().Extractor.ExtractAsync(
            media.NotMedia, Path.Combine(_dir, "bad.wav"), AudioOutputFormat.Wav16kMono,
            cancellationToken: TestContext.Current.CancellationToken));

    private sealed class FixedLocator(MediaTools tools) : IMediaToolLocator
    {
        public MediaTools Locate() => tools;

        public bool TryLocate(out MediaTools? result)
        {
            result = tools;
            return true;
        }
    }

    private sealed class SyncProgress(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }
}
