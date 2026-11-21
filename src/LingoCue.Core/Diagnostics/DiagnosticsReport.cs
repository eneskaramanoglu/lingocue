using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LingoCue.Core.Diagnostics;

/// <summary>
/// Builds the contents of a diagnostics package. The user creates it and decides whether to share it;
/// still, the home folder is replaced with "~" everywhere and the recent-files list is left out, so
/// names in folder paths do not travel with it. API keys never are in these files (they live in the
/// OS key store).
/// </summary>
public static class DiagnosticsReport
{
    /// <summary>Replaces the user's home folder in any text (both slash styles, any case) with "~".</summary>
    public static string Redact(string text, string homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return text;
        }

        var home = homeDirectory.TrimEnd('\\', '/');
        foreach (var variant in new[] { home, home.Replace('\\', '/'), home.Replace('/', '\\'), home.Replace("\\", "\\\\", StringComparison.Ordinal) }.Distinct())
        {
            text = text.Replace(variant, "~", StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    /// <summary>The settings file without the recent-files list (paths of the user's projects and videos).</summary>
    public static string SanitizeSettings(string settingsJson)
    {
        ArgumentNullException.ThrowIfNull(settingsJson);
        try
        {
            if (JsonNode.Parse(settingsJson) is not JsonObject root)
            {
                return "{}";
            }

            foreach (var key in root.Select(p => p.Key).Where(k => k.Equals("RecentFiles", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var count = root[key] is JsonArray list ? list.Count : 0;
                root[key] = $"({count} files left out)";
            }

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return "(settings file could not be read)";
        }
    }

    /// <summary>A readable summary: app, system, hardware and tools (one "Key: value" per line).</summary>
    public static string Summary(IEnumerable<KeyValuePair<string, string?>> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"LingoCue diagnostics — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        foreach (var (key, value) in facts)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{key}: {value ?? "—"}");
        }

        return sb.ToString();
    }
}
