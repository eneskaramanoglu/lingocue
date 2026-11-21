using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.Core.Platform;
using LingoCue.Core.Updates;
using Microsoft.Extensions.Logging;

namespace LingoCue.App.ViewModels;

/// <summary>
/// Update checks: at start-up (when enabled and due) and from Yardım → Güncellemeleri denetle.
/// An installed Windows copy downloads the update quietly and offers a restart; elsewhere the
/// banner links to the release page.
/// </summary>
public partial class MainViewModel
{
    private readonly IUpdateService? _updates;
    private readonly IFileManager? _fileManager;
    private bool _checkingUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateText))]
    public partial AvailableUpdate? Update { get; private set; }

    /// <summary>Downloaded and waiting for a restart (in-place updates only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText), nameof(CanRestartToUpdate))]
    public partial bool IsUpdateReady { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    public partial int? UpdateDownloadPercent { get; private set; }

    public bool HasUpdate => Update is not null;

    public bool CanRestartToUpdate => IsUpdateReady;

    public bool OffersDownloadPage => Update is { CanInstallInPlace: false };

    public string? UpdateText => Update switch
    {
        null => null,
        _ when IsUpdateReady => $"LingoCue {Update.Version} indirildi; uygulama yeniden başlatılınca kurulur.",
        { CanInstallInPlace: true } => $"LingoCue {Update.Version} indiriliyor… %{UpdateDownloadPercent ?? 0}",
        _ => $"LingoCue {Update.Version} çıktı (şu an {_updates?.CurrentVersion}). Yeni sürümü sürüm sayfasından indirebilirsiniz.",
    };

    /// <summary>Called once the main window is shown; never throws.</summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (_updates is null || _settings is null)
        {
            return;
        }

        if (_updates.PendingRestart is { } pending)
        {
            Update = pending;
            IsUpdateReady = true;
            return;
        }

        if (!_settings.Current.Updates.IsCheckDue(DateTimeOffset.UtcNow))
        {
            return;
        }

        await CheckForUpdatesCoreAsync(manual: false);
    }

    [RelayCommand]
    private Task CheckForUpdates() => CheckForUpdatesCoreAsync(manual: true);

    private async Task CheckForUpdatesCoreAsync(bool manual)
    {
        if (_updates is null || _settings is null || _checkingUpdates)
        {
            return;
        }

        _checkingUpdates = true;
        try
        {
            var found = await _updates.CheckAsync();
            _settings.Save(_settings.Current with { Updates = _settings.Current.Updates with { LastCheckUtc = DateTimeOffset.UtcNow } });
            if (found is null || (!manual && found.Version == _settings.Current.Updates.SkippedVersion))
            {
                if (manual && _dialogs is not null)
                {
                    await _dialogs.ShowMessageAsync("Güncel", $"En güncel sürümü kullanıyorsunuz (LingoCue {_updates.CurrentVersion}).");
                }

                return;
            }

            Update = found;
            OnPropertyChanged(nameof(OffersDownloadPage));
            if (found.CanInstallInPlace)
            {
                UpdateDownloadPercent = 0;
                await _updates.DownloadAsync(found, new Progress<int>(p => UpdateDownloadPercent = p));
                IsUpdateReady = true;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            LogUpdateCheckFailed(ex);
            if (Update is { CanInstallInPlace: true } && !IsUpdateReady)
            {
                Update = null; // the download failed; try again next time
            }

            if (manual && _dialogs is not null)
            {
                await _dialogs.ShowMessageAsync("Güncelleme denetlenemedi", $"GitHub'a ulaşılamadı: {ex.Message}");
            }
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    /// <summary>Asks about exports and unsaved work like closing does, then lets Velopack swap the files and restart.</summary>
    [RelayCommand]
    private async Task RestartToUpdate()
    {
        if (_updates is null || Update is not { } update || !IsUpdateReady)
        {
            return;
        }

        // Same questions as closing the window: running exports and unsaved changes.
        if (!await ConfirmCloseAsync())
        {
            return;
        }

        _updates.RestartToApply(update);
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (Update is { } update)
        {
            _fileManager?.OpenUrl(update.ReleasePage);
        }
    }

    [RelayCommand]
    private void SkipUpdate()
    {
        if (Update is { } update && _settings is not null)
        {
            _settings.Save(_settings.Current with { Updates = _settings.Current.Updates with { SkippedVersion = update.Version } });
        }

        Update = null;
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        // Only hides the banner: a downloaded update is still applied when the app next starts.
        Update = null;
        IsUpdateReady = false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check failed")]
    private partial void LogUpdateCheckFailed(Exception exception);
}
