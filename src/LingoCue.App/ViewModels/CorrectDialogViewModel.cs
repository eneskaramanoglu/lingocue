using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.Core.Projects;
using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;

namespace LingoCue.App.ViewModels;

public sealed record CorrectOptions(string ModelId, bool Reflect, bool RefreshContext, bool UseQualityModel, string ProviderId);

/// <summary>Shows what a correction run will do and cost before anything is sent.</summary>
public sealed partial class CorrectDialogViewModel : ViewModelBase
{
    private static readonly CultureInfo s_tr = CultureInfo.GetCultureInfo("tr-TR");
    private readonly ModelResolver _resolver;
    private readonly LlmSettings _settings;
    private readonly Project _project;
    private readonly IReadOnlyList<LineItem> _lines;

    public CorrectDialogViewModel(ModelResolver resolver, AppSettings settings, Project project, SubtitleTrack track)
    {
        _resolver = resolver;
        _settings = settings.Llm;
        _project = project;
        TrackName = track.Name;
        _lines = [.. track.Cues.Select(c => new LineItem(c.Id, c.Text, c.Duration, c.Speaker, c.Marks.HasFlag(CueMarks.Locked)))];
        Reflect = _settings.Reflect;
        UseQualityModel = _settings.UseQualityModel;
        RefreshContext = project.Context.Summary is null;
        LockedCount = _lines.Count(l => l.Locked);
        Engines = EngineChoice.From(resolver);
        Engine = Engines.FirstOrDefault(e => e.Id == _resolver.Provider.Id) ?? Engines[0];
        _ = ResolveAsync();
    }

    public IReadOnlyList<EngineChoice> Engines { get; }

    [ObservableProperty]
    public partial EngineChoice Engine { get; set; }

    partial void OnEngineChanged(EngineChoice value) => _ = ResolveAsync();

    public string TrackName { get; }

    public int LineCount => _lines.Count;

    public int LockedCount { get; }

    public bool HasContext => _project.Context.Summary is not null;

    public int GlossaryCount => _project.Glossary.Count(g => g.TargetLanguage is null);

    [ObservableProperty]
    public partial bool Reflect { get; set; }

    [ObservableProperty]
    public partial bool RefreshContext { get; set; }

    [ObservableProperty]
    public partial bool UseQualityModel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial string? ModelId { get; private set; }

    [ObservableProperty]
    public partial string EstimateText { get; private set; } = "Model belirleniyor…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial string? Problem { get; private set; }

    public bool CanStart => ModelId is not null && Problem is null && LineCount > LockedCount;

    public CorrectOptions? BuildResult() => CanStart ? new CorrectOptions(ModelId!, Reflect, RefreshContext, UseQualityModel, Engine.Id) : null;

    partial void OnReflectChanged(bool value) => UpdateEstimate();

    partial void OnRefreshContextChanged(bool value) => UpdateEstimate();

    partial void OnUseQualityModelChanged(bool value) => _ = ResolveAsync();

    private async Task ResolveAsync()
    {
        Problem = null;
        try
        {
            ModelId = null;
            EstimateText = "Model belirleniyor…";
            ModelId = await _resolver.ResolveAsync(_settings with { UseQualityModel = UseQualityModel, Provider = Engine.Id });
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
        if (ModelId is null)
        {
            return;
        }

        var options = new PipelineOptions { BatchSize = _settings.BatchSize, Reflect = Reflect, ShortenOverlong = _settings.ShortenOverlongLines };
        if (Engine.IsLocal)
        {
            EstimateText = EngineChoice.LocalEstimate(ModelId);
            return;
        }

        var estimate = CostEstimator.Estimate(_lines, options, GlossaryCount, RefreshContext || !HasContext, _resolver.ProviderFor(Engine.Id).PriceFor(ModelId));
        var money = estimate.Usd is { } usd ? $"~{usd.ToString("0.000", CultureInfo.InvariantCulture)} $ (ücretli anahtarla)" : "fiyat bilinmiyor";
        var free = estimate.FreeTierAvailable ? " · ücretsiz katmanda 0 $ (sınırlı hız)" : " · bu modelin ücretsiz katmanı yok";
        EstimateText = string.Create(s_tr, $"Model: {ModelId} · tahmini {estimate.InputTokens:N0} giriş + {estimate.OutputTokens:N0} çıkış token · {money}{free}");
    }
}
