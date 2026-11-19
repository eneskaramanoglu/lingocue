using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.Core.Settings;
using LingoCue.Llm.Providers;
using LingoCue.Transcription;

namespace LingoCue.App.ViewModels;

public sealed record ModelChoice(string? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Edits a copy of <see cref="AppSettings"/>; nothing is written until <see cref="Save"/>.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private const string AutoFastLabel = "Otomatik — en güncel kararlı Flash";
    private const string AutoQualityLabel = "Otomatik — en güncel Pro (yoksa Flash)";
    private readonly ISettingsStore _store;
    private readonly ModelResolver _resolver;

    public SettingsViewModel(ISettingsStore store, ModelResolver resolver, IEnumerable<ITranscriptionEngine> engines, ApiKeysViewModel apiKeys)
    {
        _store = store;
        _resolver = resolver;
        ApiKeys = apiKeys;
        Engines = [.. engines];
        var s = store.Current;

        SelectedEngine = Engines.FirstOrDefault(e => e.Id == s.Transcription.DefaultEngine) ?? (Engines.Count > 0 ? Engines[0] : null);
        WhisperModel = s.Transcription.WhisperModel;
        Language = s.Transcription.Language ?? "auto";

        FastModels = [new ModelChoice(null, AutoFastLabel)];
        QualityModels = [new ModelChoice(null, AutoQualityLabel)];
        AddChoice(FastModels, s.Llm.FastModel);
        AddChoice(QualityModels, s.Llm.QualityModel);
        SelectedFast = FastModels.First(m => m.Id == s.Llm.FastModel);
        SelectedQuality = QualityModels.First(m => m.Id == s.Llm.QualityModel);
        UseQualityModel = s.Llm.UseQualityModel;
        BatchSize = s.Llm.BatchSize;
        Parallelism = s.Llm.Parallelism;
        Reflect = s.Llm.Reflect;
        ShortenOverlong = s.Llm.ShortenOverlongLines;
        KeepCircumflex = s.Llm.KeepCircumflex;

        MaxCharsPerLine = s.Reading.MaxCharsPerLine;
        MaxLines = s.Reading.MaxLines;
        MinDuration = s.Reading.MinDurationSeconds;
        MaxDuration = s.Reading.MaxDurationSeconds;
        TargetCps = s.Reading.TargetCps;
        MaxCps = s.Reading.MaxCps;
    }

    public ApiKeysViewModel ApiKeys { get; }

    public IReadOnlyList<ITranscriptionEngine> Engines { get; }

    public IReadOnlyList<string> WhisperModels { get; } = [.. Transcription.Whisper.WhisperModels.All.Select(m => m.Id)];

    public IReadOnlyList<string> LanguageCodes { get; } = ["tr", "auto", "en", "de", "fr", "es", "ar", "ru"];

    [ObservableProperty]
    public partial ITranscriptionEngine? SelectedEngine { get; set; }

    [ObservableProperty]
    public partial string WhisperModel { get; set; }

    [ObservableProperty]
    public partial string Language { get; set; }

    public ObservableCollection<ModelChoice> FastModels { get; }

    public ObservableCollection<ModelChoice> QualityModels { get; }

    [ObservableProperty]
    public partial ModelChoice? SelectedFast { get; set; }

    [ObservableProperty]
    public partial ModelChoice? SelectedQuality { get; set; }

    [ObservableProperty]
    public partial bool UseQualityModel { get; set; }

    [ObservableProperty]
    public partial decimal BatchSize { get; set; }

    [ObservableProperty]
    public partial decimal Parallelism { get; set; }

    [ObservableProperty]
    public partial bool Reflect { get; set; }

    [ObservableProperty]
    public partial bool ShortenOverlong { get; set; }

    [ObservableProperty]
    public partial bool KeepCircumflex { get; set; }

    [ObservableProperty]
    public partial decimal MaxCharsPerLine { get; set; }

    [ObservableProperty]
    public partial decimal MaxLines { get; set; }

    [ObservableProperty]
    public partial double MinDuration { get; set; }

    [ObservableProperty]
    public partial double MaxDuration { get; set; }

    [ObservableProperty]
    public partial double TargetCps { get; set; }

    [ObservableProperty]
    public partial double MaxCps { get; set; }

    [ObservableProperty]
    public partial string? ModelStatus { get; private set; }

#pragma warning disable CA1822 // Instance member so the XAML compiled binding can reach it.
    public string PrivacyNote =>
        "Ücretsiz katmanda Google, gönderilen içeriği ürünlerini geliştirmek için kullanabilir. " +
        "Kişisel ya da gizli videolar için faturalandırması açık (ücretli) bir anahtar kullanın; ücretli kullanımda içerik bu amaçla kullanılmaz.";
#pragma warning restore CA1822

    [RelayCommand]
    private async Task RefreshModels()
    {
        ModelStatus = "Model listesi alınıyor…";
        try
        {
            var models = await _resolver.ModelsAsync(refresh: true);
            var fast = SelectedFast?.Id;
            var quality = SelectedQuality?.Id;
            Fill(FastModels, AutoFastLabel, models);
            Fill(QualityModels, AutoQualityLabel, models);
            SelectedFast = FastModels.FirstOrDefault(m => m.Id == fast) ?? FastModels[0];
            SelectedQuality = QualityModels.FirstOrDefault(m => m.Id == quality) ?? QualityModels[0];
            var autoFast = ModelCatalog.PickFast(models)?.Id ?? "—";
            var autoQuality = ModelCatalog.PickQuality(models)?.Id ?? "—";
            ModelStatus = $"{models.Count} model bulundu. Otomatik seçimler: {autoFast} / kalite: {autoQuality}.";
        }
        catch (Exception ex) when (ex is LlmException or HttpRequestException or InvalidOperationException)
        {
            ModelStatus = ex is LlmException { Remedy: { } remedy } ? remedy : $"Model listesi alınamadı: {ex.Message}";
        }
    }

    public void Save()
    {
        var current = _store.Current;
        _store.Save(current with
        {
            Transcription = current.Transcription with
            {
                DefaultEngine = SelectedEngine?.Id ?? current.Transcription.DefaultEngine,
                WhisperModel = WhisperModel,
                Language = Language == "auto" ? null : Language,
            },
            Llm = current.Llm with
            {
                FastModel = SelectedFast?.Id,
                QualityModel = SelectedQuality?.Id,
                UseQualityModel = UseQualityModel,
                BatchSize = (int)Math.Clamp(BatchSize, 5, 60),
                Parallelism = (int)Math.Clamp(Parallelism, 1, 8),
                Reflect = Reflect,
                ShortenOverlongLines = ShortenOverlong,
                KeepCircumflex = KeepCircumflex,
            },
            Reading = current.Reading with
            {
                MaxCharsPerLine = (int)MaxCharsPerLine,
                MaxLines = (int)MaxLines,
                MinDurationSeconds = MinDuration,
                MaxDurationSeconds = MaxDuration,
                TargetCps = TargetCps,
                MaxCps = MaxCps,
            },
        });
    }

    private static void AddChoice(ObservableCollection<ModelChoice> list, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && list.All(m => m.Id != id))
        {
            list.Add(new ModelChoice(id, id));
        }
    }

    private static void Fill(ObservableCollection<ModelChoice> list, string autoLabel, IReadOnlyList<LlmModelInfo> models)
    {
        list.Clear();
        list.Add(new ModelChoice(null, autoLabel));
        foreach (var model in models)
        {
            list.Add(new ModelChoice(model.Id, model.IsPreview ? $"{model.Id} (önizleme)" : model.Id));
        }
    }
}
