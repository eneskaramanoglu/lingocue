using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Playback;
using LingoCue.App.Services;
using LingoCue.Core;
using LingoCue.Core.Platform;
using LingoCue.Core.Time;
using LingoCue.Media;
using LingoCue.Media.Probing;
using LingoCue.Media.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IThemeService? _themeService;
    private readonly IFilePickerService? _filePicker;
    private readonly IMediaProbe? _probe;
    private readonly IVideoPlayer? _player;
    private readonly IPlatformInfo? _platform;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        IThemeService themeService,
        IFilePickerService filePicker,
        IMediaProbe probe,
        IVideoPlayer player,
        PlayerViewModel playerViewModel,
        IPlatformInfo platform,
        ILogger<MainViewModel> logger)
    {
        _themeService = themeService;
        _filePicker = filePicker;
        _probe = probe;
        _player = player;
        _platform = platform;
        _logger = logger;
        Player = playerViewModel;
        PlatformText = platform.RuntimeIdentifier;
        SelectedTheme = themeService.Current;
    }

    // Designer / previewer constructor.
    private MainViewModel()
    {
        _logger = NullLogger<MainViewModel>.Instance;
        PlatformText = "design";
    }

    public static MainViewModel CreateDesignInstance() => new();

    public PlayerViewModel? Player { get; }

    public string Title => CurrentMedia is null ? AppInfo.Name : $"{Path.GetFileName(CurrentMedia.FilePath)} — {AppInfo.Name}";

    public string VersionText { get; } = $"v{AppInfo.Version}";

    public string PlatformText { get; }

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    public partial AppTheme SelectedTheme { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Hazır. Başlamak için bir video açın veya sürükleyip bırakın.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    public partial MediaInfo? CurrentMedia { get; private set; }

    public bool HasMedia => CurrentMedia is not null;

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    // Long-running job state (wired up from M3 on).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelJobCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenVideoCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    public bool IsIdle => !IsBusy;

    partial void OnSelectedThemeChanged(AppTheme value) => _themeService?.Apply(value);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenVideo()
    {
        var path = _filePicker is null ? null : await _filePicker.PickVideoAsync();
        if (path is not null)
        {
            await OpenFileAsync(path);
        }
    }

    /// <summary>Opens a video from the picker or a drag-and-drop.</summary>
    public async Task OpenFileAsync(string path, string? subtitlePath = null)
    {
        if (_probe is null || _player is null || IsBusy)
        {
            return;
        }

        ErrorText = null;
        if (!SupportedMedia.IsSupportedVideo(path))
        {
            ErrorText = $"Desteklenmeyen dosya türü: {Path.GetExtension(path)}. Desteklenenler: {string.Join(", ", SupportedMedia.VideoExtensions)}";
            return;
        }

        IsBusy = true;
        StatusText = $"Açılıyor: {Path.GetFileName(path)}";
        try
        {
            var info = await _probe.ProbeAsync(path);
            if (info.Video is null)
            {
                ErrorText = "Bu dosyada video akışı yok.";
                StatusText = "Hazır";
                return;
            }

            await _player.OpenAsync(path);
            if (subtitlePath is not null)
            {
                _player.LoadSubtitle(Path.GetFullPath(subtitlePath));
            }

            CurrentMedia = info;
            StatusText = Describe(info);
        }
        catch (MediaToolNotFoundException)
        {
            ErrorText = FfmpegMissingMessage();
            StatusText = "FFmpeg bulunamadı";
        }
        catch (Exception ex) when (ex is MediaProbeException or FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            LogOpenFailed(ex, path);
            ErrorText = $"Video açılamadı: {ex.Message}";
            StatusText = "Hazır";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DismissError() => ErrorText = null;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelJob()
    {
        // Each job will own a CancellationTokenSource; nothing cancellable runs yet.
        StatusText = "İptal edildi";
        IsBusy = false;
    }

    private static string Describe(MediaInfo info)
    {
        var video = info.Video!;
        var audio = info.HasAudio ? $"{info.AudioStreams.Count} ses izi" : "ses yok";
        return string.Create(
            CultureInfo.GetCultureInfo("tr-TR"),
            $"{Path.GetFileName(info.FilePath)} · {video.DisplayWidth}×{video.DisplayHeight} · {video.FrameRate:0.##} fps · {video.Codec} · {audio} · {Timecode.Format(info.Duration)}");
    }

    private string FfmpegMissingMessage() => _platform?.Platform switch
    {
        OsPlatform.Windows => "FFmpeg bulunamadı. Kurmak için PowerShell'de çalıştırın: winget install Gyan.FFmpeg — sonra uygulamayı yeniden başlatın.",
        OsPlatform.MacOS => "FFmpeg bulunamadı. Kurmak için Terminal'de çalıştırın: brew install ffmpeg — sonra uygulamayı yeniden başlatın.",
        _ => "FFmpeg bulunamadı. Paket yöneticinizle ffmpeg kurup uygulamayı yeniden başlatın.",
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open {Path}")]
    private partial void LogOpenFailed(Exception ex, string path);
}
