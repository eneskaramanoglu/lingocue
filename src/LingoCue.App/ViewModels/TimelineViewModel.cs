using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.App.Playback;
using LingoCue.Core.Subtitles;
using LingoCue.Media.Audio;
using LingoCue.Media.Tools;
using Microsoft.Extensions.Logging;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;

namespace LingoCue.App.ViewModels;

/// <summary>
/// What the timeline draws (waveform, cue blocks of the active track, playhead) and the actions it
/// can take (seek, select, apply a drag). The control owns zoom, scroll and hit-testing.
/// </summary>
public sealed partial class TimelineViewModel : ViewModelBase, IDisposable
{
    private readonly IVideoPlayer _player;
    private readonly IWaveformService _waveforms;
    private readonly ILogger<TimelineViewModel> _logger;
    private CancellationTokenSource? _waveformCts;

    public TimelineViewModel(IVideoPlayer player, SubtitleEditorViewModel editor, IWaveformService waveforms, ILogger<TimelineViewModel> logger)
    {
        _player = player;
        Editor = editor;
        _waveforms = waveforms;
        _logger = logger;
        _player.PropertyChanged += OnPlayerPropertyChanged;
        editor.Edited += (_, _) => Redraw();
        editor.RowsChanged += (_, _) => Redraw();
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SubtitleEditorViewModel.SelectedCue))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Redraw();
            }
        };
    }

    public SubtitleEditorViewModel Editor { get; }

    /// <summary>Anything drawn changed; the control repaints.</summary>
    public event EventHandler? Invalidated;

    /// <summary>The selected line changed; the control scrolls it into view.</summary>
    public event EventHandler? SelectionChanged;

    [ObservableProperty]
    public partial Waveform? Waveform { get; private set; }

    [ObservableProperty]
    public partial bool IsWaveformLoading { get; private set; }

    public bool IsLoaded => _player.IsLoaded;

    public bool IsPlaying => _player.IsPlaying;

    public TimeSpan Position => _player.Position;

    public TimeSpan Duration => _player.Duration > TimeSpan.Zero ? _player.Duration : Waveform?.Duration ?? TimeSpan.Zero;

    public SubtitleTrack? Track => Editor.Track;

    public IReadOnlyList<CueViewModel> Cues => Editor.Cues;

    public CueViewModel? SelectedCue => Editor.SelectedCue;

    public bool FollowPlayback => Editor.FollowPlayback;

    /// <param name="exact">False while dragging: a fast keyframe seek is enough.</param>
    public void Seek(TimeSpan position, bool exact = true)
    {
        if (_player.IsLoaded)
        {
            _player.Seek(position < TimeSpan.Zero ? TimeSpan.Zero : position, exact);
        }
    }

    /// <summary>Selects a line (the editor seeks the video to its start).</summary>
    public void Select(CueViewModel row) => Editor.SelectedCue = row;

    public (TimeSpan Start, TimeSpan End) Clamp(CueViewModel row, DragHandle handle, TimeSpan delta)
        => Track is { } track ? CueEditing.ClampDrag(track, row.Model, handle, delta, Duration) : (row.Start, row.End);

    public void Apply(CueViewModel row, TimeSpan start, TimeSpan end) => Editor.ApplyTimes(row, start, end);

    /// <summary>Starts loading the waveform of a newly opened video (from the cache when possible).</summary>
    public async Task LoadWaveformAsync(string mediaPath)
    {
        Clear();
        var cts = new CancellationTokenSource();
        _waveformCts = cts;
        IsWaveformLoading = true;
        try
        {
            var waveform = await Task.Run(() => _waveforms.GetAsync(mediaPath, cancellationToken: cts.Token), cts.Token);
            if (!cts.IsCancellationRequested)
            {
                Waveform = waveform;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is AudioExtractionException or MediaToolNotFoundException or IOException or UnauthorizedAccessException)
        {
            // The timeline still works without the waveform; the reason goes to the log only.
            LogWaveformFailed(ex, Path.GetFileName(mediaPath));
        }
        finally
        {
            if (_waveformCts == cts)
            {
                IsWaveformLoading = false;
            }
        }
    }

    /// <summary>Forgets the waveform (another video, or the project closed).</summary>
    public void Clear()
    {
        _waveformCts?.Cancel();
        _waveformCts?.Dispose();
        _waveformCts = null;
        Waveform = null;
        IsWaveformLoading = false;
    }

    public void Dispose()
    {
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        _waveformCts?.Cancel();
        _waveformCts?.Dispose();
    }

    partial void OnWaveformChanged(Waveform? value) => Redraw();

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e) => Redraw();

    private void Redraw() => Invalidated?.Invoke(this, EventArgs.Empty);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Waveform could not be created for {File}")]
    private partial void LogWaveformFailed(Exception exception, string file);
}
