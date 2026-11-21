using System.Net.Http.Headers;
using LingoCue.Core;
using LingoCue.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace LingoCue.Infrastructure.Updates;

/// <summary>
/// Windows copies installed with Setup.exe update in place through Velopack (GitHub Releases feed,
/// delta packages). Everything else — macOS (unsigned, so no in-place update), the portable zip and
/// development builds — asks GitHub for the latest release and offers its page. Only the version is
/// requested; nothing about the user or their files is sent.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _http;
    private readonly UpdateManager? _velopack;
    private UpdateInfo? _pending;

    public UpdateService(HttpClient http)
    {
        _http = http;
        try
        {
            // LINGOCUE_UPDATE_SOURCE (a folder or URL with releases.win.json) is for testing an update
            // end to end without publishing a release.
            var testSource = Environment.GetEnvironmentVariable("LINGOCUE_UPDATE_SOURCE");
            var manager = string.IsNullOrWhiteSpace(testSource)
                ? new UpdateManager(new GithubSource(ReleaseVersions.RepositoryUrl.ToString(), null, false, null), new UpdateOptions(), null)
                : new UpdateManager(testSource, new UpdateOptions(), null);
            _velopack = OperatingSystem.IsWindows() && manager.IsInstalled && !manager.IsPortable ? manager : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _velopack = null; // not an installed copy
        }
    }

    public string CurrentVersion => _velopack?.CurrentVersion?.ToString() ?? AppInfo.Version;

    public bool CanInstallInPlace => _velopack is not null;

    public AvailableUpdate? PendingRestart => _velopack?.UpdatePendingRestart is { } asset
        ? new AvailableUpdate(asset.Version.ToString(), ReleaseVersions.ReleasePage(asset.Version.ToString()), asset.NotesMarkdown, CanInstallInPlace: true)
        : null;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_velopack is not null)
        {
            _pending = await _velopack.CheckForUpdatesAsync().WaitAsync(cancellationToken);
            if (_pending?.TargetFullRelease is not { } target)
            {
                return null;
            }

            var version = target.Version.ToString();
            return new AvailableUpdate(version, ReleaseVersions.ReleasePage(version), target.NotesMarkdown, CanInstallInPlace: true);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseVersions.LatestReleaseApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.Version));
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null; // no release yet, rate limit: try again another day
        }

        var latest = ReleaseVersions.ParseLatestRelease(await response.Content.ReadAsStringAsync(cancellationToken));
        return latest is not null && ReleaseVersions.IsNewer(latest.Version, CurrentVersion) ? latest : null;
    }

    public async Task DownloadAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (_velopack is null || _pending is null)
        {
            throw new InvalidOperationException("This copy cannot install updates in place.");
        }

        await _velopack.DownloadUpdatesAsync(_pending, p => progress?.Report(p), cancellationToken);
    }

    public void RestartToApply(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var asset = _pending?.TargetFullRelease ?? _velopack?.UpdatePendingRestart;
        if (_velopack is null || asset is null)
        {
            throw new InvalidOperationException("No downloaded update to apply.");
        }

        _velopack.ApplyUpdatesAndRestart(asset, null);
    }
}
