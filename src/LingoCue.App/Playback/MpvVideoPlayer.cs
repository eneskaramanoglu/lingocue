using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HanumanInstitute.LibMpv;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace LingoCue.App.Playback;

/// <summary>
/// <see cref="IVideoPlayer"/> on top of libmpv. The <see cref="MpvContext"/> is created by the
/// <c>MpvView</c> control and handed over through <see cref="Attach"/>. mpv raises events on its own
/// thread; every state change is marshalled to the UI thread.
/// </summary>
public sealed partial class MpvVideoPlayer(ILogger<MpvVideoPlayer> logger) : ObservableObject, IVideoPlayer
{
    private MpvContext? _mpv;
    private string _fontsDirectory = string.Empty;
    private bool _hasSubtitle;
    private string? _pendingFile;
    private string? _pendingSubtitle;

    [ObservableProperty]
    public partial bool IsReady { get; private set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    public partial TimeSpan Position { get; private set; }

    [ObservableProperty]
    public partial TimeSpan Duration { get; private set; }

    public void Attach(MpvContext? mpv)
    {
        if (ReferenceEquals(_mpv, mpv))
        {
            return;
        }

        Detach();
        _mpv = mpv;
        if (mpv is null)
        {
            return;
        }

        // Editor-friendly defaults: stay on the last frame at the end, exact seeks, no mpv OSD/keys,
        // never auto-load subtitle files sitting next to the video (we load our own preview ASS).
        TrySetOption(mpv, "keep-open", "yes");
        TrySetOption(mpv, "hr-seek", "yes");
        TrySetOption(mpv, "sub-auto", "no");
        TrySetOption(mpv, "osd-level", "0");
        TrySetOption(mpv, "input-default-bindings", "no");
        TrySetOption(mpv, "hwdec", "auto-safe");
        if (_fontsDirectory.Length > 0)
        {
            TrySetOption(mpv, "sub-fonts-dir", _fontsDirectory);
        }

        mpv.TimePos.Changed += OnTimePosChanged;
        mpv.Duration.Changed += OnDurationChanged;
        mpv.Pause.Changed += OnPauseChanged;
        mpv.FileLoaded += OnFileLoaded;
        mpv.EndFile += OnEndFile;

        IsReady = true;
        LogAttached();

        if (_pendingFile is { } pending)
        {
            _pendingFile = null;
            _ = OpenAsync(pending);
        }
    }

    public async Task OpenAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (_mpv is null)
        {
            // The view may not have created its context yet; open as soon as it does.
            _pendingFile = filePath;
            return;
        }

        IsLoaded = false;
        _hasSubtitle = false;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        await _mpv.LoadFile(filePath).InvokeAsync();
        _mpv.Pause.Set(true);
        LogOpened(filePath);
    }

    public void Play()
    {
        if (_mpv is null || !IsLoaded)
        {
            return;
        }

        // At the very end, play restarts from the beginning like most players.
        if (Duration > TimeSpan.Zero && Position >= Duration - TimeSpan.FromMilliseconds(100))
        {
            Seek(TimeSpan.Zero);
        }

        _mpv.Pause.Set(false);
    }

    public void Pause() => _mpv?.Pause.Set(true);

    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Seek(TimeSpan position, bool exact = true)
    {
        if (_mpv is null || !IsLoaded)
        {
            return;
        }

        var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : (Duration > TimeSpan.Zero && position > Duration ? Duration : position);
        var mode = exact ? "absolute+exact" : "absolute+keyframes";
        RunCommand("seek", clamped.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), mode);
        Position = clamped;
    }

    public void SeekRelative(TimeSpan offset) => Seek(Position + offset);

    public void StepFrame(bool forward)
    {
        if (_mpv is null || !IsLoaded)
        {
            return;
        }

        // frame-step pauses playback, which is what an editor wants.
        RunCommand(forward ? "frame-step" : "frame-back-step");
    }

    public void LoadSubtitle(string path)
    {
        if (_mpv is null || !IsLoaded)
        {
            // Applied once the file has loaded (sub-add needs a loaded file).
            _pendingSubtitle = path;
            return;
        }

        if (_hasSubtitle)
        {
            RunCommand("sub-reload");
        }
        else
        {
            RunCommand("sub-add", path, "select");
            _hasSubtitle = true;
        }
    }

    public void SetFontsDirectory(string? directory)
    {
        // Always applied (even for the same folder): a font file may have just been added to it.
        var value = directory ?? string.Empty;
        _fontsDirectory = value;
        if (_mpv is null)
        {
            return;
        }

        // libass reads the fonts folder when a subtitle decoder starts, so reload the preview track.
        TrySetOption(_mpv, "sub-fonts-dir", value);
        if (_hasSubtitle)
        {
            RunCommand("sub-reload");
        }
    }

    public void Close()
    {
        _pendingFile = null;
        _pendingSubtitle = null;
        if (_mpv is not null)
        {
            RunCommand("stop");
        }

        IsLoaded = false;
        IsPlaying = false;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
    }

    private void Detach()
    {
        if (_mpv is null)
        {
            return;
        }

        _mpv.TimePos.Changed -= OnTimePosChanged;
        _mpv.Duration.Changed -= OnDurationChanged;
        _mpv.Pause.Changed -= OnPauseChanged;
        _mpv.FileLoaded -= OnFileLoaded;
        _mpv.EndFile -= OnEndFile;
        _mpv = null;
        IsReady = false;
    }

    private void RunCommand(params object?[] args)
    {
        try
        {
            _mpv?.RunCommand(null, args);
        }
        catch (MpvException ex)
        {
            LogCommandFailed(ex, string.Join(' ', args));
        }
    }

    private void TrySetOption(MpvContext mpv, string name, string value)
    {
        try
        {
            mpv.SetOptionString(name, value);
        }
        catch (MpvException ex)
        {
            LogOptionFailed(ex, name, value);
        }
    }

    private void OnTimePosChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
        => OnUi(() => Position = TimeSpan.FromSeconds(e.NewValue ?? 0));

    private void OnDurationChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
        => OnUi(() => Duration = TimeSpan.FromSeconds(e.NewValue ?? 0));

    private void OnPauseChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e)
        => OnUi(() => IsPlaying = IsLoaded && e.NewValue == false);

    private void OnFileLoaded(object? sender, EventArgs e)
        => OnUi(() =>
        {
            IsLoaded = true;
            Duration = TimeSpan.FromSeconds(_mpv?.Duration.Get() ?? 0);
            if (_pendingSubtitle is { } subtitle)
            {
                _pendingSubtitle = null;
                LoadSubtitle(subtitle);
            }
        });

    private void OnEndFile(object? sender, MpvEndFileEventArgs e)
        => OnUi(() =>
        {
            IsLoaded = false;
            IsPlaying = false;
        });

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "mpv context attached")]
    private partial void LogAttached();

    [LoggerMessage(Level = LogLevel.Information, Message = "Opened {File}")]
    private partial void LogOpened(string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "mpv command failed: {Command}")]
    private partial void LogCommandFailed(Exception ex, string command);

    [LoggerMessage(Level = LogLevel.Warning, Message = "mpv option {Name}={Value} rejected")]
    private partial void LogOptionFailed(Exception ex, string name, string value);
}
