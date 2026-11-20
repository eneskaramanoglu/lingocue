using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.Core.Projects;
using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;

namespace LingoCue.App.ViewModels;

/// <param name="TargetLanguages">One or more languages; each becomes its own track, translated in this order.</param>
public sealed record TranslateOptions(SubtitleTrack Source, IReadOnlyList<string> TargetLanguages, string ModelId, bool Reflect, bool RefreshContext, string ProviderId);

public sealed record LanguageChoice(string Code, string Label);

/// <summary>A target language chip in the Çevir dialog.</summary>
public sealed partial class TargetLanguageItem(LanguageChoice choice) : ObservableObject
{
    public LanguageChoice Choice { get; } = choice;

    public string Label => Choice.Label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Shows what a translation run will do and cost. The source defaults to the latest corrected track
/// (the edited text is authoritative); the recognized original goes along as a hint.
/// </summary>
public sealed partial class TranslateDialogViewModel : ViewModelBase
{
    private static readonly CultureInfo s_tr = CultureInfo.GetCultureInfo("tr-TR");
    private readonly ModelResolver _resolver;
    private readonly LlmSettings _settings;
    private readonly Project _project;

    public TranslateDialogViewModel(ModelResolver resolver, AppSettings settings, Project project)
    {
        _resolver = resolver;
        _settings = settings.Llm;
        _project = project;
        Targets = [.. Languages.Select(l => new TargetLanguageItem(l))];
        Engines = EngineChoice.From(resolver);
        Engine = Engines.FirstOrDefault(e => e.Id == resolver.Provider.Id) ?? Engines[0];
        Sources = [.. project.Tracks.Where(t => t.Cues.Count > 0)];
        var active = project.ActiveTrack is { Cues.Count: > 0 } a ? a : null;
        Source = Sources.LastOrDefault(t => t.Kind == TrackKind.Corrected) ?? active ?? (Sources.Count > 0 ? Sources[0] : null);
        Reflect = _settings.Reflect;
        RefreshContext = project.Context.Summary is null;
        foreach (var item in Targets)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TargetLanguageItem.IsSelected))
                {
                    OnTargetsChanged();
                }
            };
        }

        UpdateTargetAvailability();
        (Targets.FirstOrDefault(t => t.IsEnabled) ?? Targets[0]).IsSelected = true;
        _ = ResolveAsync();
    }

    public IReadOnlyList<EngineChoice> Engines { get; }

    [ObservableProperty]
    public partial EngineChoice Engine { get; set; }

    partial void OnEngineChanged(EngineChoice value) => _ = ResolveAsync();

    public IReadOnlyList<SubtitleTrack> Sources { get; }

    public static IReadOnlyList<LanguageChoice> Languages { get; } =
    [
        new("en", "İngilizce"), new("ko", "Korece"), new("tr", "Türkçe"), new("de", "Almanca"), new("fr", "Fransızca"),
        new("es", "İspanyolca"), new("it", "İtalyanca"), new("ar", "Arapça"), new("ru", "Rusça"), new("ja", "Japonca"),
        new("az", "Azerbaycan Türkçesi"),
    ];

    /// <summary>Target language chips; several can be selected (e.g. English and Korean in one run).</summary>
    public IReadOnlyList<TargetLanguageItem> Targets { get; }

    public IReadOnlyList<string> SelectedTargets => [.. Targets.Where(t => t.IsSelected && t.IsEnabled).Select(t => t.Choice.Code)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(SourceInfo), nameof(HasReference))]
    public partial SubtitleTrack? Source { get; set; }

    [ObservableProperty]
    public partial bool Reflect { get; set; }

    [ObservableProperty]
    public partial bool RefreshContext { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial string? ModelId { get; private set; }

    [ObservableProperty]
    public partial string EstimateText { get; private set; } = "Model belirleniyor…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial string? Problem { get; private set; }

    public int GlossaryCount => _project.Glossary.Count(g => g.TargetLanguage is null || SelectedTargets.Contains(g.TargetLanguage));

    /// <summary>The source is derived from another track, so the recognized original can go along as a hint.</summary>
    public bool HasReference => Source?.SourceTrackId is not null;

    public string SourceInfo => Source is null
        ? string.Empty
        : string.Create(s_tr, $"{Source.Cues.Count} satır · {LanguageCodes.DisplayName(Source.Language)} · sözlükte {GlossaryCount} terim") +
          (HasReference ? " · orijinal konuşma metni referans olarak eklenir" : string.Empty);

    /// <summary>No target language is selected (the source's own language cannot be picked).</summary>
    public bool NoTarget => SelectedTargets.Count == 0;

    public bool HasKorean => SelectedTargets.Contains("ko");

    public bool CanStart => ModelId is not null && Problem is null && Source is not null && !NoTarget;

    public TranslateOptions? BuildResult()
        => CanStart ? new TranslateOptions(Source!, SelectedTargets, ModelId!, Reflect, RefreshContext, Engine.Id) : null;

    partial void OnSourceChanged(SubtitleTrack? value)
    {
        UpdateTargetAvailability();
        OnTargetsChanged();
    }

    // The source's own language is not a target.
    private void UpdateTargetAvailability()
    {
        foreach (var item in Targets)
        {
            item.IsEnabled = Source is null || !string.Equals(Source.Language, item.Choice.Code, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnTargetsChanged()
    {
        OnPropertyChanged(nameof(SelectedTargets));
        OnPropertyChanged(nameof(NoTarget));
        OnPropertyChanged(nameof(HasKorean));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(SourceInfo));
        UpdateEstimate();
    }

    partial void OnReflectChanged(bool value) => UpdateEstimate();

    partial void OnRefreshContextChanged(bool value) => UpdateEstimate();

    private async Task ResolveAsync()
    {
        Problem = null;
        try
        {
            ModelId = null;
            EstimateText = "Model belirleniyor…";
            ModelId = await _resolver.ResolveAsync(_settings with { Provider = Engine.Id });
            UpdateEstimate();
        }
        catch (LlmException ex)
        {
            ModelId = null;
            Problem = ex.Remedy ?? ex.Message;
            EstimateText = string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            ModelId = null;
            Problem = $"Model listesi alınamadı: {ex.Message}";
            EstimateText = string.Empty;
        }
    }

    private void UpdateEstimate()
    {
        if (ModelId is null || Source is null)
        {
            return;
        }

        // Hints roughly double the input of lines that differ from the recognized text.
        var lines = Source.Cues.Select(c => new LineItem(c.Id, c.Text, c.Duration, c.Speaker, c.Marks.HasFlag(CueMarks.Locked),
            Reference: HasReference ? TrackOperations.OriginalText(_project, Source, c) : null)).ToList();
        var options = new PipelineOptions { BatchSize = _settings.BatchSize, Reflect = Reflect, ShortenOverlong = _settings.ShortenOverlongLines };
        if (Engine.IsLocal)
        {
            EstimateText = EngineChoice.LocalEstimate(ModelId);
            return;
        }

        var estimate = CostEstimator.Estimate(lines, options, GlossaryCount, RefreshContext || _project.Context.Summary is null, _resolver.ProviderFor(Engine.Id).PriceFor(ModelId));

        // Each target is a full run; the context pass only happens once.
        var runs = Math.Max(1, SelectedTargets.Count);
        var input = estimate.InputTokens * runs;
        var output = estimate.OutputTokens * runs;
        var money = estimate.Usd is { } usd ? $"~{(usd * runs).ToString("0.000", CultureInfo.InvariantCulture)} $ (ücretli anahtarla)" : "fiyat bilinmiyor";
        var free = estimate.FreeTierAvailable ? " · ücretsiz katmanda 0 $ (sınırlı hız)" : " · bu modelin ücretsiz katmanı yok";
        var languages = runs > 1 ? string.Create(s_tr, $"{runs} dil · ") : string.Empty;
        EstimateText = string.Create(s_tr, $"Model: {ModelId} · {languages}tahmini {input:N0} giriş + {output:N0} çıkış token · {money}{free}");
    }
}
