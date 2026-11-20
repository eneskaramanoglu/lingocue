using System.Buffers.Binary;
using LingoCue.Media.Audio;
using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Tests.Rendering;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Tests.Audio;

public sealed class WaveformTests
{
    [Fact]
    public void Peaks_follow_the_loudest_sample_of_each_bucket()
    {
        // 8 kHz, 100 buckets/s => 80 samples per bucket; buckets: silence, full scale, quarter scale, partial.
        var samples = Enumerable.Repeat((short)0, 80)
            .Concat(Enumerable.Repeat((short)-32767, 80))
            .Concat(Enumerable.Repeat((short)8192, 80))
            .Concat(Enumerable.Repeat((short)100, 10))
            .ToArray();

        var waveform = Waveform.FromPcm16(new MemoryStream(Pcm(samples)), 8000);

        waveform.Peaks.Count.ShouldBe(4);
        waveform.Peaks[0].ShouldBe((byte)0);
        waveform.Peaks[1].ShouldBe((byte)255);
        waveform.Peaks[2].ShouldBe((byte)128); // square-root scale: sqrt(0.25) = 0.5
        waveform.Duration.ShouldBe(TimeSpan.FromMilliseconds(40));
        waveform.PeakBetween(TimeSpan.Zero, TimeSpan.FromMilliseconds(5)).ShouldBe(0);
        waveform.PeakBetween(TimeSpan.Zero, TimeSpan.FromMilliseconds(15)).ShouldBe(1);
    }

    [Fact]
    public void Reads_samples_split_across_buffer_boundaries()
    {
        var samples = Enumerable.Range(0, 100_000).Select(i => (short)(i % 2 == 0 ? 16000 : -16000)).ToArray();
        using var stream = new OddChunkStream(Pcm(samples));

        var waveform = Waveform.FromPcm16(stream, 8000);

        waveform.Peaks.Count.ShouldBe(1250);
        waveform.Peaks.ShouldAllBe(p => p == waveform.Peaks[0]);
    }

    [Fact]
    public void Save_and_load_round_trip_and_reject_bad_data()
    {
        var original = new Waveform([1, 2, 3, 250], 100);
        using var stream = new MemoryStream();
        original.Save(stream);

        stream.Position = 0;
        var loaded = Waveform.TryLoad(stream).ShouldNotBeNull();
        loaded.Peaks.ShouldBe(original.Peaks);
        loaded.BucketsPerSecond.ShouldBe(100);

        Waveform.TryLoad(new MemoryStream(stream.ToArray()[..^1])).ShouldBeNull();
        Waveform.TryLoad(new MemoryStream("not a waveform file"u8.ToArray())).ShouldBeNull();
    }

    private static byte[] Pcm(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        }

        return bytes;
    }

    /// <summary>Returns at most 777 bytes per read so samples straddle reads.</summary>
    private sealed class OddChunkStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 777));
    }
}

public sealed class WaveformServiceIntegrationTests(TestMedia media) : IClassFixture<TestMedia>, IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("lingocue-waveform-").FullName;

    public void Dispose() => Directory.Delete(_cache, recursive: true);

    private WaveformService Create(IProcessRunner? runner = null)
    {
        var locator = new FixedLocator(media.RequireTools());
        runner ??= new ProcessRunner();
        return new(locator, runner, new MediaProbe(locator, runner), _cache);
    }

    [Fact]
    public async Task Decodes_peaks_with_the_silent_second_and_caches_them()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new CountingRunner();
        var service = Create(runner);
        var reports = new List<double>();

        var waveform = (await service.GetAsync(media.ToneSilenceTone, new SyncProgress<double>(reports), ct))
            .ShouldNotBeNull();

        waveform.Duration.TotalSeconds.ShouldBe(3, tolerance: 0.02);
        waveform.PeakBetween(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.8)).ShouldBeGreaterThan(0.6);
        waveform.PeakBetween(TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(1.9)).ShouldBeLessThan(0.05);
        waveform.PeakBetween(TimeSpan.FromSeconds(2.2), TimeSpan.FromSeconds(2.8)).ShouldBeGreaterThan(0.6);
        reports[^1].ShouldBe(1);
        Directory.GetFiles(_cache).ShouldHaveSingleItem().ShouldEndWith(".lcwf"); // the temporary PCM is gone

        // The second call comes from the cache without running FFmpeg.
        var again = (await service.GetAsync(media.ToneSilenceTone, cancellationToken: ct)).ShouldNotBeNull();
        again.Peaks.ShouldBe(waveform.Peaks);
        runner.Runs.ShouldBe(2); // ffprobe + ffmpeg, both only the first time
    }

    [Fact]
    public async Task Video_without_audio_has_no_waveform()
        => (await Create().GetAsync(media.SilentVideo, cancellationToken: TestContext.Current.CancellationToken)).ShouldBeNull();

    [Fact]
    public async Task Cancellation_stops_decoding()
    {
        var service = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => service.GetAsync(media.LandscapeClip, cancellationToken: cts.Token));
        Directory.GetFiles(_cache).ShouldBeEmpty();
    }

    [Fact]
    public async Task Broken_input_raises_extraction_exception()
        => await Should.ThrowAsync<AudioExtractionException>(() => Create().GetAsync(
            media.NotMedia, cancellationToken: TestContext.Current.CancellationToken));

    [Fact]
    public void Cache_key_changes_when_the_file_changes()
    {
        var file = Path.Combine(_cache, "a.wav");
        File.WriteAllText(file, "x");
        var first = WaveformService.CacheKey(file);
        File.WriteAllText(file, "xy");

        WaveformService.CacheKey(file).ShouldNotBe(first);
    }

    private sealed class CountingRunner : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public int Runs { get; private set; }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, Action<string>? onStandardOutputLine = null,
            string? workingDirectory = null, CancellationToken cancellationToken = default)
        {
            Runs++;
            return _inner.RunAsync(fileName, arguments, onStandardOutputLine, workingDirectory, cancellationToken);
        }
    }

    private sealed class FixedLocator(MediaTools tools) : IMediaToolLocator
    {
        public MediaTools Locate() => tools;

        public bool TryLocate(out MediaTools? result)
        {
            result = tools;
            return true;
        }
    }
}
