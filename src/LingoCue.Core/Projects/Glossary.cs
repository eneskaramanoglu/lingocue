using System.Globalization;

namespace LingoCue.Core.Projects;

public enum GlossaryKind
{
    ProperNoun,
    Term,
    Other,
}

/// <summary>
/// A project glossary entry. For correction, <see cref="Preferred"/> is the canonical spelling of
/// <see cref="Term"/> in the source language; for translation, it is the agreed translation.
/// Locked entries are user decisions the LLM must follow.
/// </summary>
public sealed record GlossaryEntry
{
    public required string Term { get; init; }

    /// <summary>Canonical spelling or agreed translation; null means "keep as is".</summary>
    public string? Preferred { get; init; }

    /// <summary>Language of <see cref="Preferred"/> when it is a translation (e.g. "en"); null for correction entries.</summary>
    public string? TargetLanguage { get; init; }

    public GlossaryKind Kind { get; init; } = GlossaryKind.Term;

    public string? Note { get; init; }

    public bool Locked { get; init; }

    /// <summary>Suggested by the LLM context pass and not yet confirmed by the user.</summary>
    public bool Suggested { get; init; }

    public string Effective => string.IsNullOrWhiteSpace(Preferred) ? Term : Preferred;
}

/// <summary>What the context pass learned about the video, editable by the user.</summary>
public sealed record ProjectContext
{
    public string? Summary { get; init; }

    public string? Tone { get; init; }

    /// <summary>Form of address used toward the audience: "sen", "siz", or null when unknown.</summary>
    public string? Address { get; init; }

    public IReadOnlyList<string> Speakers { get; init; } = [];

    public DateTimeOffset? CreatedAt { get; init; }
}

public static class GlossaryExtensions
{
    /// <summary>Entries relevant for a language pass (correction: no target language; translation: matching target).</summary>
    public static IEnumerable<GlossaryEntry> For(this IEnumerable<GlossaryEntry> glossary, string? targetLanguage)
        => glossary.Where(e => targetLanguage is null
            ? e.TargetLanguage is null
            : string.Equals(e.TargetLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase));

    /// <summary>Merges LLM suggestions without overwriting the user's entries (case-insensitive, culture-aware match on Term).</summary>
    public static List<GlossaryEntry> MergeSuggestions(this IEnumerable<GlossaryEntry> existing, IEnumerable<GlossaryEntry> suggestions, CultureInfo culture)
    {
        var result = existing.ToList();
        foreach (var suggestion in suggestions)
        {
            var exists = result.Any(e =>
                string.Compare(e.Term, suggestion.Term, culture, CompareOptions.IgnoreCase) == 0 &&
                string.Equals(e.TargetLanguage, suggestion.TargetLanguage, StringComparison.OrdinalIgnoreCase));
            if (!exists && !string.IsNullOrWhiteSpace(suggestion.Term))
            {
                result.Add(suggestion with { Suggested = true, Locked = false });
            }
        }

        return result;
    }
}
