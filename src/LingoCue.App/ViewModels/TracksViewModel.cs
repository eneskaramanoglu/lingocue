using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Services;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;

namespace LingoCue.App.ViewModels;

/// <summary>One row in the tracks window; name and language edit the track directly.</summary>
public sealed partial class TrackRowViewModel(SubtitleTrack track, Project project) : ObservableObject
{
    public SubtitleTrack Track { get; } = track;

    public string Name
    {
        get => Track.Name;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) ? Track.Name : value.Trim();
            if (name != Track.Name)
            {
                Track.Name = name;
                OnPropertyChanged();
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Language
    {
        get => Track.Language;
        set
        {
            var language = string.IsNullOrWhiteSpace(value) ? Track.Language : value.Trim();
            if (language != Track.Language)
            {
                Track.Language = language;
                OnPropertyChanged();
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string KindText => Track.Kind switch
    {
        TrackKind.Corrected => "Düzeltilmiş",
        TrackKind.Translation => "Çeviri",
        _ => "Kaynak",
    };

    public int LineCount => Track.Cues.Count;

    public string? SourceName => project.Tracks.FirstOrDefault(t => t.Id == Track.SourceTrackId)?.Name;

    public string Details => SourceName is { } source ? $"{KindText} · {LineCount} satır · kaynağı: {source}" : $"{KindText} · {LineCount} satır";

    internal event EventHandler? Edited;
}

/// <summary>
/// Track management: rename, language, order (used for exports), duplicate, delete, add an empty
/// track and pull timing from the source track. Every change applies to the project at once.
/// </summary>
public sealed partial class TracksViewModel : ViewModelBase
{
    private readonly Project _project;
    private readonly IDialogService _dialogs;

    public TracksViewModel(Project project, IDialogService dialogs)
    {
        _project = project;
        _dialogs = dialogs;
        Reload(project.ActiveTrack);
    }

    /// <summary>True once anything was changed (the caller marks the project dirty and refreshes).</summary>
    public bool Changed { get; private set; }

    public ObservableCollection<TrackRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(DuplicateCommand), nameof(DeleteCommand), nameof(SyncTimingCommand))]
    public partial TrackRowViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string? Message { get; private set; }

    private bool HasSelection => Selected is not null;

    private bool CanDelete => Selected is not null && Rows.Count > 1;

    private bool CanSync => Selected?.Track.SourceTrackId is { } id && _project.Tracks.Any(t => t.Id == id);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => Move(+1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var copy = TrackOperations.Duplicate(_project, Selected!.Track);
        Done(copy, $"\"{copy.Name}\" oluşturuldu.");
    }

    [RelayCommand]
    private void AddEmpty()
    {
        var track = TrackOperations.AddEmpty(_project, "Yeni iz", _project.SourceLanguage);
        Done(track, "Boş bir iz eklendi; satırları editörde ekleyebilirsiniz.");
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        var row = Selected!;
        var children = _project.Tracks.Count(t => t.SourceTrackId == row.Track.Id);
        var note = children > 0 ? $" Bu izden üretilmiş {children} iz kendi kaynağına bağlanmaya devam eder." : string.Empty;
        if (!await _dialogs.ConfirmAsync("İzi sil", $"\"{row.Name}\" ({row.LineCount} satır) silinecek. Bu işlem geri alınamaz.{note}", "Sil"))
        {
            return;
        }

        TrackOperations.Delete(_project, row.Track);
        Done(_project.ActiveTrack, $"\"{row.Name}\" silindi.");
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private void SyncTiming()
    {
        var (updated, skipped) = TrackOperations.SyncTimingFromSource(_project, Selected!.Track);
        var skippedText = skipped > 0 ? $"; {skipped} satırın kaynakta karşılığı yok, değişmedi" : string.Empty;
        Done(Selected!.Track, $"{updated} satırın zamanlaması \"{Selected!.SourceName}\" izinden alındı{skippedText}.");
    }

    private void Move(int delta)
    {
        TrackOperations.Move(_project, Selected!.Track, delta);
        Done(Selected!.Track, null);
    }

    private void Done(SubtitleTrack? select, string? message)
    {
        Changed = true;
        Message = message;
        Reload(select);
    }

    private void Reload(SubtitleTrack? select)
    {
        Rows.Clear();
        foreach (var track in _project.Tracks)
        {
            var row = new TrackRowViewModel(track, _project);
            row.Edited += (_, _) => Changed = true;
            Rows.Add(row);
        }

        Selected = Rows.FirstOrDefault(r => r.Track == select) ?? Rows.FirstOrDefault();
        DeleteCommand.NotifyCanExecuteChanged();
    }
}
