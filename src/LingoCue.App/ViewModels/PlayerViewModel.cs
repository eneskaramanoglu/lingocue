using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.LibMpv;
using LingoCue.App.Playback;
using LingoCue.Core.Time;

namespace LingoCue.App.ViewModels;

/// <summary>Transport controls bound to <see cref="IVideoPlayer"/>.</summary>
public sealed partial class PlayerViewModel : ViewModelBase
{
    private static readonly TimeSpan s_jump = TimeSpan.FromSeconds(5);
    private readonly IVideoPlayer _player;
    private readonly MpvVideoPlayer? _mpvPlayer;
    private bool _syncingFromPlayer;

    public PlayerViewModel(IVideoPlayer player)
    {
        _player = player;
        _mpvPlayer = player as MpvVideoPlayer;
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    /// <summary>Set by the <c>MpvView</c> control (OneWayToSource) once it has created its mpv context.</summary>
    public MpvContext? Mpv
    {
        get;
        set
        {
            field = value;
            _mpvPlayer?.Attach(value);
        }
    }

    public bool IsLoaded => _player.IsLoaded;

    public bool IsPlaying => _player.IsPlaying;

    public double DurationSeconds => Math.Max(_player.Duration.TotalSeconds, 0.001);

    public string PositionText => Timecode.Format(_player.Position);

    public string DurationText => Timecode.Format(_player.Duration);

    /// <summary>Two-way bound to the seek slider; user changes seek the player.</summary>
    public double PositionSeconds
    {
        get => _player.Position.TotalSeconds;
        set
        {
            if (_syncingFromPlayer || Math.Abs(value - _player.Position.TotalSeconds) < 0.01)
            {
                return;
            }

            _player.Seek(TimeSpan.FromSeconds(value));
        }
    }

    [RelayCommand(CanExecute = nameof(IsLoaded))]
    private void TogglePlayPause() => _player.TogglePlayPause();

    [RelayCommand(CanExecute = nameof(IsLoaded))]
    private void JumpBack() => _player.SeekRelative(-s_jump);

    [RelayCommand(CanExecute = nameof(IsLoaded))]
    private void JumpForward() => _player.SeekRelative(s_jump);

    [RelayCommand(CanExecute = nameof(IsLoaded))]
    private void FrameBack() => _player.StepFrame(forward: false);

    [RelayCommand(CanExecute = nameof(IsLoaded))]
    private void FrameForward() => _player.StepFrame(forward: true);

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IVideoPlayer.Position):
                _syncingFromPlayer = true;
                try
                {
                    OnPropertyChanged(nameof(PositionSeconds));
                    OnPropertyChanged(nameof(PositionText));
                }
                finally
                {
                    _syncingFromPlayer = false;
                }

                break;

            case nameof(IVideoPlayer.Duration):
                OnPropertyChanged(nameof(DurationSeconds));
                OnPropertyChanged(nameof(DurationText));
                break;

            case nameof(IVideoPlayer.IsPlaying):
                OnPropertyChanged(nameof(IsPlaying));
                break;

            case nameof(IVideoPlayer.IsLoaded):
                OnPropertyChanged(nameof(IsLoaded));
                TogglePlayPauseCommand.NotifyCanExecuteChanged();
                JumpBackCommand.NotifyCanExecuteChanged();
                JumpForwardCommand.NotifyCanExecuteChanged();
                FrameBackCommand.NotifyCanExecuteChanged();
                FrameForwardCommand.NotifyCanExecuteChanged();
                break;
        }
    }
}
