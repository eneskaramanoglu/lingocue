using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Time;

namespace LingoCue.App.ViewModels;

/// <summary>One row of the QA list.</summary>
public sealed record QaIssueItem(QaIssue Issue)
{
    public string NumberText => $"#{Issue.Number}";

    public string TimeText => Timecode.Format(Issue.Start);

    public bool IsError => Issue.Severity == QaSeverity.Error;

    public string Glyph => IsError ? "✕" : "!";
}

/// <summary>
/// The "Kontrol" tab: every rule problem of the active track (reading speed, line length, overlaps,
/// tiny gaps) and two quick fixes. Updates itself after edits; each fix is one undo step.
/// </summary>
public sealed partial class QualityViewModel : ViewModelBase
{
    private readonly SubtitleEditorViewModel _editor;
    private bool _refreshQueued;

    public QualityViewModel(SubtitleEditorViewModel editor)
    {
        _editor = editor;
        editor.Edited += (_, _) => QueueRefresh();
        editor.RowsChanged += (_, _) => QueueRefresh();
    }

    public ObservableCollection<QaIssueItem> Issues { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = "Altyazı yok";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IssueCount), nameof(HasIssues))]
    public partial int ErrorCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IssueCount), nameof(HasIssues))]
    public partial int WarningCount { get; private set; }

    /// <summary>Lines without any problem (the green tile).</summary>
    [ObservableProperty]
    public partial int CleanCount { get; private set; }

    public int IssueCount => ErrorCount + WarningCount;

    public bool HasIssues => IssueCount > 0;

    [ObservableProperty]
    public partial string? FixStatus { get; private set; }

    [ObservableProperty]
    public partial QaIssueItem? SelectedIssue { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixOverlapsCommand))]
    public partial bool HasOverlaps { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtendForReadingSpeedCommand))]
    public partial bool HasFastLines { get; private set; }

    /// <summary>Jumps to the line of the picked problem (selects it; the video seeks there).</summary>
    partial void OnSelectedIssueChanged(QaIssueItem? value)
    {
        if (value is not null && _editor.Cues.FirstOrDefault(c => c.Model.Id == value.Issue.CueId) is { } row)
        {
            _editor.SelectedCue = row;
        }
    }

    [RelayCommand(CanExecute = nameof(HasOverlaps))]
    private void FixOverlaps()
    {
        if (_editor.Track is not { } track)
        {
            return;
        }

        var count = QualityCheck.FixOverlaps(track);
        Applied(count, "çakışma giderildi");
    }

    [RelayCommand(CanExecute = nameof(HasFastLines))]
    private void ExtendForReadingSpeed()
    {
        if (_editor.Track is not { } track)
        {
            return;
        }

        var count = QualityCheck.ExtendForReadingSpeed(track, _editor.Rules);
        Applied(count, "satırın süresi uzatıldı");
    }

    private void Applied(int count, string what)
    {
        if (count > 0)
        {
            _editor.Rebuild(_editor.SelectedCue?.Model.Id);
            _editor.NotifyTrackEdited();
        }

        FixStatus = count > 0 ? $"{count} {what} (Ctrl+Z ile geri alınır)" : "Değiştirilebilecek satır yok (komşu satırlar yer bırakmıyor)";
        Refresh();
    }

    // Typing raises an edit per key press; check once per dispatcher round instead.
    private void QueueRefresh()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(Refresh, Avalonia.Threading.DispatcherPriority.Background);
    }

    public void Refresh()
    {
        _refreshQueued = false;
        Issues.Clear();
        if (_editor.Track is not { } track)
        {
            Summary = "Altyazı yok";
            HasOverlaps = HasFastLines = false;
            ErrorCount = WarningCount = CleanCount = 0;
            return;
        }

        var issues = QualityCheck.Check(track, _editor.Rules);
        foreach (var issue in issues)
        {
            Issues.Add(new QaIssueItem(issue));
        }

        var errors = issues.Count(i => i.Severity == QaSeverity.Error);
        Summary = issues.Count == 0 ? "Sorun yok" : $"{errors} hata · {issues.Count - errors} uyarı";
        ErrorCount = errors;
        WarningCount = issues.Count - errors;
        CleanCount = track.Cues.Count - issues.Select(i => i.CueId).Distinct().Count();
        HasOverlaps = issues.Any(i => i.Kind == "Overlap");
        HasFastLines = issues.Any(i => i.Kind is nameof(CueIssues.ReadingSpeedTooHigh) or nameof(CueIssues.ReadingSpeedHigh));
    }
}
