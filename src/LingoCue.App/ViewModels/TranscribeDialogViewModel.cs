using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.Core.Projects;
using LingoCue.Core.Text;
using LingoCue.Transcription;
using LingoCue.Transcription.Gemini;
using LingoCue.Transcription.Whisper;

namespace LingoCue.App.ViewModels;

public sealed record LanguageOption(string? Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One language box of the "Karışık" choice.</summary>
public sealed partial class MixedLanguageChoice(string code, string name) : ObservableObject
{
    public string Code { get; } = code;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;
}

public sealed record TranscribeOptions(ITranscriptionEngine Engine, string ModelId, string? Language, bool Diarize, AsrRun? CachedRun);

/// <summary>Choices for a transcription run: engine, model, language, speakers, cache reuse.</summary>
public sealed partial class TranscribeDialogViewModel : ViewModelBase
{
    private static readonly CultureInfo s_tr = CultureInfo.GetCultureInfo("tr-TR");
    private readonly Project _project;
    private readonly TimeSpan _duration;

    /// <summary>Code of the "several languages" entry; the real selection is built from <see cref="MixedChoices"/>.</summary>
    public const string MixedCode = "mixed";

    /// <param name="lastLanguage">The language selection of the previous run in this session (may be mixed).</param>
    public TranscribeDialogViewModel(IEnumerable<ITranscriptionEngine> engines, Project project, TimeSpan duration, string? lastEngineId = null, string? lastLanguage = null)
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
            new("ko", "Korece"),
            new(MixedCode, "Karışık (birden çok dil)"),
        ];
        MixedChoices = [.. Languages.Where(l => l.Code is not (null or MixedCode)).Select(l => new MixedLanguageChoice(l.Code!, l.Name))];
        foreach (var choice in MixedChoices)
        {
            choice.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MixedLanguageChoice.IsChecked))
                {
                    OnMixedChanged();
                }
            };
        }

        var initial = lastLanguage ?? project.SourceLanguage;
        var mixed = LanguageSelection.Mixed(initial);
        if (mixed.Count > 1)
        {
            foreach (var choice in MixedChoices)
            {
                choice.IsChecked = mixed.Contains(choice.Code);
            }

            SelectedLanguage = Languages.First(l => l.Code == MixedCode);
        }
        else
        {
            SelectedLanguage = Languages.FirstOrDefault(l => l.Code == initial) ?? Languages[0];
        }
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

    /// <summary>Languages offered for a mixed recording; at most <see cref="LanguageSelection.MaxMixed"/> can be ticked.</summary>
    public IReadOnlyList<MixedLanguageChoice> MixedChoices { get; }

    public bool IsMixed => SelectedLanguage.Code == MixedCode;

    /// <summary>The selection sent to the engine: one code, null (detect) or <c>tr+en+ko</c>.</summary>
    public string? EffectiveLanguage => IsMixed
        ? LanguageSelection.ForMixed(MixedChoices.Where(c => c.IsChecked).Select(c => c.Code))
        : SelectedLanguage.Code;

    public string MixedHint => MixedChoices.Count(c => c.IsChecked) < 2
        ? "Videoda konuşulan dilleri seçin (en az 2, en çok 3)."
        : "Her konuşma bölümünün dili seçilen diller arasından algılanır ve satırlar dil etiketi alır. Aynı cümle içindeki dil geçişleri ayrılmaz.";

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
        : _project.FindAsrRun(SelectedEngine.Id, SelectedModel.Id, EffectiveLanguage, Diarize && CanDiarize);

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

    public bool CanStart => SelectedEngine is not null && SelectedModel is not null && (!IsMixed || LanguageSelection.IsMixed(EffectiveLanguage)) &&
                            (Problem is null || (UseCache && HasCachedRun));

    public TranscribeOptions? BuildResult()
        => CanStart
            ? new TranscribeOptions(SelectedEngine!, SelectedModel!.Id, EffectiveLanguage, Diarize && CanDiarize, UseCache ? CachedRun : null)
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

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        // Start a mixed choice from the project's language, the one spoken most often.
        if (value.Code == MixedCode && !MixedChoices.Any(c => c.IsChecked) &&
            MixedChoices.FirstOrDefault(c => c.Code == LanguageCodes.Primary(_project.SourceLanguage)) is { } main)
        {
            main.IsChecked = true;
        }

        Refresh();
    }

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

    private void OnMixedChanged()
    {
        var full = MixedChoices.Count(c => c.IsChecked) >= LanguageSelection.MaxMixed;
        foreach (var choice in MixedChoices)
        {
            choice.IsEnabled = choice.IsChecked || !full;
        }

        OnPropertyChanged(nameof(MixedHint));
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsMixed));
        OnPropertyChanged(nameof(EffectiveLanguage));
        OnPropertyChanged(nameof(ModelInfo));
        OnPropertyChanged(nameof(CachedRun));
        OnPropertyChanged(nameof(HasCachedRun));
        OnPropertyChanged(nameof(CachedRunText));
        OnPropertyChanged(nameof(CanStart));
    }
}
