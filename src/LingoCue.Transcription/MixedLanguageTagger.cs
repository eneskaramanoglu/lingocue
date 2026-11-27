using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;

namespace LingoCue.Transcription;

/// <summary>
/// Tags words with their language for engines that transcribe a mixed-language recording but do not
/// say which language each word is in. Words are read a sentence at a time (a sentence ends at
/// end punctuation or a pause); a sentence without evidence keeps the language before it.
/// </summary>
public static class MixedLanguageTagger
{
    private static readonly TimeSpan s_sentencePause = TimeSpan.FromMilliseconds(700);

    public static List<Word> Tag(IReadOnlyList<Word> words, IReadOnlyList<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return [.. words];
        }

        var result = new List<Word>(words.Count);
        var sentences = Sentences(words);
        var guesses = sentences.Select(s => TextLanguageGuess.Guess(string.Join(' ', s.Select(w => w.Text)), candidates)).ToList();
        var first = guesses.FirstOrDefault(g => g is not null) ?? LanguageCodes.Primary(candidates[0]);
        var current = first;
        for (var i = 0; i < sentences.Count; i++)
        {
            current = guesses[i] ?? current;
            result.AddRange(sentences[i].Select(w => w with { Language = current }));
        }

        return result;
    }

    private static List<List<Word>> Sentences(IReadOnlyList<Word> words)
    {
        var sentences = new List<List<Word>>();
        var current = new List<Word>();
        foreach (var word in words)
        {
            if (current.Count > 0 && word.Start - current[^1].End > s_sentencePause)
            {
                sentences.Add(current);
                current = [];
            }

            current.Add(word);
            if (word.Text.TrimEnd('"', '\'', ')', '»', '”').EndsWith('.') || word.Text.EndsWith('?') || word.Text.EndsWith('!'))
            {
                sentences.Add(current);
                current = [];
            }
        }

        if (current.Count > 0)
        {
            sentences.Add(current);
        }

        return sentences;
    }
}
