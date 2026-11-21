using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Playback;
using LingoCue.App.Services;
using LingoCue.Core;
using LingoCue.Core.Fonts;
using LingoCue.Core.Formats;
using LingoCue.Core.Platform;
using LingoCue.Core.Projects;
using LingoCue.Core.Security;
using LingoCue.Core.Segmentation;
using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Time;
using LingoCue.Infrastructure.Security;
using LingoCue.Llm;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;
using LingoCue.Media;
using LingoCue.Media.Probing;
using LingoCue.Media.Rendering;
using LingoCue.Media.Tools;
using LingoCue.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.App.ViewModels;

/// <summary>Welcome screen readiness checks.</summary>
public partial class MainViewModel
{

    /// <summary>The welcome screen's "Hazırlık" list: what the main features need and whether it is there.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ReadinessItem> Readiness { get; } = [];

    private void RefreshReadiness()
    {
        Readiness.Clear();
        if (_toolLocator is not null)
        {
            var ffmpeg = _toolLocator.TryLocate(out _);
            Readiness.Add(new ReadinessItem("FFmpeg", ffmpeg ? "bulundu" : "kurulu değil — video açmak için gerekli", ffmpeg));
        }

        if (_keys is not null)
        {
            var key = _keys.HasGeminiKey;
            Readiness.Add(new ReadinessItem("Gemini anahtarı", key ? "kayıtlı" : "yok — düzeltme ve çeviri için", key));
        }

        if (_settings is not null && _engines.OfType<Transcription.Whisper.WhisperNetEngine>().FirstOrDefault() is { } whisper)
        {
            var model = _settings.Current.Transcription.WhisperModel;
            var ready = whisper.IsModelDownloaded(model);
            var size = Transcription.Whisper.WhisperModels.All.FirstOrDefault(m => m.Id == model)?.SizeBytes ?? 0;
            Readiness.Add(new ReadinessItem("Whisper modeli", ready ? model : $"ilk transkripsiyonda iner (~{size / 1e9:0.#} GB)", ready));
        }

        if (_hardware is not null)
        {
            var info = _hardware.Read();
            var advice = HardwareAdvisor.Advise(info);
            var suggested = Transcription.Whisper.WhisperModels.For(advice.Whisper);
            var current = _settings?.Current.Transcription.WhisperModel;
            var detail = HardwareAdvisor.Describe(info) + (suggested == current
                ? " · ayarlar bu donanıma uygun"
                : $" · önerilen Whisper modeli: {suggested} (Ayarlar → Genel → Önerilen ayarları uygula)");
            Readiness.Add(new ReadinessItem("Donanım", detail, IsReady: true));
        }

        _ = AddOllamaReadinessAsync();
    }

    /// <summary>Optional local engine: asks Ollama briefly in the background so startup never waits for it.</summary>
    private async Task AddOllamaReadinessAsync()
    {
        if (_resolver?.Providers.FirstOrDefault(p => p.Id == OllamaProvider.ProviderId) is not { } ollama)
        {
            return;
        }

        string detail;
        bool ok;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var models = await ollama.ListModelsAsync(timeout.Token);
            ok = models.Count > 0;
            detail = ok ? $"çalışıyor · {models.Count} model" : "çalışıyor, model indirilmemiş (ollama pull gemma3:12b)";
        }
        catch (Exception ex) when (ex is LlmException or HttpRequestException or OperationCanceledException)
        {
            ok = false;
            detail = "yok — isteğe bağlı: ücretsiz, çevrimdışı çeviri için";
        }

        Readiness.Add(new ReadinessItem("Yerel model (Ollama)", detail, ok));
    }
}
