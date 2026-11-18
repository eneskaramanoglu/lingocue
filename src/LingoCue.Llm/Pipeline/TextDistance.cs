using LingoCue.Core.Text;

namespace LingoCue.Llm.Pipeline;

internal static class TextDistance
{
    /// <summary>Levenshtein distance divided by the longer length (0 = identical, 1 = completely different), Turkish case-insensitive.</summary>
    public static double ChangeRatio(string a, string b)
    {
        a = TurkishText.ToLower(a.Trim());
        b = TurkishText.ToLower(b.Trim());
        if (a == b)
        {
            return 0;
        }

        var longer = Math.Max(a.Length, b.Length);
        return longer == 0 ? 0 : (double)Levenshtein(a, b) / longer;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
