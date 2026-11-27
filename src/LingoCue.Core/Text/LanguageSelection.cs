namespace LingoCue.Core.Text;

/// <summary>
/// The spoken language(s) asked of speech recognition, stored as one string so settings, requests and
/// cached runs compare simply: null or <c>auto</c> = detect one language, <c>tr</c> = one language,
/// <c>tr+en+ko</c> = a recording that mixes these languages (each stretch is detected among them).
/// </summary>
public static class LanguageSelection
{
    public const string Auto = "auto";

    /// <summary>Most languages a mixed recording may name; more makes short-stretch detection unreliable.</summary>
    public const int MaxMixed = 3;

    private const char Separator = '+';

    /// <summary>The languages of a mixed selection (primary subtags, in order, no repeats); empty otherwise.</summary>
    public static IReadOnlyList<string> Mixed(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection) || !selection.Contains(Separator, StringComparison.Ordinal))
        {
            return [];
        }

        var languages = selection.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(LanguageCodes.Primary)
            .Where(l => l.Length > 0 && l != Auto)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxMixed)
            .ToList();
        return languages.Count > 1 ? languages : [];
    }

    public static bool IsMixed(string? selection) => Mixed(selection).Count > 1;

    /// <summary>The selection for a mixed recording; one language gives just that code, none gives null.</summary>
    public static string? ForMixed(IEnumerable<string> languages)
    {
        ArgumentNullException.ThrowIfNull(languages);
        var list = languages.Select(LanguageCodes.Primary).Where(l => l.Length > 0 && l != Auto).Distinct(StringComparer.Ordinal).Take(MaxMixed).ToList();
        return list.Count switch
        {
            0 => null,
            1 => list[0],
            _ => string.Join(Separator, list),
        };
    }

    /// <summary>The first (main) language of a selection, or null for automatic detection.</summary>
    public static string? Main(string? selection)
    {
        var mixed = Mixed(selection);
        if (mixed.Count > 0)
        {
            return mixed[0];
        }

        return string.IsNullOrWhiteSpace(selection) || selection.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase) ? null : selection.Trim();
    }
}
