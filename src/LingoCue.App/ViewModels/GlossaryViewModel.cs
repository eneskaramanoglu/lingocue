using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.Core.Projects;

namespace LingoCue.App.ViewModels;

public sealed partial class GlossaryRowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Term { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Preferred { get; set; }

    [ObservableProperty]
    public partial string? TargetLanguage { get; set; }

    [ObservableProperty]
    public partial GlossaryKind Kind { get; set; } = GlossaryKind.Term;

    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    public partial bool Locked { get; set; }

    [ObservableProperty]
    public partial bool Suggested { get; set; }

    public static GlossaryRowViewModel From(GlossaryEntry e) => new()
    {
        Term = e.Term,
        Preferred = e.Preferred,
        TargetLanguage = e.TargetLanguage,
        Kind = e.Kind,
        Note = e.Note,
        Locked = e.Locked,
        Suggested = e.Suggested,
    };

    public GlossaryEntry? ToEntry() => string.IsNullOrWhiteSpace(Term)
        ? null
        : new GlossaryEntry
        {
            Term = Term.Trim(),
            Preferred = string.IsNullOrWhiteSpace(Preferred) ? null : Preferred.Trim(),
            TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguage) ? null : TargetLanguage.Trim(),
            Kind = Kind,
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            Locked = Locked,
            Suggested = Suggested && !Locked,
        };
}

/// <summary>Edits the project glossary and context; <see cref="Apply"/> writes back.</summary>
public sealed partial class GlossaryViewModel : ViewModelBase
{
    private readonly Project _project;

    public GlossaryViewModel(Project project)
    {
        _project = project;
        Rows = [.. project.Glossary.Select(GlossaryRowViewModel.From)];
        Summary = project.Context.Summary;
        Tone = project.Context.Tone;
        Address = project.Context.Address ?? string.Empty;
        Speakers = string.Join(", ", project.Context.Speakers);
    }

    public ObservableCollection<GlossaryRowViewModel> Rows { get; }

    public IReadOnlyList<GlossaryKind> Kinds { get; } = Enum.GetValues<GlossaryKind>();

    public IReadOnlyList<string> Addresses { get; } = [string.Empty, "sen", "siz", "karışık"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial GlossaryRowViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string? Summary { get; set; }

    [ObservableProperty]
    public partial string? Tone { get; set; }

    [ObservableProperty]
    public partial string Address { get; set; }

    [ObservableProperty]
    public partial string Speakers { get; set; }

    public int SuggestionCount => Rows.Count(r => r.Suggested);

    private bool HasSelection => Selected is not null;

    [RelayCommand]
    private void Add()
    {
        var row = new GlossaryRowViewModel { Term = "yeni terim", Locked = true };
        Rows.Add(row);
        Selected = row;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (Selected is { } row)
        {
            Rows.Remove(row);
        }
    }

    /// <summary>Accepts every suggestion (they stop being marked as suggestions; locking stays a choice).</summary>
    [RelayCommand]
    private void AcceptSuggestions()
    {
        foreach (var row in Rows)
        {
            row.Suggested = false;
        }

        OnPropertyChanged(nameof(SuggestionCount));
    }

    /// <summary>Returns true when something changed.</summary>
    public bool Apply()
    {
        var entries = Rows.Select(r => r.ToEntry()).OfType<GlossaryEntry>().ToList();
        var context = _project.Context with
        {
            Summary = string.IsNullOrWhiteSpace(Summary) ? null : Summary.Trim(),
            Tone = string.IsNullOrWhiteSpace(Tone) ? null : Tone.Trim(),
            Address = string.IsNullOrWhiteSpace(Address) ? null : Address,
            Speakers = [.. Speakers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        };

        var changed = !entries.SequenceEqual(_project.Glossary) || context != _project.Context ||
                      !context.Speakers.SequenceEqual(_project.Context.Speakers);
        _project.Glossary.Clear();
        _project.Glossary.AddRange(entries);
        _project.Context = context;
        return changed;
    }
}

/// <summary>Values for the glossary kind editor (bound with x:Static).</summary>
public static class GlossaryKinds
{
    public static IReadOnlyList<GlossaryKind> All { get; } = Enum.GetValues<GlossaryKind>();
}
