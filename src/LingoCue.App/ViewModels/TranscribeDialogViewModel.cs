using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.Core.Projects;
using LingoCue.Transcription;
using LingoCue.Transcription.Gemini;
using LingoCue.Transcription.Whisper;

namespace LingoCue.App.ViewModels;

public sealed record LanguageOption(string? Code, string Name)
{
    public override string ToString() => Name;
}

public sealed record TranscribeOptions(ITranscriptionEngine Engine, string ModelId, string? Language, bool Diarize, AsrRun? CachedRun);

/// <summary>Choices for a transcription run: engine, model, language, speakers, cache reuse.</summary>
public sealed partial class TranscribeDialogViewModel : ViewModelBase
{
    private static readonly CultureInfo s_tr = CultureInfo.GetCultureInfo("tr-TR");
    private readonly Project _project;
    private readonly TimeSpan _duration;

    public TranscribeDialogViewModel(IEnumerable<ITranscriptionEngine> engines, Project project, TimeSpan duration, string? lastEngineId = null)
    {
        _project = project;
        _duration = duration;
        Engines = [.. engines];
        Languages =
        [
            new("tr", "Türkçe"),
            new(null, "Otomatik algıla"),
            new("en", "İngilizce"),
            new("de", "Almanca"),
            new("fr", "Fransızca"),
            new("es", "İspanyolca"),
            new("ar", "Arapça"),
            new("ru", "Rusça"),
        ];
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == project.SourceLanguage) ?? Languages[0];
        _fallBackWhenUnavailable = lastEngineId is null;
        SelectedEngine = Engines.FirstOrDefault(e => e.Id == (lastEngineId ?? DefaultEngineId)) ?? (Engines.Count > 0 ? Engines[0] : null);
    }

    /// <summary>
    /// Whisper (free, local) is the default while the app is being developed and tested, to avoid paid
    /// API calls; after its DTW/punctuation fixes it is usable on real footage. Gemini gave the best
    /// results on noisy multi-speaker video (docs/reports/asr-comparison.md) and stays one click away.
    /// The fallback below only matters if the default is ever switched back to a key-based engine.
    /// </summary>
    public const string DefaultEngineId = "whisper";

    private const string FallbackEngineId = "whisper";
    private bool _fallBackWhenUnavailable;

    [ObservableProperty]
    public partial string? Note { get; private set; }

    public IReadOnlyList<ITranscriptionEngine> Engines { get; }

    public IReadOnlyList<LanguageOption> Languages { get; }

    [ObservableProperty]
    public partial ITranscriptionEngine? SelectedEngine { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<TranscriptionModel> Models { get; private set; } = [];

    [ObservableProperty]
    public partial TranscriptionModel? SelectedModel { get; set; }

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool Diarize { get; set; }

    [ObservableProperty]
    public partial bool UseCache { get; set; } = true;

    [ObservableProperty]
    public partial string? Problem { get; private set; }

    [ObservableProperty]
    public partial string? Remedy { get; private set; }

    public bool CanDiarize => SelectedEngine?.SupportsDiarization == true;

    public AsrRun? CachedRun => SelectedEngine is null || SelectedModel is null
        ? null
        : _project.FindAsrRun(SelectedEngine.Id, SelectedModel.Id, SelectedLanguage.Code, Diarize && CanDiarize);

    public bool HasCachedRun => CachedRun is not null;

    public string CachedRunText => CachedRun is { } run
        ? $"Bu ayarlarla {run.CreatedAt.ToLocalTime().ToString("g", s_tr)} tarihinde yapılmış sonuç projede kayıtlı ({run.Words.Count} kelime). Yeniden tanıma yapmadan kullanılabilir."
        : string.Empty;

    public string ModelInfo
    {
        get
        {
            if (SelectedModel is null)
            {
                return string.Empty;
            }

            var info = SelectedModel.Description;
            if (SelectedEngine is WhisperNetEngine whisper && SelectedModel.SizeBytes is { } size)
            {
                info += whisper.IsModelDownloaded(SelectedModel.Id)
                    ? " · Model bilgisayarda yüklü."
                    : $" · İlk kullanımda {size / 1_000_000_000d:0.0} GB indirilecek.";
            }

            if (SelectedEngine is GeminiTranscribeEngine)
            {
                info += $" · Bu video için tahmini maliyet: ~{GeminiTranscribeEngine.EstimateCost(_duration).ToString("0.000", CultureInfo.InvariantCulture)} $.";
            }

            return info;
        }
    }

    public bool CanStart => SelectedEngine is not null && SelectedModel is not null && (Problem is null || (UseCache && HasCachedRun));

    public TranscribeOptions? BuildResult()
        => CanStart
            ? new TranscribeOptions(SelectedEngine!, SelectedModel!.Id, SelectedLanguage.Code, Diarize && CanDiarize, UseCache ? CachedRun : null)
            : null;

    partial void OnSelectedEngineChanged(ITranscriptionEngine? value)
    {
        Models = value?.Models ?? [];
        SelectedModel = Models.FirstOrDefault(m => m.Id == value?.DefaultModelId) ?? Models.FirstOrDefault();
        OnPropertyChanged(nameof(CanDiarize));
        _ = RefreshAvailabilityAsync(value);
        Refresh();
    }

    partial void OnSelectedModelChanged(TranscriptionModel? value) => Refresh();

    partial void OnSelectedLanguageChanged(LanguageOption value) => Refresh();

    partial void OnDiarizeChanged(bool value) => Refresh();

    partial void OnUseCacheChanged(bool value) => Refresh();

    private async Task RefreshAvailabilityAsync(ITranscriptionEngine? engine)
    {
        Problem = null;
        Remedy = null;
        if (engine is null)
        {
            return;
        }

        var availability = await engine.CheckAvailabilityAsync();
        if (engine == SelectedEngine && !availability.IsAvailable)
        {
            // First open without a key: quietly use the local engine instead of a disabled dialog.
            if (_fallBackWhenUnavailable && engine.Id == DefaultEngineId &&
                Engines.FirstOrDefault(e => e.Id == FallbackEngineId) is { } fallback)
            {
                _fallBackWhenUnavailable = false;
                Note = $"{availability.Problem} Yerel Whisper seçildi; Gemini için {availability.Remedy}";
                SelectedEngine = fallback;
                return;
            }

            Problem = availability.Problem;
            Remedy = availability.Remedy;
        }

        _fallBackWhenUnavailable = false;

        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(ModelInfo));
        OnPropertyChanged(nameof(CachedRun));
        OnPropertyChanged(nameof(HasCachedRun));
        OnPropertyChanged(nameof(CachedRunText));
        OnPropertyChanged(nameof(CanStart));
    }
}
