using System.Globalization;

namespace LingoCue.Core.Text;

/// <summary>
/// Picks which of a few known languages a short subtitle line is written in, from its script (Hangul,
/// kana, Cyrillic, Arabic) and, for Latin text, from letters and common words. Used to tag lines from
/// engines that do not report a language per word, and to catch a stretch Whisper wrote in the wrong
/// language. It only chooses among the candidates; it is not a general language identifier.
/// </summary>
public static class TextLanguageGuess
{
    private static readonly Dictionary<string, HashSet<string>> s_commonWords = new(StringComparer.Ordinal)
    {
        ["tr"] = Words("ve bir bu şu da de ki için ile çok ne mi mı mu mü ama gibi daha var yok ben sen biz siz onlar olarak " +
                       "değil evet hayır şey nasıl neden çünkü sonra şimdi burada orada kadar her hiç bana sana onu bunu"),
        ["en"] = Words("the and is are was were you to of in it that this what for with have has not but they we he she " +
                       "be do does did can will would there here so just about yes no my your our their from at on"),
        ["de"] = Words("der die das und ist nicht ich du wir sie es ein eine zu mit auf für von den dem ja nein aber auch"),
        ["fr"] = Words("le la les et est un une des pas je tu nous vous il elle ce que qui pour avec dans sur oui non mais"),
        ["es"] = Words("el la los las y es un una que de no yo tú nosotros para con por en sí pero muy como está"),
        ["it"] = Words("il lo la gli le e è un una che di non io tu noi per con in sì ma molto come sono"),
        ["pt"] = Words("o a os as e é um uma que de não eu você nós para com em sim mas muito como está"),
        ["nl"] = Words("de het een en is niet ik je wij zij van op voor met ja nee maar ook"),
    };

    /// <summary>
    /// The candidate the text is most likely written in; null when the text gives no evidence for any
    /// of them (numbers, names, a single unknown word).
    /// </summary>
    public static string? Guess(string? text, IReadOnlyList<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (string.IsNullOrWhiteSpace(text) || candidates.Count == 0)
        {
            return null;
        }

        var codes = candidates.Select(LanguageCodes.Primary).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (codes.Count == 1)
        {
            return codes[0];
        }

        var scripts = CountScripts(text);
        if (scripts.Letters == 0)
        {
            return null;
        }

        // A non-Latin script settles it when exactly one candidate uses it.
        foreach (var (code, count) in new[] { ("ko", scripts.Hangul), ("ja", scripts.Kana), ("ru", scripts.Cyrillic), ("uk", scripts.Cyrillic), ("ar", scripts.Arabic), ("zh", scripts.Han) })
        {
            if (count * 3 >= scripts.Letters && codes.Contains(code))
            {
                return code;
            }
        }

        if (scripts.Latin * 2 < scripts.Letters)
        {
            return null;
        }

        var latin = codes.Where(c => c is not ("ko" or "ja" or "zh" or "ru" or "uk" or "ar")).ToList();
        if (latin.Count == 1)
        {
            return latin[0];
        }

        var lower = text.ToLower(CultureInfo.InvariantCulture);
        string? best = null;
        var bestScore = 0.0;
        foreach (var code in latin)
        {
            var score = Score(lower, text, code);
            if (score > bestScore)
            {
                best = code;
                bestScore = score;
            }
            else if (score == bestScore && score > 0)
            {
                best = null; // a tie is no evidence
            }
        }

        return best;
    }

    private static double Score(string lower, string original, string code)
    {
        var score = 0.0;
        foreach (var token in Tokens(lower))
        {
            if (s_commonWords.TryGetValue(code, out var common) && common.Contains(token))
            {
                score += 1;
            }
        }

        foreach (var c in original)
        {
            score += code switch
            {
                "tr" when c is 'ğ' or 'Ğ' or 'ş' or 'Ş' or 'ı' or 'İ' => 1.5,
                "tr" when c is 'ç' or 'Ç' or 'ö' or 'Ö' or 'ü' or 'Ü' => 0.5,
                "de" when c is 'ß' or 'ä' or 'Ä' or 'ö' or 'Ö' or 'ü' or 'Ü' => 0.5,
                "fr" when c is 'é' or 'è' or 'ê' or 'à' or 'ç' or 'œ' => 0.5,
                "es" when c is 'ñ' or 'á' or 'í' or 'ó' or 'ú' or '¿' or '¡' => 0.5,
                "pt" when c is 'ã' or 'õ' or 'á' or 'é' or 'ç' => 0.5,
                _ => 0,
            };
        }

        // An apostrophe suffix ("İstanbul'da", "Boğaz'ın") is Turkish; "don't", "it's" are English.
        if (code == "tr" && original.Contains('\'', StringComparison.Ordinal) && TurkishSuffix(lower))
        {
            score += 1;
        }

        return score;
    }

    private static bool TurkishSuffix(string lower)
    {
        foreach (var token in lower.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = token.IndexOf('\'', StringComparison.Ordinal);
            if (at > 0 && at < token.Length - 1)
            {
                var suffix = token[(at + 1)..].TrimEnd('.', ',', '!', '?');
                if (suffix is not ("t" or "s" or "re" or "ll" or "ve" or "d" or "m") && suffix.Length <= 5)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> Tokens(string lower)
    {
        var start = -1;
        for (var i = 0; i <= lower.Length; i++)
        {
            var letter = i < lower.Length && char.IsLetter(lower[i]);
            if (letter && start < 0)
            {
                start = i;
            }
            else if (!letter && start >= 0)
            {
                yield return lower[start..i];
                start = -1;
            }
        }
    }

    private static (int Letters, int Latin, int Hangul, int Kana, int Han, int Cyrillic, int Arabic) CountScripts(string text)
    {
        int letters = 0, latin = 0, hangul = 0, kana = 0, han = 0, cyrillic = 0, arabic = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            letters++;
            switch (c)
            {
                case >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏':
                    hangul++;
                    break;
                case >= '぀' and <= 'ヿ':
                    kana++;
                    break;
                case >= '一' and <= '鿿':
                    han++;
                    break;
                case >= 'Ѐ' and <= 'ӿ':
                    cyrillic++;
                    break;
                case >= '؀' and <= 'ۿ':
                    arabic++;
                    break;
                case <= 'ɏ':
                    latin++;
                    break;
            }
        }

        return (letters, latin, hangul, kana, han, cyrillic, arabic);
    }

    private static HashSet<string> Words(string list) => new(list.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
}
