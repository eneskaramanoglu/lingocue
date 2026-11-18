using System.Globalization;

namespace LingoCue.Core.Text;

/// <summary>
/// Culture-correct casing helpers. Turkish has dotted/dotless i (i/İ, ı/I), so invariant or
/// current-culture casing silently corrupts text. Always go through these helpers for tr content.
/// </summary>
public static class TurkishText
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("tr-TR");

    public static string ToUpper(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.ToUpper(Culture);
    }

    public static string ToLower(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.ToLower(Culture);
    }
}
