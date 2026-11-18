using LingoCue.Core.Subtitles;

namespace LingoCue.Transcription.Whisper;

/// <summary>A whisper.cpp token: times in centiseconds (whisper's t0/t1 unit).</summary>
internal readonly record struct RawToken(string Text, long StartCs, long EndCs, float Probability);

/// <summary>Speech region from the VAD.</summary>
internal readonly record struct SpeechRegion(TimeSpan Start, TimeSpan End);

internal static class WhisperTokenWords
{
    /// <summary>
    /// Merges sub-word tokens into words. A token starting with a space begins a new word; special
    /// tokens (<c>[_BEG_]</c>, <c>&lt;|en|&gt;</c>, timestamps) are skipped. Word confidence is the
    /// lowest token probability, which is what flags a doubtful word.
    /// </summary>
    public static List<Word> ToWords(IEnumerable<RawToken> tokens, TimeSpan offset = default)
    {
        var words = new List<Word>();
        string? text = null;
        long start = 0, end = 0;
        var probability = 1f;

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token.Text) || IsSpecial(token.Text))
            {
                continue;
            }

            var startsWord = token.Text[0] == ' ' || text is null;
            if (startsWord && text is not null)
            {
                Flush();
            }

            if (text is null)
            {
                text = token.Text.TrimStart();
                start = token.StartCs;
                probability = token.Probability;
            }
            else
            {
                text += token.Text;
                probability = Math.Min(probability, token.Probability);
            }

            end = Math.Max(end, token.EndCs);
        }

        Flush();
        return words;

        void Flush()
        {
            if (text is null)
            {
                return;
            }

            if (text.Trim().Length > 0)
            {
                words.Add(new Word(
                    text.Trim(),
                    offset + TimeSpan.FromMilliseconds(start * 10),
                    offset + TimeSpan.FromMilliseconds(Math.Max(start, end) * 10),
                    Math.Round(probability, 3)));
            }

            text = null;
            end = 0;
        }
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
