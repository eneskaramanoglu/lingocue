using LingoCue.Media.Audio;

namespace LingoCue.Media.Tests.Audio;

public class AudioChunkPlannerTests
{
    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    private static SilenceInterval Silence(double startMin, double seconds)
        => new(Min(startMin), Min(startMin) + TimeSpan.FromSeconds(seconds));

    [Fact]
    public void Short_audio_is_one_chunk()
        => AudioChunkPlanner.Plan(Min(10), Min(25), []).ShouldBe([new AudioChunk(0, TimeSpan.Zero, Min(10))]);

    [Fact]
    public void Cuts_in_the_middle_of_the_latest_silence_before_the_limit()
    {
        var silences = new[] { Silence(10, 2), Silence(20, 2), Silence(24, 2), Silence(30, 2) };

        var chunks = AudioChunkPlanner.Plan(Min(40), Min(25), silences);

        chunks.Count.ShouldBe(2);
        chunks[0].End.ShouldBe(Min(24) + TimeSpan.FromSeconds(1));
        chunks[1].Start.ShouldBe(chunks[0].End);
        chunks[1].End.ShouldBe(Min(40));
        chunks.ShouldAllBe(c => c.Duration <= Min(25));
    }

    [Fact]
    public void Hard_cut_when_no_silence_in_the_search_window()
    {
        var chunks = AudioChunkPlanner.Plan(Min(60), Min(25), [Silence(5, 2)]);

        chunks.Select(c => c.End).ShouldBe([Min(25), Min(50), Min(60)]);
        chunks.Select(c => c.Index).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void Parses_silencedetect_output_including_trailing_silence()
    {
        const string stderr = """
            [silencedetect @ 0x1] silence_start: 1.0012
            [silencedetect @ 0x1] silence_end: 2.0005 | silence_duration: 0.9993
            [silencedetect @ 0x1] silence_start: 9.5
            """;

        var silences = AudioChunkPlanner.ParseSilenceDetect(stderr, TimeSpan.FromSeconds(10));

        silences.Count.ShouldBe(2);
        silences[0].Start.TotalSeconds.ShouldBe(1.0012, tolerance: 0.0001);
        silences[0].End.TotalSeconds.ShouldBe(2.0005, tolerance: 0.0001);
        silences[1].End.ShouldBe(TimeSpan.FromSeconds(10));
    }
}

public class FfmpegProgressTests
{
    [Fact]
    public void Reports_fraction_from_out_time_and_one_at_end()
    {
        var values = new List<double>();
        var progress = new FfmpegProgress(TimeSpan.FromSeconds(10), new SyncProgress(values));

        progress.OnLine("frame=1");
        progress.OnLine("out_time_us=2500000");
        progress.OnLine("out_time_ms=5000000"); // FFmpeg reports microseconds here too
        progress.OnLine("out_time_us=N/A");
        progress.OnLine("progress=end");

        values.ShouldBe([0.25, 0.5, 1.0]);
    }

    [Fact]
    public void Clamps_overshoot()
    {
        var values = new List<double>();
        new FfmpegProgress(TimeSpan.FromSeconds(1), new SyncProgress(values)).OnLine("out_time_us=3000000");

        values.ShouldBe([1.0]);
    }

    // Progress<T> posts to a sync context; tests need immediate, ordered callbacks.
    private sealed class SyncProgress(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }
}
