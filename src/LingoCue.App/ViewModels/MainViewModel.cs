using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Playback;
using LingoCue.App.Services;
using LingoCue.Core;
using LingoCue.Core.Formats;
using LingoCue.Core.Platform;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
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
    private readonly IFilePickerService? _files;
    private readonly IDialogService? _dialogs;
    private readonly IMediaProbe? _probe;
    private readonly IVideoPlayer? _player;
    private readonly SubtitlePreviewService? _preview;
    private readonly IPlatformInfo? _platform;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        IThemeService themeService,
        IFilePickerService files,
        IDialogService dialogs,
        IMediaProbe probe,
        IVideoPlayer player,
        SubtitlePreviewService preview,
        PlayerViewModel playerViewModel,
        SubtitleEditorViewModel editor,
        IPlatformInfo platform,
        ILogger<MainViewModel> logger)
    {
        _themeService = themeService;
        _files = files;
        _dialogs = dialogs;
        _probe = probe;
        _player = player;
        _preview = preview;
        _platform = platform;
        _logger = logger;
        Player = playerViewModel;
        Editor = editor;
        Editor.Edited += (_, _) => OnEdited();
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

    public SubtitleEditorViewModel? Editor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand), nameof(SaveProjectAsCommand), nameof(ExportSubtitleCommand))]
    public partial Project? Project { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string? ProjectPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsDirty { get; private set; }

    public bool HasProject => Project is not null;

    public string DocumentName => ProjectPath is not null
        ? Path.GetFileName(ProjectPath)
        : Project?.VideoPath is { } video ? Path.GetFileNameWithoutExtension(video) + AppInfo.ProjectFileExtension : "Adsız proje";

    public string Title => Project is null ? AppInfo.Name : $"{(IsDirty ? "• " : string.Empty)}{DocumentName} — {AppInfo.Name}";

    public string VersionText { get; } = $"v{AppInfo.Version}";

    public string PlatformText { get; }

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    public partial AppTheme SelectedTheme { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Hazır. Başlamak için bir video ya da proje açın veya sürükleyip bırakın.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    public partial MediaInfo? CurrentMedia { get; private set; }

    public bool HasMedia => CurrentMedia is not null;

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    // Long-running job state (wired up from M3 on).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelJobCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenVideoCommand), nameof(OpenProjectCommand), nameof(ImportSubtitleCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    public bool IsIdle => !IsBusy;

    partial void OnSelectedThemeChanged(AppTheme value) => _themeService?.Apply(value);

    // ---------------------------------------------------------------- open

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenVideo()
    {
        if (_files is not null && await _files.PickVideoAsync() is { } path)
        {
            await OpenFileAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenProject()
    {
        if (_files is not null && await _files.PickProjectAsync() is { } path)
        {
            await OpenFileAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ImportSubtitle()
    {
        if (_files is not null && await _files.PickSubtitleAsync() is { } path)
        {
            await OpenFileAsync(path);
        }
    }

    /// <summary>Opens a project, a subtitle file (import) or a video, depending on the extension.</summary>
    public async Task OpenFileAsync(string path)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorText = null;
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, AppInfo.ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            await OpenProjectFileAsync(path);
        }
        else if (SubtitleFormats.ForExtension(extension) is not null)
        {
            await ImportSubtitleFileAsync(path);
        }
        else if (SupportedMedia.IsSupportedVideo(path))
        {
            await OpenVideoFileAsync(path);
        }
        else
        {
            ErrorText = $"Desteklenmeyen dosya türü: {extension}. Video ({string.Join(", ", SupportedMedia.VideoExtensions)}), " +
                        $"altyazı ({string.Join(", ", SubtitleFormats.AllExtensions)}) veya {AppInfo.ProjectFileExtension} projesi açılabilir.";
        }
    }

    private async Task OpenVideoFileAsync(string path)
    {
        // A project without a video (subtitles imported first) adopts it; otherwise a new project starts.
        var adopt = Project is { VideoPath: null };
        if (!adopt && !await ConfirmDiscardChangesAsync())
        {
            return;
        }

        await RunBusyAsync($"Açılıyor: {Path.GetFileName(path)}", async () =>
        {
            if (!await LoadVideoAsync(path))
            {
                return;
            }

            if (adopt)
            {
                Project!.VideoPath = path;
                MarkDirty();
            }
            else
            {
                SetProject(new Project { VideoPath = path }, projectPath: null);
            }

            RefreshPreview(immediate: true);
            StatusText = Describe(CurrentMedia!);
        });
    }

    private async Task OpenProjectFileAsync(string path)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }

        await RunBusyAsync($"Proje açılıyor: {Path.GetFileName(path)}", async () =>
        {
            Project project;
            try
            {
                project = await ProjectSerializer.LoadAsync(path);
            }
            catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException)
            {
                LogFailed(ex, path);
                ErrorText = $"Proje açılamadı: {ex.Message}";
                return;
            }

            SetProject(project, path);
            if (project.VideoPath is { } video)
            {
                if (File.Exists(video))
                {
                    await LoadVideoAsync(video);
                }
                else
                {
                    ErrorText = $"Projenin videosu bulunamadı: {video}. Video Aç ile yeni konumunu seçebilirsiniz.";
                    project.VideoPath = null;
                }
            }

            RefreshPreview(immediate: true);
            StatusText = $"{Path.GetFileName(path)} açıldı · {Editor?.Summary}";
        });
    }

    private async Task ImportSubtitleFileAsync(string path)
    {
        SubtitleReadResult result;
        try
        {
            result = await SubtitleFile.ReadAsync(path);
        }
        catch (Exception ex) when (ex is SubtitleFormatException or IOException or UnauthorizedAccessException)
        {
            LogFailed(ex, path);
            ErrorText = $"Altyazı okunamadı: {ex.Message}";
            return;
        }

        var project = Project;
        if (project is null)
        {
            project = new Project();
            SetProject(project, projectPath: null);
        }

        var track = project.EnsureActiveTrack();
        if (track.Cues.Count > 0 && _dialogs is not null &&
            !await _dialogs.ConfirmAsync("Altyazıyı değiştir", $"Mevcut {track.Cues.Count} satır, \"{Path.GetFileName(path)}\" içeriğiyle değiştirilecek.", "Değiştir"))
        {
            return;
        }

        track.Cues.Clear();
        track.Cues.AddRange(result.Document.Cues);
        track.Sort();
        track.Renumber();

        // An imported ASS brings its look along (scaled to our 1080 reference).
        if ((result.Document.Styles.FirstOrDefault(s => s.Name == "Default") ?? result.Document.Styles.FirstOrDefault()) is { } style)
        {
            project.Style = AssScriptBuilder.NormalizeStyle(style, result.Document.ScriptInfo?.PlayResY ?? AssScriptBuilder.ReferenceHeight) with { Name = "Default" };
        }

        Editor?.Load(track);
        MarkDirty();
        RefreshPreview(immediate: true);

        var fileName = Path.GetFileName(path);
        var warnings = result.Document.Warnings.Count > 0 ? $" · {result.Document.Warnings.Count} uyarı (log'a bakın)" : string.Empty;
        foreach (var warning in result.Document.Warnings)
        {
            LogImportWarning(fileName, warning);
        }

        StatusText = $"{fileName} içe aktarıldı ({result.Format.Name}, {result.Encoding.WebName}) · {Editor?.Summary}{warnings}";
    }

    // ---------------------------------------------------------------- save / export

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task<bool> SaveProject()
        => ProjectPath is null ? await SaveProjectAsAsync() : await SaveToAsync(ProjectPath);

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task<bool> SaveProjectAs() => await SaveProjectAsAsync();

    private async Task<bool> SaveProjectAsAsync()
    {
        if (_files is null || Project is null)
        {
            return false;
        }

        var path = await _files.SaveProjectAsync(DocumentName);
        return path is not null && await SaveToAsync(path);
    }

    private async Task<bool> SaveToAsync(string path)
    {
        try
        {
            await ProjectSerializer.SaveAsync(Project!, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailed(ex, path);
            ErrorText = $"Proje kaydedilemedi: {ex.Message}";
            return false;
        }

        ProjectPath = path;
        IsDirty = false;
        OnPropertyChanged(nameof(DocumentName));
        StatusText = $"Kaydedildi: {path}";
        return true;
    }

    /// <param name="formatKey"><c>srt</c>, <c>vtt</c> or <c>ass</c>.</param>
    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task ExportSubtitle(string formatKey)
    {
        if (_files is null || Project?.ActiveTrack is not { } track)
        {
            return;
        }

        var format = SubtitleFormats.ForExtension("." + formatKey) ?? SubtitleFormats.Srt;
        var baseName = Path.GetFileNameWithoutExtension(Project.VideoPath ?? ProjectPath ?? "altyazi");
        var suggested = $"{baseName}.{track.Language}";
        if (await _files.SaveSubtitleAsync(format, suggested) is not { } path)
        {
            return;
        }

        var (width, height) = VideoSize();
        var document = format is AssFormat
            ? AssScriptBuilder.BuildDocument(Project, track, width, height)
            : SubtitleDocument.FromTrack(track);
        try
        {
            await SubtitleFile.WriteAsync(path, document, format);
            StatusText = $"Dışa aktarıldı ({format.Name}): {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailed(ex, path);
            ErrorText = $"Dışa aktarılamadı: {ex.Message}";
        }
    }

    /// <summary>Asks to save unsaved changes. Returns false when the user cancels.</summary>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!IsDirty || _dialogs is null)
        {
            return true;
        }

        return await _dialogs.AskUnsavedChangesAsync(DocumentName) switch
        {
            UnsavedChangesChoice.Save => await SaveProject(),
            UnsavedChangesChoice.Discard => true,
            _ => false,
        };
    }

    // ---------------------------------------------------------------- misc

    [RelayCommand]
    private void DismissError() => ErrorText = null;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelJob()
    {
        // Each job will own a CancellationTokenSource; nothing cancellable runs yet.
        StatusText = "İptal edildi";
        IsBusy = false;
    }

    private void SetProject(Project project, string? projectPath)
    {
        Project = project;
        ProjectPath = projectPath;
        IsDirty = false;
        if (project.VideoPath is null)
        {
            CurrentMedia = null;
            _player?.Close();
        }

        Editor?.Load(project.EnsureActiveTrack());
        OnPropertyChanged(nameof(DocumentName));
    }

    private async Task<bool> LoadVideoAsync(string path)
    {
        if (_probe is null || _player is null)
        {
            return false;
        }

        try
        {
            var info = await _probe.ProbeAsync(path);
            if (info.Video is null)
            {
                ErrorText = "Bu dosyada video akışı yok.";
                return false;
            }

            await _player.OpenAsync(path);
            CurrentMedia = info;
            return true;
        }
        catch (MediaToolNotFoundException)
        {
            ErrorText = FfmpegMissingMessage();
            StatusText = "FFmpeg bulunamadı";
        }
        catch (Exception ex) when (ex is MediaProbeException or FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            LogFailed(ex, path);
            ErrorText = $"Video açılamadı: {ex.Message}";
        }

        return false;
    }

    private void OnEdited()
    {
        MarkDirty();
        RefreshPreview(immediate: false);
    }

    private void MarkDirty()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(DocumentName));
    }

    private void RefreshPreview(bool immediate)
    {
        if (_preview is null || Project is null || CurrentMedia is null)
        {
            return;
        }

        var (width, height) = VideoSize();
        _preview.Request(Project, Project.ActiveTrack, width, height);
        if (immediate)
        {
            _preview.Flush();
        }
    }

    private (int Width, int Height) VideoSize()
        => CurrentMedia?.Video is { } v ? (v.DisplayWidth, v.DisplayHeight) : (0, 0);

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        IsBusy = true;
        StatusText = status;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
            if (StatusText == status)
            {
                StatusText = "Hazır";
            }
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operation failed for {Path}")]
    private partial void LogFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import warning in {File}: {Warning}")]
    private partial void LogImportWarning(string file, string warning);
}
