using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.Core.Platform;
using LingoCue.Core.Settings;
using LingoCue.Llm.Providers;
using LingoCue.Transcription;

namespace LingoCue.App.ViewModels;

public sealed record BeamSearchChoice(bool? Value, string Label);

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

    public SettingsViewModel(ISettingsStore store, ModelResolver resolver, IEnumerable<ITranscriptionEngine> engines, ApiKeysViewModel apiKeys, IHardwareInfoProvider? hardware = null)
    {
        _store = store;
        _resolver = resolver;
        ApiKeys = apiKeys;
        Engines = [.. engines];
        var s = store.Current;

        SelectedEngine = Engines.FirstOrDefault(e => e.Id == s.Transcription.DefaultEngine) ?? (Engines.Count > 0 ? Engines[0] : null);
        WhisperModel = s.Transcription.WhisperModel;
        Language = s.Transcription.Language ?? "auto";
        BeamSearchMode = BeamSearchModes.First(m => m.Value == s.Transcription.BeamSearch);
        UseGlossaryHints = s.Transcription.UseGlossaryHints;
        UseNeuralEngine = s.Transcription.UseNeuralEngine;
        AutoCheckUpdates = s.Updates.AutoCheck;
        LocalBatchSize = s.Llm.LocalBatchSize;
        LocalParallelism = s.Llm.LocalParallelism;
        if (hardware is not null)
        {
            var info = hardware.Read();
            _advice = HardwareAdvisor.Advise(info);
            HardwareText = HardwareAdvisor.Describe(info);
            IsAppleSilicon = info.UnifiedMemory;
            AdviceText = DescribeAdvice(_advice);
        }

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
        FreeTier = s.Llm.FreeTier;
        RequestsPerMinute = s.Llm.RequestsPerMinute;
        LlmEngines = EngineChoice.From(resolver);
        DefaultLlmEngine = LlmEngines.FirstOrDefault(e => string.Equals(e.Id, s.Llm.Provider, StringComparison.OrdinalIgnoreCase)) ?? LlmEngines[0];
        OllamaUrl = s.Llm.OllamaUrl;
        LocalModel = s.Llm.LocalModel;
        if (s.Llm.LocalModel is { } chosen)
        {
            LocalModels.Add(chosen);
        }

        MaxCharsPerLine = s.Reading.MaxCharsPerLine;
        MaxLines = s.Reading.MaxLines;
        MinDuration = s.Reading.MinDurationSeconds;
        MaxDuration = s.Reading.MaxDurationSeconds;
        TargetCps = s.Reading.TargetCps;
        MaxCps = s.Reading.MaxCps;
    }

    public ApiKeysViewModel ApiKeys { get; }

    // ---------------------------------------------------------------- hardware and accuracy

    private readonly HardwareAdvice? _advice;

    /// <summary>"NVIDIA GeForce RTX 3050 Laptop GPU · 4 GB"; null when unknown.</summary>
    public string? HardwareText { get; }

    public string? AdviceText { get; }

    public bool HasAdvice => _advice is not null;

    public IReadOnlyList<BeamSearchChoice> BeamSearchModes { get; } =
    [
        new(null, "Otomatik — GPU varsa en iyi, yoksa hızlı"),
        new(true, "En iyi (beam search, 5 aday; yaklaşık 2 kat yavaş)"),
        new(false, "Hızlı (tek aday)"),
    ];

    [ObservableProperty]
    public partial BeamSearchChoice? BeamSearchMode { get; set; }

    [ObservableProperty]
    public partial bool UseGlossaryHints { get; set; }

    /// <summary>Apple Silicon: the Neural Engine option is offered.</summary>
    public bool IsAppleSilicon { get; }

    [ObservableProperty]
    public partial bool UseNeuralEngine { get; set; }

    /// <summary>Look for a new version at start-up, at most about once a day.</summary>
    [ObservableProperty]
    public partial bool AutoCheckUpdates { get; set; }

    public string VersionText { get; } = $"LingoCue {Core.AppInfo.Version}";

    [ObservableProperty]
    public partial decimal LocalBatchSize { get; set; }

    [ObservableProperty]
    public partial decimal LocalParallelism { get; set; }

    [ObservableProperty]
    public partial string? AdviceStatus { get; private set; }

    /// <summary>Sets the Whisper model, accuracy mode and local batch settings for this machine (saved with Kaydet).</summary>
    [RelayCommand]
    private void ApplyRecommended()
    {
        if (_advice is not { } advice)
        {
            return;
        }

        WhisperModel = Transcription.Whisper.WhisperModels.For(advice.Whisper);
        BeamSearchMode = BeamSearchModes[0];
        LocalBatchSize = advice.LocalBatchSize;
        LocalParallelism = advice.LocalParallelism;
        AdviceStatus = "Önerilen ayarlar seçildi; kaydetmek için Kaydet'e basın.";
    }

    internal static string DescribeAdvice(HardwareAdvice advice)
    {
        var whisper = Transcription.Whisper.WhisperModels.All.First(m => m.Id == Transcription.Whisper.WhisperModels.For(advice.Whisper));
        var local = advice.Tier == HardwareTier.Cpu
            ? "yerel dil modeli işlemcide yavaş çalışır; Gemini önerilir"
            : $"yerel dil modeli olarak ~{advice.LocalModelBillions}B boyutunda (4-bit, ~{advice.LocalModelDownloadGb:0.#} GB) bir Ollama modeli";
        return $"Öneri: Whisper {whisper.DisplayName}{(advice.BeamSearch ? " + beam search" : string.Empty)}; {local}; " +
               $"yerel modelde istek başına {advice.LocalBatchSize} satır, aynı anda {advice.LocalParallelism} istek.";
    }

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

    /// <summary>Free tier: one request at a time, small batches, paced to the per-minute limit.</summary>
    [ObservableProperty]
    public partial bool FreeTier { get; set; }

    [ObservableProperty]
    public partial decimal RequestsPerMinute { get; set; }

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

    // ---------------------------------------------------------------- local engine (Ollama)

    /// <summary>Default engine for Düzelt and Çevir (each dialog can still switch).</summary>
    public IReadOnlyList<EngineChoice> LlmEngines { get; }

    [ObservableProperty]
    public partial EngineChoice DefaultLlmEngine { get; set; }

    [ObservableProperty]
    public partial string OllamaUrl { get; set; } = OllamaProvider.DefaultUrl;

    public System.Collections.ObjectModel.ObservableCollection<string> LocalModels { get; } = [];

    [ObservableProperty]
    public partial string? LocalModel { get; set; }

    [ObservableProperty]
    public partial string? LocalStatus { get; private set; }

    /// <summary>Asks the Ollama at the address typed here (not yet saved) for its installed models.</summary>
    [RelayCommand]
    private async Task RefreshLocalModels()
    {
        LocalStatus = "Ollama'ya bağlanılıyor…";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var url = string.IsNullOrWhiteSpace(OllamaUrl) ? OllamaProvider.DefaultUrl : OllamaUrl.Trim();
        try
        {
            var models = await new OllamaProvider(http, () => url).ListModelsAsync();
            var chosen = LocalModel;
            LocalModels.Clear();
            foreach (var model in models)
            {
                LocalModels.Add(model.Id);
            }

            LocalModel = LocalModels.FirstOrDefault(m => string.Equals(m, chosen, StringComparison.OrdinalIgnoreCase)) ?? LocalModels.FirstOrDefault();
            LocalStatus = models.Count == 0
                ? "Ollama çalışıyor ama indirilmiş model yok. Terminalde: ollama pull gemma3:12b (Korece için qwen3:8b de iyi bir seçenek)."
                : $"Ollama çalışıyor · {models.Count} model: {string.Join(", ", models.Select(m => m.DisplayName))}";
        }
        catch (Exception ex) when (ex is LlmException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            LocalStatus = ex is LlmException { Remedy: { } remedy } ? remedy : $"Ollama'ya bağlanılamadı: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshModels()
    {
        ModelStatus = "Model listesi alınıyor…";
        try
        {
            var models = await _resolver.ModelsAsync("gemini", refresh: true);
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
                BeamSearch = BeamSearchMode?.Value,
                UseGlossaryHints = UseGlossaryHints,
                UseNeuralEngine = UseNeuralEngine,
            },
            Llm = current.Llm with
            {
                Provider = DefaultLlmEngine.Id,
                OllamaUrl = string.IsNullOrWhiteSpace(OllamaUrl) ? OllamaProvider.DefaultUrl : OllamaUrl.Trim(),
                LocalModel = string.IsNullOrWhiteSpace(LocalModel) ? null : LocalModel,
                FastModel = SelectedFast?.Id,
                QualityModel = SelectedQuality?.Id,
                UseQualityModel = UseQualityModel,
                BatchSize = (int)Math.Clamp(BatchSize, 5, 60),
                Parallelism = (int)Math.Clamp(Parallelism, 1, 8),
                Reflect = Reflect,
                ShortenOverlongLines = ShortenOverlong,
                KeepCircumflex = KeepCircumflex,
                FreeTier = FreeTier,
                RequestsPerMinute = (int)Math.Clamp(RequestsPerMinute, 1, 60),
                LocalBatchSize = (int)Math.Clamp(LocalBatchSize, 5, 40),
                LocalParallelism = (int)Math.Clamp(LocalParallelism, 1, 4),
            },
            Updates = current.Updates with { AutoCheck = AutoCheckUpdates },
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
