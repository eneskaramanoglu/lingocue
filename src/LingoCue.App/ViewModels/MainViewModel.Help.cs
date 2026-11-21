using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Services;
using LingoCue.Core.Updates;
using Microsoft.Extensions.Logging;

namespace LingoCue.App.ViewModels;

/// <summary>Yardım menu: diagnostics package, log folder, bug report, user guide and the crash dialog.</summary>
public partial class MainViewModel
{
    private readonly DiagnosticsService? _diagnostics;

    public static Uri NewIssuePage { get; } = new(ReleaseVersions.RepositoryUrl + "/issues/new");

    public static Uri UserGuidePage { get; } = new(ReleaseVersions.RepositoryUrl + "/blob/main/docs/KULLANIM.md");

    [RelayCommand]
    private Task CreateDiagnostics() => CreateDiagnosticsAsync(error: null);

    private async Task CreateDiagnosticsAsync(string? error)
    {
        if (_diagnostics is null || _dialogs is null)
        {
            return;
        }

        try
        {
            var path = await _diagnostics.CreatePackageAsync(error);
            _fileManager?.Reveal(path);
            await _dialogs.ShowMessageAsync("Tanılama paketi hazır",
                $"{Path.GetFileName(path)} masaüstüne kaydedildi. İçinde özet, son log'lar ve ayarlar var. Ev klasörünüzün yolu gizlendi; son açılan dosyalar ve API anahtarları pakette yok. " +
                "Log'larda açtığınız video ve proje dosyalarının adları görünebilir; paylaşmadan önce zip'in içine bakabilirsiniz. Hiçbir şey otomatik gönderilmez.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogDiagnosticsFailed(ex);
            await _dialogs.ShowMessageAsync("Tanılama paketi oluşturulamadı", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        if (_platform is not null)
        {
            _fileManager?.OpenFolder(_platform.LogDirectory);
        }
    }

    [RelayCommand]
    private void ReportIssue() => _fileManager?.OpenUrl(NewIssuePage);

    [RelayCommand]
    private void OpenUserGuide() => _fileManager?.OpenUrl(UserGuidePage);

    /// <summary>
    /// An exception nothing else caught (called by App on the UI thread). The app keeps running; the
    /// user can save their work, open the logs or make a diagnostics package.
    /// </summary>
    public async Task ReportUnexpectedErrorAsync(Exception exception)
    {
        LogUnexpected(exception);
        if (_dialogs is null)
        {
            return;
        }

        var wantsPackage = await _dialogs.ConfirmAsync("Beklenmeyen bir hata oluştu",
            $"{exception.Message}\n\nÇalışmanızı kaydetmeniz önerilir. Bir tanılama paketi oluşturup hata bildirimine ekleyebilirsiniz.",
            "Tanılama paketi oluştur", "Kapat");
        if (wantsPackage)
        {
            await CreateDiagnosticsAsync(exception.ToString());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Diagnostics package failed")]
    private partial void LogDiagnosticsFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error")]
    private partial void LogUnexpected(Exception exception);
}
