using System.Globalization;

namespace LingoCue.Transcription.Whisper;

/// <summary>
/// Recognizes text Whisper is known to invent from silence, music or noise: credits and channel
/// phrases from its training subtitles, and the same sentence looping. Such segments are not
/// removed (they may be real) but get zero confidence, so their lines are flagged for review.
/// </summary>
public static class WhisperHallucinations
{
    private static readonly CultureInfo s_turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Lower-case fragments; matched inside the segment text.
    private static readonly string[] s_phrases =
    [
        "altyazı m.k", "alt yazı m.k", "altyazı: m.k", "altyazılar m.k",
        "izlediğiniz için teşekkür", "izlediğiniz için teşekkürler",
        "abone olmayı unutmayın", "abone olmayı unutma", "kanalıma abone",
        "beğenmeyi unutmayın", "bir sonraki videoda görüşmek üzere",
        "sesli betimleme", "altyazılar amara", "amara.org",
        "thanks for watching", "thank you for watching", "subtitles by", "please subscribe", "like and subscribe",
    ];

    /// <param name="previous">The segment before this one; a loop repeats the same text.</param>
    /// <param name="noSpeechProbability">Whisper's own estimate that the window held no speech.</param>
    public static bool IsSuspicious(string text, string? previous, float noSpeechProbability, float averageProbability)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (s_phrases.Any(p => normalized.Contains(p, StringComparison.Ordinal)))
        {
            return true;
        }

        if (previous is not null && Normalize(previous) == normalized && normalized.Length > 3)
        {
            return true; // the decoder got stuck repeating itself
        }

        // Whisper thought the window was silent and is unsure of the words it wrote anyway.
        return noSpeechProbability > 0.6f && averageProbability < 0.5f;
    }

    private static string Normalize(string text)
        => string.Join(' ', text.ToLower(s_turkish).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim(' ', '.', '!', '?', '…', ',', '"', '\'');
}
