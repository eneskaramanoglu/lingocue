using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Playback;
using LingoCue.Core.Subtitles;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;

namespace LingoCue.App.ViewModels;

/// <summary>
/// The cue list of the active track plus the edit panel. Keeps the list sorted by start time,
/// follows the playhead and seeks the player when the user picks a row.
/// </summary>
public sealed partial class SubtitleEditorViewModel : ViewModelBase
{
    private static readonly TimeSpan s_defaultCueLength = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_minGap = TimeSpan.FromMilliseconds(40);
    private readonly IVideoPlayer _player;
    private ReadingRules _rules = ReadingRules.Default;
    private SubtitleTrack? _track;
    private bool _selectingFromPlayback;
    private CueViewModel? _current;

    public SubtitleEditorViewModel(IVideoPlayer player)
    {
        _player = player;
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    /// <summary>Raised after any edit (text, time, add, delete).</summary>
    public event EventHandler? Edited;

    public ObservableCollection<CueViewModel> Cues { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCueCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetStartToPlayheadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetEndToPlayheadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertToOriginalCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkReviewedCommand))]
    public partial CueViewModel? SelectedCue { get; set; }

    /// <summary>While playing, select the cue under the playhead.</summary>
    [ObservableProperty]
    public partial bool FollowPlayback { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCueCommand))]
    public partial bool HasTrack { get; private set; }

    public bool HasSelection => SelectedCue is not null;

    public string Summary => Cues.Count == 0
        ? "Altyazı yok"
        : $"{Cues.Count} satır · {Cues.Count(c => c.HasError)} hata · {Cues.Count(c => c.HasWarning)} uyarı" +
          (!HasSourceTrack ? string.Empty : IsTranslation ? $" · {ReviewCount} kontrol bekliyor" : $" · {ChangedCount} değişti · {ReviewCount} kontrol bekliyor");

    /// <summary>Reading rules for a track language (English allows a higher reading speed than Turkish).</summary>
    public Func<string?, ReadingRules> RulesFor { get; set; } = _ => ReadingRules.Default;

    /// <summary>Re-evaluates the rows, e.g. after the reading rules changed in the settings.</summary>
    public void Refresh() => Load(_track, _sourceTrack);

    /// <summary>The track this one was derived from (correction/translation); its cue texts are shown as originals.</summary>
    private SubtitleTrack? _sourceTrack;

    /// <summary>Show only lines changed by a pass or flagged for review.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleCues))]
    [NotifyCanExecuteChangedFor(nameof(AddCueCommand))]
    public partial bool ShowChangedOnly { get; set; }

    [ObservableProperty]
    public partial bool HasSourceTrack { get; private set; }

    /// <summary>The loaded track is a translation (its lines differ from the source by nature).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterLabel), nameof(FilterTip))]
    public partial bool IsTranslation { get; private set; }

    public string FilterLabel => IsTranslation ? "Kontrol bekleyenler" : "Değişenler";

    public string FilterTip => IsTranslation
        ? "Yalnızca kontrol bekleyen ya da çevrilemeyen satırları göster"
        : "Yalnızca yapay zekânın değiştirdiği ya da kontrol bekleyen satırları göster";

    /// <summary>The grid's rows: the live list, or a snapshot of the interesting lines while filtering.</summary>
    public IEnumerable<CueViewModel> VisibleCues => ShowChangedOnly ? [.. Cues.Where(c => c.IsOfInterest)] : Cues;

    public int ChangedCount => Cues.Count(c => c.IsChanged);

    public int ReviewCount => Cues.Count(c => c.NeedsReview || c.LlmFailed);

    private bool CanAddCue => HasTrack && !ShowChangedOnly;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RevertToOriginal()
    {
        if (SelectedCue is { OriginalText: { } original } vm)
        {
            vm.Text = original;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MarkReviewed() => SelectedCue?.ClearMarks();

    public void Load(SubtitleTrack? track, SubtitleTrack? sourceTrack = null)
    {
        _rules = RulesFor(track?.Language);
        _sourceTrack = track is { SourceTrackId: { } sourceId } && sourceTrack?.Id == sourceId ? sourceTrack : null;
        HasSourceTrack = _sourceTrack is not null;
        IsTranslation = track?.Kind == TrackKind.Translation;
        if (!HasSourceTrack)
        {
            ShowChangedOnly = false;
        }

        var originals = _sourceTrack?.Cues.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Text);
        foreach (var vm in Cues)
        {
            Detach(vm);
        }

        Cues.Clear();
        SelectedCue = null;
        _current = null;
        _track = track;
        HasTrack = track is not null;
        if (track is null)
        {
            OnPropertyChanged(nameof(Summary));
            return;
        }

        track.Sort();
        foreach (var cue in track.Cues)
        {
            var original = originals is not null && cue.SourceCueId is { } sourceCueId && originals.TryGetValue(sourceCueId, out var text) ? text : null;
            Cues.Add(Attach(new CueViewModel(cue, _rules) { OriginalText = original, CompareToOriginal = !IsTranslation }));
        }

        Renumber();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(VisibleCues));
        UpdateCurrent(_player.Position);
    }

    [RelayCommand(CanExecute = nameof(CanAddCue))]
    private void AddCue()
    {
        if (_track is null)
        {
            return;
        }

        // Start at the playhead (or right after the selected cue without a video); if a cue is
        // already showing there, start right after it so the new cue never overlaps.
        var start = _player.IsLoaded ? _player.Position : (SelectedCue?.End + s_minGap ?? TimeSpan.Zero);
        if (_track.CueAt(start) is { } covering)
        {
            start = covering.End + s_minGap;
        }

        var next = _track.Cues.Where(c => c.Start > start).MinBy(c => c.Start);
        var end = start + s_defaultCueLength;
        if (next is not null && end > next.Start - s_minGap)
        {
            end = next.Start - s_minGap;
        }

        if (end <= start)
        {
            end = start + TimeSpan.FromMilliseconds(500);
        }

        var cue = new Cue(_track.NextCueId(), start, end, string.Empty);
        var index = InsertIndex(start);
        _track.Cues.Insert(index, cue);
        var vm = Attach(new CueViewModel(cue, _rules));
        Cues.Insert(index, vm);
        Renumber();
        SelectedCue = vm;
        RaiseEdited();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteCue()
    {
        if (_track is null || SelectedCue is not { } vm)
        {
            return;
        }

        var index = Cues.IndexOf(vm);
        Detach(vm);
        _track.Cues.Remove(vm.Model);
        Cues.Remove(vm);
        Renumber();
        SelectedCue = Cues.Count == 0 ? null : Cues[Math.Min(index, Cues.Count - 1)];
        RaiseEdited();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetStartToPlayhead() => SelectedCue?.SetStart(_player.Position);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetEndToPlayhead() => SelectedCue?.SetEnd(_player.Position);

    partial void OnSelectedCueChanged(CueViewModel? value)
    {
        // A row picked by the user jumps the video there; one picked by playback must not seek.
        if (value is not null && !_selectingFromPlayback && _player.IsLoaded)
        {
            _player.Seek(value.Start);
        }
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IVideoPlayer.Position))
        {
            UpdateCurrent(_player.Position);
        }
    }

    private void UpdateCurrent(TimeSpan position)
    {
        var current = _track?.CueAt(position) is { } cue ? Cues.FirstOrDefault(c => c.Model == cue) : null;
        if (current == _current)
        {
            return;
        }

        if (_current is not null)
        {
            _current.IsCurrent = false;
        }

        _current = current;
        if (current is null)
        {
            return;
        }

        current.IsCurrent = true;
        if (FollowPlayback && _player.IsPlaying && SelectedCue != current)
        {
            _selectingFromPlayback = true;
            try
            {
                SelectedCue = current;
            }
            finally
            {
                _selectingFromPlayback = false;
            }
        }
    }

    private CueViewModel Attach(CueViewModel vm)
    {
        vm.Changed += OnCueChanged;
        vm.StartChanged += OnCueStartChanged;
        return vm;
    }

    private void Detach(CueViewModel vm)
    {
        vm.Changed -= OnCueChanged;
        vm.StartChanged -= OnCueStartChanged;
    }

    private void OnCueChanged(object? sender, EventArgs e) => RaiseEdited();

    // Keep list and model ordered by start time after a start edit.
    private void OnCueStartChanged(object? sender, EventArgs e)
    {
        if (_track is null || sender is not CueViewModel vm)
        {
            return;
        }

        var oldIndex = Cues.IndexOf(vm);
        _track.Cues.Remove(vm.Model);
        var newIndex = InsertIndex(vm.Start);
        _track.Cues.Insert(newIndex, vm.Model);
        if (newIndex != oldIndex)
        {
            Cues.Move(oldIndex, newIndex);
            Renumber();
        }
    }

    private int InsertIndex(TimeSpan start)
    {
        var cues = _track!.Cues;
        var index = 0;
        while (index < cues.Count && cues[index].Start <= start)
        {
            index++;
        }

        return index;
    }

    private void Renumber()
    {
        for (var i = 0; i < Cues.Count; i++)
        {
            Cues[i].Number = i + 1;
        }
    }

    private void RaiseEdited()
    {
        OnPropertyChanged(nameof(Summary));
        Edited?.Invoke(this, EventArgs.Empty);
    }
}
