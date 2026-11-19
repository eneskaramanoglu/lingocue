using System.Globalization;

namespace LingoCue.Core.Text;

public static class LanguageCodes
{
    // ISO 639-2/B, the form MP4 and Matroska players expect (e.g. "ger", not "deu").
    private static readonly Dictionary<string, string> s_bibliographic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tr"] = "tur", ["en"] = "eng", ["de"] = "ger", ["fr"] = "fre", ["es"] = "spa", ["it"] = "ita",
        ["pt"] = "por", ["ru"] = "rus", ["ar"] = "ara", ["fa"] = "per", ["nl"] = "dut", ["zh"] = "chi",
        ["ja"] = "jpn", ["ko"] = "kor", ["el"] = "gre", ["az"] = "aze", ["ku"] = "kur", ["uk"] = "ukr",
    };

    /// <summary>
    /// BCP 47 tag (<c>tr</c>, <c>en-US</c>) → ISO 639-2 code for container metadata; <c>und</c> when unknown.
    /// </summary>
    public static string ToIso6392(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "und";
        }

        var primary = language.Split('-', '_')[0];
        if (s_bibliographic.TryGetValue(primary, out var code))
        {
            return code;
        }

        try
        {
            var three = CultureInfo.GetCultureInfo(primary).ThreeLetterISOLanguageName;
            return three.Length == 3 && three != "ivl" ? three : "und";
        }
        catch (CultureNotFoundException)
        {
            return "und";
        }
    }

    /// <summary>Human name for a track title in the viewer's player (Turkish UI names for common languages).</summary>
    public static string DisplayName(string? language) => language?.Split('-', '_')[0].ToLowerInvariant() switch
    {
        "tr" => "Türkçe",
        "en" => "English",
        "de" => "Deutsch",
        "fr" => "Français",
        "es" => "Español",
        "ar" => "العربية",
        "ru" => "Русский",
        null or "" => "Altyazı",
        var other => other,
    };
}
