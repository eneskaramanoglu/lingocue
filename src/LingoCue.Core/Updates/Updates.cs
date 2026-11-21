using System.Text.Json;

namespace LingoCue.Core.Updates;

/// <summary>A newer release than the running one.</summary>
/// <param name="ReleasePage">The GitHub release page (download links and notes).</param>
/// <param name="CanInstallInPlace">
/// True for a Windows copy installed with Setup.exe: the update downloads and applies on restart.
/// Otherwise (macOS, the portable zip, development builds) the user downloads it from the page.
/// </param>
public sealed record AvailableUpdate(string Version, Uri ReleasePage, string? Notes, bool CanInstallInPlace);

/// <summary>Checks GitHub Releases for a newer version and, where possible, installs it.</summary>
public interface IUpdateService
{
    string CurrentVersion { get; }

    /// <summary>The running copy was installed with Setup.exe and can update itself.</summary>
    bool CanInstallInPlace { get; }

    /// <summary>An update downloaded earlier and waiting for a restart, if any.</summary>
    AvailableUpdate? PendingRestart { get; }

    /// <returns>The newer release, or null when this is the latest.</returns>
    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads an in-place update (<see cref="AvailableUpdate.CanInstallInPlace"/>); progress is 0–100.</summary>
    Task DownloadAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Closes the app, applies the downloaded update and starts the new version.</summary>
    void RestartToApply(AvailableUpdate update);
}

public static class ReleaseVersions
{
    public const string Repository = "eneskaramanoglu/lingocue";

    public static Uri RepositoryUrl { get; } = new($"https://github.com/{Repository}");

    public static Uri LatestReleaseApi { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static Uri ReleasePage(string version) => new($"https://github.com/{Repository}/releases/tag/v{version.TrimStart('v')}");

    /// <summary>
    /// True when <paramref name="candidate"/> (a tag such as <c>v1.2.0</c>) is newer than
    /// <paramref name="current"/>. Pre-release suffixes ("-beta.1") sort below the release.
    /// </summary>
    public static bool IsNewer(string candidate, string current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);
        if (!TryParse(candidate, out var c, out var cPre) || !TryParse(current, out var v, out var vPre))
        {
            return false;
        }

        var compare = c.CompareTo(v);
        if (compare != 0)
        {
            return compare > 0;
        }

        // Same numbers: a release beats a pre-release; two pre-releases compare as text.
        return (cPre, vPre) switch
        {
            (null, null) => false,
            (null, _) => true,
            (_, null) => false,
            _ => string.CompareOrdinal(cPre, vPre) > 0,
        };
    }

    /// <summary>Reads tag, page and notes from the GitHub "latest release" JSON; null when the shape is wrong.</summary>
    public static AvailableUpdate? ParseLatestRelease(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } tagName ||
                (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) ||
                (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True))
            {
                return null;
            }

            var page = root.TryGetProperty("html_url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed) &&
                       parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? parsed
                : ReleasePage(tagName);
            var notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
            return new AvailableUpdate(tagName.TrimStart('v'), page, notes, CanInstallInPlace: false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParse(string text, out Version version, out string? preRelease)
    {
        var trimmed = text.Trim().TrimStart('v', 'V');
        var dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        preRelease = dash >= 0 ? trimmed[(dash + 1)..] : null;
        var numbers = dash >= 0 ? trimmed[..dash] : trimmed;
        var plus = numbers.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            numbers = numbers[..plus];
        }

        if (System.Version.TryParse(numbers, out var parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }

        version = new Version();
        return false;
    }
}
