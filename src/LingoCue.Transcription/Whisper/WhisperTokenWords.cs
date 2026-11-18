using LingoCue.Core.Subtitles;

namespace LingoCue.Transcription.Whisper;

/// <summary>
/// A whisper.cpp token: times in centiseconds (whisper's t0/t1 unit). <paramref name="DtwCs"/> is the
/// DTW-aligned onset of the token (or -1 when DTW is off).
/// </summary>
internal readonly record struct RawToken(string Text, long StartCs, long EndCs, float Probability, long DtwCs = -1);

/// <summary>Speech region from the VAD.</summary>
internal readonly record struct SpeechRegion(TimeSpan Start, TimeSpan End);

internal static class WhisperTokenWords
{
    /// <summary>
    /// Merges sub-word tokens into words. A token starting with a space begins a new word; special
    /// tokens (<c>[_BEG_]</c>, <c>&lt;|en|&gt;</c>, timestamps) are skipped. Word confidence is the
    /// lowest token probability, which is what flags a doubtful word.
    /// </summary>
    /// <remarks>
    /// With DTW, a word starts at the aligned onset of its first token and ends where the next word
    /// starts; a word followed by more than <see cref="MaxDtwGapCs"/> of silence ends
    /// <see cref="DtwTailCs"/> after its last token onset instead of stretching over the silence.
    /// Without DTW, whisper's token t0/t1 are used (they collapse to segment edges in fast speech).
    /// </remarks>
    public static List<Word> ToWords(IEnumerable<RawToken> tokens, TimeSpan offset = default)
    {
        var drafts = new List<Draft>();
        Draft? current = null;

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token.Text) || IsSpecial(token.Text))
            {
                continue;
            }

            if (current is null || token.Text[0] == ' ')
            {
                current = new Draft
                {
                    Text = token.Text.TrimStart(),
                    Start = token.DtwCs >= 0 ? token.DtwCs : token.StartCs,
                    LastOnset = token.DtwCs,
                    End = token.EndCs,
                    Probability = token.Probability,
                    UsesDtw = token.DtwCs >= 0,
                };
                drafts.Add(current);
                continue;
            }

            current.Text += token.Text;
            current.Probability = Math.Min(current.Probability, token.Probability);
            current.End = Math.Max(current.End, token.EndCs);
            if (token.DtwCs >= 0)
            {
                current.LastOnset = Math.Max(current.LastOnset, token.DtwCs);
            }
        }

        var words = new List<Word>(drafts.Count);
        for (var i = 0; i < drafts.Count; i++)
        {
            var d = drafts[i];
            if (d.Text.Trim().Length == 0)
            {
                continue;
            }

            long end;
            if (d.UsesDtw)
            {
                var tail = Math.Max(d.LastOnset, d.Start) + DtwTailCs;
                end = i + 1 < drafts.Count && drafts[i + 1].Start - d.Start <= MaxDtwGapCs
                    ? Math.Max(drafts[i + 1].Start, d.Start + 1)
                    : tail;
            }
            else
            {
                end = Math.Max(d.Start, d.End);
            }

            words.Add(new Word(
                d.Text.Trim(),
                offset + TimeSpan.FromMilliseconds(d.Start * 10),
                offset + TimeSpan.FromMilliseconds(end * 10),
                Math.Round(d.Probability, 3)));
        }

        return words;
    }

    private const long MaxDtwGapCs = 100;
    private const long DtwTailCs = 40;

    private sealed class Draft
    {
        public required string Text { get; set; }

        public long Start { get; init; }

        public long LastOnset { get; set; }

        public long End { get; set; }

        public float Probability { get; set; }

        public bool UsesDtw { get; init; }
    }

    /// <summary>
    /// Keeps segments that overlap detected speech. Whisper's best-known failure is inventing text
    /// in silence or music ("Altyazı M.K.", "İzlediğiniz için teşekkürler"); VAD regions catch it.
    /// </summary>
    public static bool OverlapsSpeech(TimeSpan start, TimeSpan end, IReadOnlyList<SpeechRegion> regions, TimeSpan padding)
    {
        if (regions.Count == 0)
        {
            return true; // no VAD information: keep everything
        }

        var duration = end - start;
        if (duration <= TimeSpan.Zero)
        {
            return regions.Any(r => start >= r.Start - padding && start <= r.End + padding);
        }

        var covered = TimeSpan.Zero;
        foreach (var region in regions)
        {
            var s = Max(start, region.Start - padding);
            var e = Min(end, region.End + padding);
            if (e > s)
            {
                covered += e - s;
            }
        }

        return covered.TotalMilliseconds / duration.TotalMilliseconds >= 0.3;
    }

    private static bool IsSpecial(string text)
    {
        var t = text.Trim();
        return (t.StartsWith("[_", StringComparison.Ordinal) && t.EndsWith(']')) ||
               (t.StartsWith("<|", StringComparison.Ordinal) && t.EndsWith("|>", StringComparison.Ordinal));
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
