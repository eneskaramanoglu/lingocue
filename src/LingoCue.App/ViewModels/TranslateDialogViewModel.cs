using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.Core.Projects;
using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;

namespace LingoCue.App.ViewModels;

public sealed record TranslateOptions(SubtitleTrack Source, string TargetLanguage, string ModelId, bool Reflect, bool RefreshContext);

public sealed record LanguageChoice(string Code, string Label);

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
        Sources = [.. project.Tracks.Where(t => t.Cues.Count > 0)];
        var active = project.ActiveTrack is { Cues.Count: > 0 } a ? a : null;
        Source = Sources.LastOrDefault(t => t.Kind == TrackKind.Corrected) ?? active ?? (Sources.Count > 0 ? Sources[0] : null);
        Reflect = _settings.Reflect;
        RefreshContext = project.Context.Summary is null;
        Target = Languages.First(l => l.Code != Source?.Language);
        _ = ResolveAsync();
    }

    public IReadOnlyList<SubtitleTrack> Sources { get; }

    public IReadOnlyList<LanguageChoice> Languages { get; } =
    [
        new("en", "İngilizce"), new("tr", "Türkçe"), new("de", "Almanca"), new("fr", "Fransızca"), new("es", "İspanyolca"),
        new("it", "İtalyanca"), new("ar", "Arapça"), new("ru", "Rusça"), new("az", "Azerbaycan Türkçesi"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(SourceInfo), nameof(HasReference))]
    public partial SubtitleTrack? Source { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial LanguageChoice Target { get; set; }

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

    public int GlossaryCount => _project.Glossary.Count(g => g.TargetLanguage is null || g.TargetLanguage == Target.Code);

    /// <summary>The source is derived from another track, so the recognized original can go along as a hint.</summary>
    public bool HasReference => Source?.SourceTrackId is not null;

    public string SourceInfo => Source is null
        ? string.Empty
        : string.Create(s_tr, $"{Source.Cues.Count} satır · {LanguageCodes.DisplayName(Source.Language)} · sözlükte {GlossaryCount} terim") +
          (HasReference ? " · orijinal konuşma metni referans olarak eklenir" : string.Empty);

    public bool SameLanguage => Source is not null && string.Equals(Source.Language, Target.Code, StringComparison.OrdinalIgnoreCase);

    public bool CanStart => ModelId is not null && Problem is null && Source is not null && !SameLanguage;

    public TranslateOptions? BuildResult()
        => CanStart ? new TranslateOptions(Source!, Target.Code, ModelId!, Reflect, RefreshContext) : null;

    partial void OnSourceChanged(SubtitleTrack? value)
    {
        OnPropertyChanged(nameof(SameLanguage));
        UpdateEstimate();
    }

    partial void OnTargetChanged(LanguageChoice value)
    {
        OnPropertyChanged(nameof(SameLanguage));
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
            ModelId = await _resolver.ResolveAsync(_settings);
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
        var estimate = CostEstimator.Estimate(lines, options, GlossaryCount, RefreshContext || _project.Context.Summary is null, _resolver.Provider.PriceFor(ModelId));
        var money = estimate.Usd is { } usd ? $"~{usd.ToString("0.000", CultureInfo.InvariantCulture)} $ (ücretli anahtarla)" : "fiyat bilinmiyor";
        var free = estimate.FreeTierAvailable ? " · ücretsiz katmanda 0 $ (sınırlı hız)" : " · bu modelin ücretsiz katmanı yok";
        EstimateText = string.Create(s_tr, $"Model: {ModelId} · tahmini {estimate.InputTokens:N0} giriş + {estimate.OutputTokens:N0} çıkış token · {money}{free}");
    }
}
