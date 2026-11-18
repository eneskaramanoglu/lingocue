using System.Globalization;
using LingoCue.Core.Text;

namespace LingoCue.Core.Segmentation;

/// <summary>
/// Scores how good it is to break a cue or a line between two words. Higher is better;
/// <see cref="Forbidden"/> means "only if nothing else works". Language-specific heuristics are
/// deliberately simple (no parser): they catch the common bad breaks in subtitles.
/// </summary>
internal sealed class BreakRules
{
    public const double Forbidden = -100;

    private static readonly HashSet<string> s_turkishConjunctions = new(StringComparer.Ordinal)
    {
        "ve", "ama", "fakat", "ancak", "lakin", "çünkü", "veya", "yahut", "ya", "ki", "de", "da",
        "hem", "ne", "yani", "oysa", "halbuki", "sonra", "eğer", "madem", "gibi", "kadar", "için",
    };

    // Words that bind to the next word: determiners, quantifiers, intensifiers and the most frequent
    // attributive adjectives (Turkish adjectives precede their noun: "güzel köşeler", "büyük şehir").
    private static readonly HashSet<string> s_turkishBindNext = new(StringComparer.Ordinal)
    {
        "bir", "bu", "şu", "o", "her", "hiçbir", "birkaç", "bazı", "tüm", "bütün", "çok", "en", "daha",
        "pek", "gayet", "oldukça", "biraz", "az", "kaç", "hangi", "ne", "aynı", "başka", "diğer",
        "güzel", "büyük", "küçük", "yeni", "eski", "iyi", "kötü", "uzun", "kısa", "yüksek", "alçak", "geniş", "dar",
        "sıcak", "soğuk", "ılık", "genç", "yaşlı", "önemli", "farklı", "özel", "ilk", "son", "tek", "ana", "temel",
        "doğru", "yanlış", "zor", "kolay", "hızlı", "yavaş", "derin", "ağır", "hafif", "açık", "kapalı", "tatlı",
        "acı", "kırmızı", "mavi", "yeşil", "sarı", "beyaz", "siyah", "kara", "koca", "minik", "harika", "muhteşem",
        "yerel", "ulusal", "uluslararası", "tarihi", "kültürel", "sosyal", "ekonomik", "siyasi", "bilimsel",
    };

    private static readonly HashSet<string> s_englishConjunctions = new(StringComparer.Ordinal)
    {
        "and", "but", "or", "because", "so", "that", "which", "who", "when", "while", "if", "although", "then",
    };

    private static readonly HashSet<string> s_englishBindNext = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "this", "that", "these", "those", "my", "your", "his", "her", "its", "our", "their",
        "to", "of", "in", "on", "at", "for", "with", "very", "more", "most",
    };

    private readonly bool _turkish;
    private readonly CultureInfo _culture;

    public BreakRules(string language)
    {
        _turkish = language.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        _culture = _turkish ? TurkishText.Culture : CultureInfo.InvariantCulture;
    }

    public static bool EndsSentence(string word)
    {
        var w = word.TrimEnd('"', '\'', '”', '’', ')', '»');
        return w.EndsWith('.') || w.EndsWith('?') || w.EndsWith('!') || w.EndsWith('…');
    }

    /// <summary>Score for breaking between <paramref name="left"/> and <paramref name="right"/>.</summary>
    public double Score(string left, string right)
    {
        if (EndsSentence(left))
        {
            return 10;
        }

        var l = Bare(left);
        var r = Bare(right);

        if (left.EndsWith(',') || left.EndsWith(';') || left.EndsWith(':') || left.EndsWith('—') || left.EndsWith('-'))
        {
            return 6;
        }

        if (BindsToNext(l, left))
        {
            return Forbidden;
        }

        if (IsConjunction(r))
        {
            return 4;
        }

        return 0;
    }

    private bool BindsToNext(string bare, string raw)
    {
        // Numbers bind to their unit/noun: "3 kişi", "10 dakika".
        if (bare.Length > 0 && bare.All(char.IsDigit))
        {
            return true;
        }

        if (_turkish)
        {
            return s_turkishBindNext.Contains(bare) || IsTurkishGenitive(bare, raw);
        }

        return s_englishBindNext.Contains(bare);
    }

    private bool IsConjunction(string bare)
        => _turkish ? s_turkishConjunctions.Contains(bare) : s_englishConjunctions.Contains(bare);

    private static readonly string[] s_genitiveEndings = ["ın", "in", "un", "ün"];

    // Genitive (-(n)ın/-(n)in/-(n)un/-(n)ün, after an apostrophe for names) starts a noun compound
    // with the next word: "İstanbul'un sokakları", "evimizin bahçesi". Heuristic: a word of 5+ letters
    // with that ending. It also catches some adjectives ending the same way ("yakın", "kalın"), which
    // bind to the next word too, so the false positives are harmless.
    private static bool IsTurkishGenitive(string bare, string raw)
    {
        var apostrophe = raw.LastIndexOfAny(['\'', '’']);
        if (apostrophe >= 0)
        {
            var suffix = bare[(bare.LastIndexOfAny(['\'', '’']) + 1)..];
            return suffix is "ın" or "in" or "un" or "ün" or "nın" or "nin" or "nun" or "nün";
        }

        if (bare is "benim" or "senin" or "bizim" or "sizin" or "onun")
        {
            return true;
        }

        return bare.Length >= 5 && bare != "için" && s_genitiveEndings.Any(e => bare.EndsWith(e, StringComparison.Ordinal));
    }

    private string Bare(string word)
        => word.Trim('.', ',', ';', ':', '!', '?', '…', '"', '“', '”', '(', ')', '«', '»', '-', '—').ToLower(_culture);
}
