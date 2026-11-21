namespace LingoCue.Transcription.Whisper;

/// <summary>
/// Packs the speech regions found by VAD into one shorter recording (long silences and music-only
/// stretches removed, a short pause left between regions) and maps times in that recording back to
/// the original. Whisper then never decodes long non-speech stretches, where it tends to invent text
/// or lose the sentence that follows.
/// </summary>
internal sealed class SpeechTimeline
{
    /// <param name="SourceStart">Where the piece starts in the original audio.</param>
    /// <param name="PackedStart">Where it starts in the packed audio.</param>
    internal readonly record struct Piece(TimeSpan SourceStart, TimeSpan SourceEnd, TimeSpan PackedStart)
    {
        public TimeSpan Length => SourceEnd - SourceStart;

        public TimeSpan PackedEnd => PackedStart + Length;
    }

    private SpeechTimeline(IReadOnlyList<Piece> pieces, TimeSpan joinGap)
    {
        Pieces = pieces;
        JoinGap = joinGap;
        PackedDuration = pieces.Count == 0 ? TimeSpan.Zero : pieces[^1].PackedEnd;
    }

    public IReadOnlyList<Piece> Pieces { get; }

    /// <summary>Silence placed between two pieces in the packed audio.</summary>
    public TimeSpan JoinGap { get; }

    public TimeSpan PackedDuration { get; }

    /// <param name="padding">Kept around each region so word onsets and endings are not clipped.</param>
    /// <param name="mergeGap">Regions closer than this stay together with their real pause.</param>
    /// <param name="joinGap">Pause left between regions that were further apart.</param>
    public static SpeechTimeline Build(IReadOnlyList<SpeechRegion> regions, TimeSpan total, TimeSpan padding, TimeSpan mergeGap, TimeSpan joinGap)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var spans = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var region in regions.OrderBy(r => r.Start))
        {
            var start = Clamp(region.Start - padding, total);
            var end = Clamp(region.End + padding, total);
            if (end <= start)
            {
                continue;
            }

            if (spans.Count > 0 && start - spans[^1].End < mergeGap)
            {
                spans[^1] = (spans[^1].Start, Max(spans[^1].End, end));
            }
            else
            {
                spans.Add((start, end));
            }
        }

        var pieces = new List<Piece>(spans.Count);
        var packed = TimeSpan.Zero;
        foreach (var (start, end) in spans)
        {
            pieces.Add(new Piece(start, end, packed));
            packed += (end - start) + joinGap;
        }

        return new SpeechTimeline(pieces, joinGap);
    }

    /// <summary>The original time of a moment in the packed audio; inside a join gap, the end of the piece before it.</summary>
    public TimeSpan ToSource(TimeSpan packed)
    {
        if (Pieces.Count == 0)
        {
            return packed;
        }

        var piece = Pieces[0];
        foreach (var candidate in Pieces)
        {
            if (candidate.PackedStart > packed)
            {
                break;
            }

            piece = candidate;
        }

        var offset = packed - piece.PackedStart;
        return piece.SourceStart + (offset < TimeSpan.Zero ? TimeSpan.Zero : offset > piece.Length ? piece.Length : offset);
    }

    /// <summary>The packed samples: each piece copied in order with <see cref="JoinGap"/> of silence after it.</summary>
    public float[] Pack(ReadOnlySpan<float> samples, int sampleRate)
    {
        var result = new float[ToSamples(PackedDuration, sampleRate)];
        foreach (var piece in Pieces)
        {
            var from = Math.Min(samples.Length, ToSamples(piece.SourceStart, sampleRate));
            var to = Math.Min(samples.Length, ToSamples(piece.SourceEnd, sampleRate));
            var at = ToSamples(piece.PackedStart, sampleRate);
            var count = Math.Min(to - from, result.Length - at);
            if (count > 0)
            {
                samples.Slice(from, count).CopyTo(result.AsSpan(at, count));
            }
        }

        return result;
    }

    private static int ToSamples(TimeSpan t, int sampleRate) => (int)Math.Round(t.TotalSeconds * sampleRate);

    private static TimeSpan Clamp(TimeSpan t, TimeSpan total) => t < TimeSpan.Zero ? TimeSpan.Zero : t > total ? total : t;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>Reads the 16 kHz mono 16-bit WAV that FFmpeg extracts for Whisper.</summary>
internal static class WavSamples
{
    public static (float[] Samples, int SampleRate) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) is false || bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8) is false)
        {
            throw new InvalidDataException("Not a WAV file.");
        }

        var sampleRate = 16000;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var id = bytes.AsSpan(position, 4);
            var size = BitConverter.ToInt32(bytes, position + 4);
            var body = position + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                var channels = BitConverter.ToInt16(bytes, body + 2);
                sampleRate = BitConverter.ToInt32(bytes, body + 4);
                var bits = BitConverter.ToInt16(bytes, body + 14);
                if (channels != 1 || bits != 16)
                {
                    throw new InvalidDataException($"Expected 16-bit mono WAV, got {bits}-bit with {channels} channels.");
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                // FFmpeg writing to a pipe or an unfinished file may leave the size at 0 or too large.
                var length = size <= 0 || body + size > bytes.Length ? bytes.Length - body : size;
                var samples = new float[length / 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, body + (2 * i)) / 32768f;
                }

                return (samples, sampleRate);
            }

            position = body + size + (size & 1);
        }

        throw new InvalidDataException("The WAV file has no data chunk.");
    }
}
