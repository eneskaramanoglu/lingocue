using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using LingoCue.Core;
using LingoCue.Core.Diagnostics;
using LingoCue.Core.Platform;
using LingoCue.Core.Settings;
using LingoCue.Core.Updates;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.App.Services;

/// <summary>
/// "Yardım → Tanılama paketi oluştur": a zip on the desktop with a summary, the recent logs and the
/// settings (home folder masked, recent files left out). Nothing is sent anywhere; the user attaches
/// the zip to a bug report if they want to.
/// </summary>
public sealed class DiagnosticsService(
    IPlatformInfo platform,
    ISettingsStore settings,
    IHardwareInfoProvider hardware,
    IMediaToolLocator tools,
    IProcessRunner runner,
    IUpdateService updates)
{
    private const int MaxLogFiles = 5;

    public async Task<string> CreatePackageAsync(string? error = null, CancellationToken cancellationToken = default)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"LingoCue-tanilama-{DateTime.Now:yyyyMMdd-HHmm}.zip"));
        var summary = DiagnosticsReport.Redact(DiagnosticsReport.Summary(await FactsAsync(cancellationToken)), home);

        await using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        await AddTextAsync(zip, "ozet.txt", summary, cancellationToken);
        if (!string.IsNullOrWhiteSpace(error))
        {
            await AddTextAsync(zip, "hata.txt", DiagnosticsReport.Redact(error, home), cancellationToken);
        }

        var settingsFile = Path.Combine(platform.AppDataDirectory, "settings.json");
        if (File.Exists(settingsFile))
        {
            var json = await ReadSharedAsync(settingsFile, cancellationToken);
            await AddTextAsync(zip, "settings.json", DiagnosticsReport.Redact(DiagnosticsReport.SanitizeSettings(json), home), cancellationToken);
        }

        if (Directory.Exists(platform.LogDirectory))
        {
            foreach (var log in new DirectoryInfo(platform.LogDirectory).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxLogFiles))
            {
                var text = await ReadSharedAsync(log.FullName, cancellationToken);
                await AddTextAsync(zip, $"logs/{log.Name}", DiagnosticsReport.Redact(text, home), cancellationToken);
            }
        }

        return path;
    }

    private async Task<List<KeyValuePair<string, string?>>> FactsAsync(CancellationToken cancellationToken)
    {
        var s = settings.Current;
        var info = hardware.Read();
        var facts = new List<KeyValuePair<string, string?>>
        {
            new("LingoCue", AppInfo.Version),
            new("Kurulum", updates.CanInstallInPlace ? "Setup.exe (kendini günceller)" : "taşınabilir / macOS / geliştirme"),
            new("İşletim sistemi", RuntimeInformation.OSDescription),
            new("Çalışma zamanı", $"{RuntimeInformation.FrameworkDescription} · {platform.RuntimeIdentifier} · {RuntimeInformation.ProcessArchitecture}"),
            new("Dil/bölge", CultureInfo.CurrentCulture.Name),
            new("Donanım", HardwareAdvisor.Describe(info)),
            new("Sistem belleği", string.Create(CultureInfo.InvariantCulture, $"{info.SystemMemoryBytes / (1024d * 1024 * 1024):0.#} GB")),
            new("Whisper modeli", s.Transcription.WhisperModel),
            new("Whisper doğruluğu", s.Transcription.BeamSearch switch { true => "en iyi", false => "hızlı", null => "otomatik" }),
            new("Varsayılan motor", s.Transcription.DefaultEngine),
            new("Dil modeli sağlayıcısı", s.Llm.Provider),
            new("Ücretsiz katman", s.Llm.FreeTier ? "açık" : "kapalı"),
            new("Güncelleme denetimi", s.Updates.AutoCheck ? "açık" : "kapalı"),
        };

        if (tools.TryLocate(out var found) && found is not null)
        {
            facts.Add(new("FFmpeg", found.FfmpegPath));
            facts.Add(new("FFmpeg sürümü", await FirstLineAsync(found.FfmpegPath, cancellationToken)));
        }
        else
        {
            facts.Add(new("FFmpeg", "bulunamadı"));
        }

        facts.Add(new("Model klasörü", Path.Combine(platform.AppDataDirectory, "models")));
        var models = Path.Combine(platform.AppDataDirectory, "models");
        if (Directory.Exists(models))
        {
            facts.Add(new("İndirilmiş modeller", string.Join(", ", Directory.GetFiles(models, "*.bin").Select(Path.GetFileName))));
        }

        return facts;
    }

    private async Task<string?> FirstLineAsync(string ffmpeg, CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner.RunAsync(ffmpeg, ["-hide_banner", "-version"], cancellationToken: cancellationToken);
            return result.StandardOutput.Split('\n').FirstOrDefault()?.Trim();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ex.Message;
        }
    }

    /// <summary>Serilog keeps the current log open; share the file instead of failing.</summary>
    private static async Task<string> ReadSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task AddTextAsync(ZipArchive zip, string name, string text, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync(text.AsMemory(), cancellationToken);
    }
}
