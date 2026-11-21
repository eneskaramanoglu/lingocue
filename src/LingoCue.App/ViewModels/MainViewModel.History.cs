using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Playback;
using LingoCue.App.Services;
using LingoCue.Core;
using LingoCue.Core.Fonts;
using LingoCue.Core.Formats;
using LingoCue.Core.Platform;
using LingoCue.Core.Projects;
using LingoCue.Core.Security;
using LingoCue.Core.Segmentation;
using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Time;
using LingoCue.Infrastructure.Security;
using LingoCue.Llm;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;
using LingoCue.Media;
using LingoCue.Media.Probing;
using LingoCue.Media.Rendering;
using LingoCue.Media.Tools;
using LingoCue.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.App.ViewModels;

/// <summary>Undo and redo.</summary>
public partial class MainViewModel
{

    private readonly ProjectHistory _history = new();
    private Avalonia.Threading.DispatcherTimer? _historyTimer;
    private string? _pendingKey;
    private bool _historyPending;

    /// <summary>Snapshots are taken once the edits pause (not on every keystroke).</summary>
    private void ScheduleHistoryRecord(string? coalesceKey)
    {
        if (Project is null)
        {
            return;
        }

        if (_historyPending && _pendingKey != coalesceKey)
        {
            FlushHistory(); // a different kind of edit: close the previous step first
        }

        _pendingKey = coalesceKey;
        _historyPending = true;
        _historyTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(400), Avalonia.Threading.DispatcherPriority.Background, (_, _) => FlushHistory());
        _historyTimer.Stop();
        _historyTimer.Start();
    }

    private void FlushHistory()
    {
        _historyTimer?.Stop();
        if (!_historyPending || Project is null)
        {
            return;
        }

        _historyPending = false;
        _history.Record(Project, _pendingKey);
        IsDirty = !_history.IsAtSavedState;
        OnPropertyChanged(nameof(DocumentName));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo => IsIdle && (_historyPending || _history.CanUndo);

    private bool CanRedo => IsIdle && !_historyPending && _history.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        FlushHistory();
        if (Project is not null && _history.Undo(Project) is { } previous)
        {
            ApplyHistoryState(previous, "Geri alındı.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (Project is not null && _history.Redo(Project) is { } next)
        {
            ApplyHistoryState(next, "Yinelendi.");
        }
    }

    /// <summary>Swaps in a restored project, keeping the video, path and (if possible) the selected line.</summary>
    private void ApplyHistoryState(Project restored, string status)
    {
        var selectedCue = Editor?.SelectedCue?.Model.Id;
        restored.VideoPath = Project?.VideoPath;
        Project = restored;
        restored.EnsureActiveTrack();
        RefreshTracks();
        if (selectedCue is { } id)
        {
            Editor?.SelectCue(id);
        }

        RefreshPreview(immediate: true);
        IsDirty = !_history.IsAtSavedState;
        OnPropertyChanged(nameof(DocumentName));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        StatusText = status;
    }
}
