using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;

namespace LingoCue.App.ViewModels;

/// <summary>An entry of a plan row's box: nothing, the original or a translation.</summary>
public sealed record PlanChoice(PlanLine Line, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One spoken language of the plan card: what is shown on top and below for its lines.</summary>
public sealed partial class PlanRuleRow : ObservableObject
{
    private readonly Action<PlanRuleRow> _changed;

    public PlanRuleRow(string language, IReadOnlyList<PlanChoice> topChoices, IReadOnlyList<PlanChoice> bottomChoices, LanguageRule rule, Action<PlanRuleRow> changed)
    {
        Language = language;
        TopChoices = topChoices;
        BottomChoices = bottomChoices;
        Top = topChoices.FirstOrDefault(c => c.Line == rule.Top) ?? topChoices[1];
        Bottom = bottomChoices.FirstOrDefault(c => c.Line == rule.Bottom) ?? bottomChoices[0];
        _changed = changed; // set last: the initial values are not edits
    }

    public string Language { get; }

    /// <summary>"KO · 한국어 konuşulan satırlar".</summary>
    public string Title => $"{LanguageCodes.Badge(Language)} · {LanguageCodes.DisplayName(Language)}";

    public IReadOnlyList<PlanChoice> TopChoices { get; }

    public IReadOnlyList<PlanChoice> BottomChoices { get; }

    [ObservableProperty]
    public partial PlanChoice Top { get; set; }

    [ObservableProperty]
    public partial PlanChoice Bottom { get; set; }

    public LanguageRule ToRule() => new() { Language = Language, Top = Top.Line, Bottom = Bottom.Line };

    partial void OnTopChanged(PlanChoice value) => _changed?.Invoke(this);

    partial void OnBottomChanged(PlanChoice value) => _changed?.Invoke(this);
}

/// <summary>The "Dile göre altyazı" card: a plan of which subtitles each spoken language gets.</summary>
public sealed partial class StyleEditorViewModel
{
    private static readonly string[] s_commonTargets = ["tr", "en", "ko"];

    [ObservableProperty]
    public partial bool PlanEnabled { get; set; }

    public ObservableCollection<PlanRuleRow> PlanRows { get; } = [];

    /// <summary>Translations the plan needs but the project lacks, e.g. "Korece satırlar için İngilizce çeviri yok (3 satır)".</summary>
    [ObservableProperty]
    public partial string? PlanMissingText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FillPlanTranslationsCommand))]
    public partial bool HasPlanMissing { get; private set; }

    /// <summary>Set by the main window: translates the lines the plan lacks (a paid AI call, started by the button).</summary>
    public Func<Task>? FillPlanTranslations { get; set; }

    public string PlanInfo => _activeTrack?.Kind == TrackKind.Translation
        ? "Plan, kaynak veya düzeltilmiş izde uygulanır; şu an bir çeviri izi seçili."
        : "Her konuşma dili için üstte ve altta ne gösterileceğini seçin. Alt satır ikinci dil stilini kullanır. Plan açıkken \"İki dilli\" ayarı kullanılmaz.";

    [RelayCommand(CanExecute = nameof(HasPlanMissing))]
    private async Task FillPlanTranslationsAsync()
    {
        if (FillPlanTranslations is { } fill)
        {
            await fill();
        }
    }

    private void LoadPlan(Project? project, SubtitleTrack? activeTrack)
    {
        PlanRows.Clear();
        var plan = project?.Presentation.Plan ?? new SubtitlePlan();
        PlanEnabled = plan.Enabled;
        if (project is null || activeTrack is null)
        {
            UpdatePlanMissing();
            return;
        }

        // Rows: the languages spoken in the active track, then languages that already have a rule.
        var spoken = activeTrack.SpokenLanguages().Select(LanguageCodes.Primary)
            .Concat(plan.Rules.Select(r => LanguageCodes.Primary(r.Language)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var targets = s_commonTargets
            .Concat(project.Tracks.Where(t => t.Kind == TrackKind.Translation).Select(t => LanguageCodes.Primary(t.Language)))
            .Concat(spoken)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var language in spoken)
        {
            var translations = targets.Where(t => t != language)
                .Select(t => new PlanChoice(PlanLine.TranslationTo(t), $"{LanguageName(t)} çeviri"))
                .ToList();
            List<PlanChoice> top = [new(PlanLine.None, "Gösterme"), new(PlanLine.Original, "Orijinal (konuşulan)"), .. translations];
            List<PlanChoice> bottom = [new(PlanLine.None, "Yok"), new(PlanLine.Original, "Orijinal (konuşulan)"), .. translations];
            PlanRows.Add(new PlanRuleRow(language, top, bottom, plan.RuleFor(language), OnPlanRowChanged));
        }

        UpdatePlanMissing();
        OnPropertyChanged(nameof(PlanInfo));
    }

    partial void OnPlanEnabledChanged(bool value)
    {
        if (!_loading)
        {
            WritePlan();
        }
    }

    private void OnPlanRowChanged(PlanRuleRow row)
    {
        if (!_loading)
        {
            WritePlan();
        }
    }

    private void WritePlan()
    {
        if (_project is not { } project)
        {
            return;
        }

        var plan = project.Presentation.Plan with { Enabled = PlanEnabled };
        foreach (var row in PlanRows)
        {
            plan = plan.With(row.ToRule());
        }

        project.Presentation = project.Presentation with { Plan = plan };
        UpdatePlanMissing();
        MarkAppliedCard();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recomputes which translations the plan lacks (call after a translation was added).</summary>
    public void UpdatePlanMissing()
    {
        var missing = _project is { } project && _activeTrack is { } track && SubtitlePlans.AppliesTo(project, track)
            ? SubtitlePlans.Resolve(project, track).Missing
            : [];
        HasPlanMissing = missing.Count > 0;
        PlanMissingText = missing.Count == 0
            ? null
            : string.Join("\n", missing.Select(m =>
                $"{LanguageName(m.SpokenLanguage)} satırlar için {LanguageName(m.TargetLanguage).ToLower(TurkishText.Culture)} çeviri yok ({m.SourceCueIds.Count} satır)."));
    }

    private static string LanguageName(string code) => code switch
    {
        "tr" => "Türkçe",
        "en" => "İngilizce",
        "ko" => "Korece",
        "de" => "Almanca",
        "fr" => "Fransızca",
        "es" => "İspanyolca",
        "ar" => "Arapça",
        "ru" => "Rusça",
        "ja" => "Japonca",
        "it" => "İtalyanca",
        _ => LanguageCodes.DisplayName(code),
    };
}
