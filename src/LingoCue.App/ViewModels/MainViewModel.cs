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

public partial class MainViewModel : ViewModelBase
{
    private readonly IThemeService? _themeService;
    private readonly IFilePickerService? _files;
    private readonly IDialogService? _dialogs;
    private readonly IMediaProbe? _probe;
    private readonly IVideoPlayer? _player;
    private readonly SubtitlePreviewService? _preview;
    private readonly IPlatformInfo? _platform;
    private readonly IReadOnlyList<ITranscriptionEngine> _engines = [];
    private readonly ApiKeyProvider? _keys;
    private readonly ISecretStore? _secretStore;
    private readonly ISettingsStore? _settings;
    private readonly IVideoRenderer? _renderer;
    private readonly IFontCatalog? _fontCatalog;
    private readonly ModelResolver? _resolver;
    private readonly ILoggerFactory? _loggers;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _jobCts;
    private readonly IMediaToolLocator? _toolLocator;
    private readonly IHardwareInfoProvider? _hardware;
    private string? _lastEngineId;

    public MainViewModel(
        IThemeService themeService,
        IFilePickerService files,
        IDialogService dialogs,
        IMediaProbe probe,
        IVideoPlayer player,
        SubtitlePreviewService preview,
        PlayerViewModel playerViewModel,
        SubtitleEditorViewModel editor,
        TimelineViewModel timeline,
        QualityViewModel quality,
        StyleEditorViewModel styleEditor,
        RenderQueueViewModel renderQueue,
        IVideoRenderer renderer,
        IFontCatalog fontCatalog,
        IPlatformInfo platform,
        IEnumerable<ITranscriptionEngine> engines,
        ApiKeyProvider keys,
        ISecretStore secretStore,
        ISettingsStore settings,
        ModelResolver resolver,
        ILoggerFactory loggers,
        ILogger<MainViewModel> logger,
        IMediaToolLocator? toolLocator = null,
        IHardwareInfoProvider? hardware = null,
        Core.Updates.IUpdateService? updates = null,
        IFileManager? fileManager = null,
        Services.DiagnosticsService? diagnostics = null)
    {
        _themeService = themeService;
        _files = files;
        _dialogs = dialogs;
        _probe = probe;
        _player = player;
        _preview = preview;
        _platform = platform;
        _engines = [.. engines];
        _keys = keys;
        _secretStore = secretStore;
        _settings = settings;
        _resolver = resolver;
        _loggers = loggers;
        _logger = logger;
        Player = playerViewModel;
        Editor = editor;
        Timeline = timeline;
        Quality = quality;
        Editor.Edited += (_, key) => OnEdited(key);
        Editor.RulesFor = language => settings.Current.Reading.ToRules(language);
        StyleEditor = styleEditor;
        RenderQueue = renderQueue;
        RenderQueue.JobFinished += (_, message) => StatusText = message;
        _renderer = renderer;
        _fontCatalog = fontCatalog;
        StyleEditor.FontAdded += (_, _) => UpdateFontsDirectory();
        StyleEditor.GalleryChanged += (_, _) => RefreshGallery();
        StyleEditor.Changed += (_, _) =>
        {
            MarkDirty("style");
            RefreshPreview(immediate: false);
        };
        PlatformText = platform.RuntimeIdentifier;
        LoadRecentFiles();
        _toolLocator = toolLocator;
        _hardware = hardware;
        _updates = updates;
        _fileManager = fileManager;
        _diagnostics = diagnostics;
        RefreshReadiness();
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

    public TimelineViewModel? Timeline { get; }

    /// <summary>The "Kontrol" tab (QA checks and quick fixes).</summary>
    public QualityViewModel? Quality { get; }

    public StyleEditorViewModel? StyleEditor { get; }

    public RenderQueueViewModel? RenderQueue { get; }

    /// <summary>Right panel tab: 0 = lines, 1 = style.</summary>
    [ObservableProperty]
    public partial int SelectedPanel { get; set; }

    [RelayCommand]
    private void ShowStyle() => SelectedPanel = 1;

    // The template gallery is drawn on the current frame each time the Stil tab is opened.
    partial void OnSelectedPanelChanged(int value) => RefreshGallery();

    private void RefreshGallery()
    {
        if (SelectedPanel == 1 && StyleEditor is { } style)
        {
            _ = style.RefreshGalleryAsync(GalleryScene());
        }
    }

    /// <summary>The selected line (or the first one, or a sample) over the video frame where it is spoken.</summary>
    private Media.Rendering.ThumbnailScene GalleryScene()
    {
        var cue = Editor?.SelectedCue?.Model ?? Project?.ActiveTrack?.Cues.FirstOrDefault();
        var text = cue?.Text is { Length: > 0 } t ? t : "Örnek altyazı satırı\nikinci satır";
        var at = cue is null ? TimeSpan.FromSeconds(1) : cue.Start + ((cue.End - cue.Start) / 2);
        var (width, height) = VideoSize();
        var background = Path.Combine(AppContext.BaseDirectory, "Assets", "gallery-background.jpg");
        return new Media.Rendering.ThumbnailScene(CurrentMedia?.FilePath, at, text, width > 0 ? width : 1920, height > 0 ? height : 1080,
            File.Exists(background) ? background : null, FontCatalog.ProjectFontDirectory(ProjectPath));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand), nameof(SaveProjectAsCommand), nameof(ExportCommand), nameof(EditGlossaryCommand), nameof(ManageTracksCommand), nameof(CorrectCommand), nameof(TranslateCommand))]
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
    [NotifyPropertyChangedFor(nameof(HasMedia), nameof(VideoWidth), nameof(VideoHeight), nameof(MediaSummary))]
    [NotifyCanExecuteChangedFor(nameof(TranscribeCommand))]
    public partial MediaInfo? CurrentMedia { get; private set; }

    public bool HasMedia => CurrentMedia is not null;

    /// <summary>Displayed picture size (for the safe-area guides).</summary>
    public double VideoWidth => CurrentMedia?.Video?.DisplayWidth ?? 0;

    public double VideoHeight => CurrentMedia?.Video?.DisplayHeight ?? 0;

    /// <summary>"1920×1080 · 25 fps" for the chip over the video.</summary>
    public string? MediaSummary => CurrentMedia?.Video is { } v
        ? string.Create(CultureInfo.GetCultureInfo("tr-TR"), $"{v.DisplayWidth}×{v.DisplayHeight} · {v.FrameRate:0.##} fps")
        : null;

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    /// <summary>Tracks of the project for the track selector (full track management comes in M7).</summary>
    public System.Collections.ObjectModel.ObservableCollection<SubtitleTrack> Tracks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CorrectCommand), nameof(TranslateCommand))]
    public partial SubtitleTrack? SelectedTrack { get; set; }

    public bool HasSeveralTracks => Tracks.Count > 1;

    // Long-running job state.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(OpenVideoCommand), nameof(OpenProjectCommand), nameof(ImportSubtitleCommand), nameof(TranscribeCommand), nameof(CorrectCommand), nameof(TranslateCommand), nameof(RetryFailedLinesCommand), nameof(UndoCommand), nameof(RedoCommand), nameof(OpenRecentCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>0..1; 0 shows an indeterminate bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    public partial double Progress { get; set; }

    public bool IsProgressIndeterminate => Progress <= 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelJobCommand))]
    public partial bool CanCancel { get; private set; }

    public bool IsIdle => !IsBusy;

    private bool CanTranscribe => IsIdle && HasMedia && Project is not null;

    private bool CanCorrect => IsIdle && Project?.ActiveTrack is { Cues.Count: > 0 };

    partial void OnSelectedThemeChanged(AppTheme value) => _themeService?.Apply(value);

    // ---------------------------------------------------------------- misc

    [RelayCommand]
    private void DismissError() => ErrorText = null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelJob()
    {
        _jobCts?.Cancel();
        StatusText = "İptal ediliyor…";
    }

    private SubtitleTrack? SourceOf(SubtitleTrack? track)
        => track?.SourceTrackId is { } id ? Project?.Tracks.FirstOrDefault(t => t.Id == id) : null;

    private void RefreshTracks()
    {
        _syncingTracks = true;
        try
        {
            Tracks.Clear();
            foreach (var track in Project?.Tracks ?? [])
            {
                Tracks.Add(track);
            }

            SelectedTrack = Project?.ActiveTrack;
            Editor?.Load(Project?.ActiveTrack, SourceOf(Project?.ActiveTrack));
            StyleEditor?.Load(Project, Project?.ActiveTrack, ProjectPath);
        }
        finally
        {
            _syncingTracks = false;
        }

        OnPropertyChanged(nameof(HasSeveralTracks));
        OnPropertyChanged(nameof(IsTranscribed));
        OnPropertyChanged(nameof(IsCorrected));
        OnPropertyChanged(nameof(IsTranslated));
        RetryFailedLinesCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- workflow (app bar)

    /// <summary>The project has lines from speech recognition or an imported file (Transkribe step done).</summary>
    public bool IsTranscribed => Project?.Tracks.Any(t => t.Kind == TrackKind.Source && t.Cues.Count > 0) == true;

    public bool IsCorrected => Project?.Tracks.Any(t => t.Kind == TrackKind.Corrected) == true;

    public bool IsTranslated => Project?.Tracks.Any(t => t.Kind == TrackKind.Translation) == true;

    /// <summary>Ayarlar → Tema.</summary>
    [RelayCommand]
    private void SetTheme(AppTheme theme) => SelectedTheme = theme;

    private void UpdateFontsDirectory() => _player?.SetFontsDirectory(FontCatalog.ProjectFontDirectory(ProjectPath));

    private void SetProject(Project project, string? projectPath)
    {
        Project = project;
        ProjectPath = projectPath;
        UpdateFontsDirectory();
        IsDirty = false;
        _historyPending = false;
        _history.Reset(project);
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        if (project.VideoPath is null)
        {
            CurrentMedia = null;
            _player?.Close();
        }

        project.EnsureActiveTrack();
        RefreshTracks();
        TranscribeCommand.NotifyCanExecuteChanged();
        CorrectCommand.NotifyCanExecuteChanged();
        TranslateCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
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
            _ = Timeline?.LoadWaveformAsync(path);
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

    private void OnEdited(string? coalesceKey)
    {
        CorrectCommand.NotifyCanExecuteChanged();
        TranslateCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        RetryFailedLinesCommand.NotifyCanExecuteChanged();
        MarkDirty(coalesceKey);
        RefreshPreview(immediate: false);
    }

    /// <param name="coalesceKey">Edits with the same key in quick succession form one undo step.</param>
    private void MarkDirty(string? coalesceKey = null)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(DocumentName));
        ScheduleHistoryRecord(coalesceKey);
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

    /// <summary>The installed FFmpeg lacks libass (the "ass" filter), so subtitles cannot be burned in.</summary>
    private string BurnInMissingMessage() => _platform?.Platform switch
    {
        OsPlatform.MacOS => "Kurulu FFmpeg altyazı gömmeyi desteklemiyor (libass yok). Terminal'de çalıştırın: brew install ffmpeg-full — sonra uygulamayı yeniden başlatın.",
        OsPlatform.Windows => "Kurulu FFmpeg altyazı gömmeyi desteklemiyor (libass yok). Tam sürümü kurun: winget install Gyan.FFmpeg — sonra uygulamayı yeniden başlatın.",
        _ => "Kurulu FFmpeg altyazı gömmeyi desteklemiyor (libass yok). libass içeren bir FFmpeg kurun.",
    };

    private string FfmpegMissingMessage() => _platform?.Platform switch
    {
        OsPlatform.Windows => "FFmpeg bulunamadı. Kurmak için PowerShell'de çalıştırın: winget install Gyan.FFmpeg — sonra uygulamayı yeniden başlatın.",
        OsPlatform.MacOS => "FFmpeg bulunamadı. Kurmak için Terminal'de çalıştırın: brew install ffmpeg-full — sonra uygulamayı yeniden başlatın.",
        _ => "FFmpeg bulunamadı. Paket yöneticinizle ffmpeg kurup uygulamayı yeniden başlatın.",
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operation failed for {Path}")]
    private partial void LogFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import warning in {File}: {Warning}")]
    private partial void LogImportWarning(string file, string warning);
}
