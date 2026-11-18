using System.Globalization;
using System.Text.RegularExpressions;

namespace LingoCue.Llm.Providers;

/// <summary>
/// Picks models from a provider's list by name, so no model id is hard-coded in the app. Gemini ids
/// look like <c>gemini-3.8-flash</c>, <c>gemini-3.5-flash-lite</c>, <c>gemini-3.1-pro-preview</c>.
/// </summary>
public static partial class ModelCatalog
{
    private static readonly string[] s_excludedKinds =
        ["image", "tts", "audio", "live", "transcribe", "embedding", "vision", "robotics", "omni", "computer-use", "native", "banana", "nano"];

    public static bool IsPreview(string id)
        => id.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
           id.Contains("exp", StringComparison.OrdinalIgnoreCase) ||
           id.Contains("latest", StringComparison.OrdinalIgnoreCase);

    /// <summary>Text chat models only (no image/tts/audio/embedding variants).</summary>
    public static IEnumerable<LlmModelInfo> TextModels(IEnumerable<LlmModelInfo> models)
        => models.Where(m => m.Id.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) &&
                             !s_excludedKinds.Any(k => m.Id.Contains(k, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Newest stable Flash (not Lite): the everyday default.</summary>
    public static LlmModelInfo? PickFast(IEnumerable<LlmModelInfo> models)
        => Newest(TextModels(models).Where(m => IsFamily(m.Id, "flash") && !m.Id.Contains("lite", StringComparison.OrdinalIgnoreCase) && !IsPreview(m.Id)));

    /// <summary>Newest stable Flash-Lite: the cheapest option, used for development tests.</summary>
    public static LlmModelInfo? PickLite(IEnumerable<LlmModelInfo> models)
        => Newest(TextModels(models).Where(m => m.Id.Contains("flash-lite", StringComparison.OrdinalIgnoreCase) && !IsPreview(m.Id)));

    /// <summary>
    /// Newest Pro (stable preferred, preview accepted), but only when it is not older than the newest
    /// Flash; otherwise quality mode uses that Flash with more thinking. Seen in practice: a key that
    /// lists gemini-3.8-flash but only gemini-2.5-pro.
    /// </summary>
    public static LlmModelInfo? PickQuality(IEnumerable<LlmModelInfo> models)
    {
        var list = models.ToList();
        var pro = TextModels(list).Where(m => IsFamily(m.Id, "pro") && !m.Id.Contains("latest", StringComparison.OrdinalIgnoreCase)).ToList();
        var bestPro = Newest(pro.Where(m => !IsPreview(m.Id))) ?? Newest(pro);
        var fast = PickFast(list);
        return bestPro is not null && (fast is null || Version(bestPro.Id) >= Version(fast.Id)) ? bestPro : fast;
    }

    /// <summary>The <c>3.8</c> in <c>gemini-3.8-flash</c>; 0 when absent.</summary>
    public static decimal Version(string id)
    {
        var m = VersionPattern().Match(id);
        return m.Success ? decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static bool IsFamily(string id, string family)
        => id.Split('-').Contains(family, StringComparer.OrdinalIgnoreCase);

    private static LlmModelInfo? Newest(IEnumerable<LlmModelInfo> models)
        => models.OrderByDescending(m => Version(m.Id)).ThenBy(m => m.Id.Length).FirstOrDefault();

    [GeneratedRegex(@"gemini-(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
}
