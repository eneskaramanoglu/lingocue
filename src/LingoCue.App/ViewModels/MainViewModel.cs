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
using LingoCue.Core.Subtitles;
using LingoCue.Core.Time;
using LingoCue.Media;
using LingoCue.Media.Probing;
using LingoCue.Media.Rendering;
using LingoCue.Media.Tools;
using LingoCue.Core.Settings;
using LingoCue.Infrastructure.Security;
using LingoCue.Llm;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;
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
        IHardwareInfoProvider? hardware = null)
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
        StyleEditor.Changed += (_, _) =>
        {
            MarkDirty("style");
            RefreshPreview(immediate: false);
        };
        PlatformText = platform.RuntimeIdentifier;
        LoadRecentFiles();
        _toolLocator = toolLocator;
        _hardware = hardware;
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
                RememberRecent(path);
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
            Timeline?.Clear(); // the previous video's waveform
            RememberRecent(path);
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
        var mode = track.Cues.Count == 0 || _dialogs is null
            ? ImportMode.Replace
            : await _dialogs.AskImportModeAsync(Path.GetFileName(path), track.Name, track.Cues.Count);
        if (mode == ImportMode.Cancel)
        {
            return;
        }

        if (mode == ImportMode.AddTrack)
        {
            // A second language (e.g. an English SRT) next to the existing tracks; the project style stays.
            track = TrackOperations.AddImported(project, result.Document.Cues, Path.GetFileName(path));
            RefreshTracks();
        }
        else
        {
            track.Cues.Clear();
            track.Cues.AddRange(result.Document.Cues);
            track.Sort();
            track.Renumber();

            // An imported ASS brings its look along (scaled to our 1080 reference).
            if ((result.Document.Styles.FirstOrDefault(s => s.Name == "Default") ?? result.Document.Styles.FirstOrDefault()) is { } style)
            {
                project.Style = AssScriptBuilder.NormalizeStyle(style, result.Document.ScriptInfo?.PlayResY ?? AssScriptBuilder.ReferenceHeight) with { Name = "Default" };
            }

            Editor?.Load(track, SourceOf(track));
            StyleEditor?.Load(Project, track, ProjectPath);
        }
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
        UpdateFontsDirectory();
        StyleEditor?.Load(Project, Project?.ActiveTrack, path);
        FlushHistory();
        _history.MarkSaved(Project!);
        IsDirty = false;
        OnPropertyChanged(nameof(DocumentName));
        StatusText = $"Kaydedildi: {path}";
        RememberRecent(path);
        return true;
    }

    private bool CanExport => HasProject && Project!.Tracks.Any(t => t.Cues.Count > 0);

    /// <summary>One dialog for subtitle files, burned-in video and soft-subtitle video.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        if (_dialogs is null || _files is null || _renderer is null || Project is not { } project || project.ActiveTrack is not { } active)
        {
            return;
        }

        var hardware = CurrentMedia is null ? [] : await _renderer.HardwareEncodersAsync();
        var burnInProblem = CurrentMedia is not null && !await _renderer.SupportsBurnInAsync() ? BurnInMissingMessage() : null;
        var choice = await _dialogs.AskExportAsync(new ExportDialogViewModel(project, active, CurrentMedia, _fontCatalog, ProjectPath, hardware, burnInProblem));
        if (choice is null)
        {
            return;
        }

        try
        {
            if (choice.Kind == ExportKind.SubtitleFiles)
            {
                await ExportSubtitleFilesAsync(project, choice);
            }
            else
            {
                await QueueVideoExportAsync(project, choice);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogFailed(ex, ProjectPath ?? project.VideoPath ?? "export");
            ErrorText = $"Dışa aktarılamadı: {ex.Message}";
        }
    }

    private string ExportBaseName() => Path.GetFileNameWithoutExtension(Project?.VideoPath ?? ProjectPath ?? "altyazi");

    private async Task ExportSubtitleFilesAsync(Project project, ExportChoice choice)
    {
        var format = choice.SubtitleFormat;
        var extension = format.Extensions[0];
        string folder;
        string? singlePath = null;
        if (choice.Tracks.Count == 1)
        {
            if (await _files!.SaveSubtitleAsync(format, $"{ExportBaseName()}.{choice.Tracks[0].Language}") is not { } path)
            {
                return;
            }

            singlePath = path;
            folder = Path.GetDirectoryName(path)!;
        }
        else if (await _files!.PickFolderAsync("Altyazı dosyalarının klasörü") is { } picked)
        {
            folder = picked;
        }
        else
        {
            return;
        }

        var (width, height) = VideoSize();
        var options = new SubtitleWriteOptions { Utf8Bom = choice.Utf8Bom };
        var written = new List<string>();
        foreach (var track in choice.Tracks)
        {
            var path = singlePath ?? Path.Combine(folder, UniqueSubtitleName(choice.Tracks, track) + extension);
            var document = format is AssFormat
                ? AssScriptBuilder.BuildDocument(project, track, width, height, allowBilingual: choice.Tracks.Count == 1)
                : SubtitleDocument.FromTrack(track);
            await SubtitleFile.WriteAsync(path, document, format, options);
            written.Add(path);
        }

        StatusText = written.Count == 1
            ? $"Dışa aktarıldı ({format.Name}): {written[0]}"
            : $"{written.Count} altyazı dosyası dışa aktarıldı ({format.Name}): {folder}";
    }

    /// <summary><c>film.tr</c>, or <c>film.tr.Düzeltilmiş</c> when two chosen tracks share a language.</summary>
    private string UniqueSubtitleName(IReadOnlyList<SubtitleTrack> tracks, SubtitleTrack track)
    {
        var name = $"{ExportBaseName()}.{track.Language}";
        if (tracks.Count(t => t.Language == track.Language) > 1)
        {
            var safe = string.Concat(track.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is ':' or '/' or '\\' ? '_' : c)).Trim();
            name += "." + safe.Replace(" · ", "-", StringComparison.Ordinal);
        }

        return name;
    }

    private async Task QueueVideoExportAsync(Project project, ExportChoice choice)
    {
        if (CurrentMedia is not { } media || _renderer is null || RenderQueue is null)
        {
            return;
        }

        var burnIn = choice.Kind == ExportKind.BurnIn;
        var extension = burnIn || choice.Container == SoftSubContainer.Mp4 ? ".mp4" : ".mkv";
        var suffix = burnIn ? "altyazili" : "altyazi-izli";
        if (await _files!.SaveVideoAsync($"{ExportBaseName()}.{suffix}", extension) is not { } output)
        {
            return;
        }

        var jobsRoot = Path.Combine(Path.GetTempPath(), "LingoCue", "jobs");
        var fonts = FontCatalog.ProjectFontDirectory(ProjectPath);
        var job = burnIn
            ? await ExportJobs.PrepareBurnInAsync(project, choice.Tracks[0], media, output, choice.Encoder, choice.Quality, fonts, jobsRoot)
            : await ExportJobs.PrepareSoftSubAsync(project, choice.Tracks, media, output, choice.Container, fonts, jobsRoot);
        RenderQueue.Enqueue(job);
        StatusText = $"Kuyruğa eklendi: {Path.GetFileName(output)} — ilerlemeyi sağ alttaki İşler'den izleyebilirsiniz.";
    }

    /// <summary>Before the window closes: running exports and unsaved changes. False keeps the window open.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (RenderQueue is { IsBusy: true } queue && _dialogs is not null)
        {
            if (!await _dialogs.ConfirmAsync("Dışa aktarma sürüyor", $"{queue.ActiveCount} dışa aktarma işi bitmedi. Kapatırsanız iptal edilecek.", "İptal et ve kapat"))
            {
                return false;
            }

            queue.CancelAll();
        }

        return await ConfirmDiscardChangesAsync();
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

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelJob()
    {
        _jobCts?.Cancel();
        StatusText = "İptal ediliyor…";
    }

    // ---------------------------------------------------------------- transcription

    [RelayCommand]
    private async Task Preferences()
    {
        if (_dialogs is not null && _settings is not null && _resolver is not null && _keys is not null && _secretStore is not null && _platform is not null &&
            await _dialogs.ShowSettingsAsync(new SettingsViewModel(_settings, _resolver, _engines, new ApiKeysViewModel(_keys, _secretStore, _platform), _hardware)))
        {
            Editor?.Refresh();
            RefreshReadiness();
            StatusText = "Ayarlar kaydedildi.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanTranscribe))]
    private async Task Transcribe()
    {
        if (_dialogs is null || Project is not { } project || CurrentMedia is not { } media)
        {
            return;
        }

        var options = await _dialogs.AskTranscribeOptionsAsync(new TranscribeDialogViewModel(_engines, project, media.Duration, _lastEngineId ?? _settings?.Current.Transcription.DefaultEngine));
        if (options is null)
        {
            return;
        }

        _lastEngineId = options.Engine.Id;
        AsrRun run;
        if (options.CachedRun is { } cached)
        {
            run = cached;
        }
        else
        {
            var result = await RunTranscriptionAsync(options, media);
            if (result is null)
            {
                return;
            }

            run = new AsrRun
            {
                Engine = result.Engine,
                Model = result.Model,
                Language = options.Language,
                DetectedLanguage = result.DetectedLanguage,
                Diarized = options.Diarize,
                Elapsed = result.Elapsed,
                CostUsd = result.EstimatedCostUsd,
                Words = [.. result.Words],
            };
            project.AsrRuns.Add(run);
            StatusText = $"Tanıma bitti: {run.Words.Count} kelime, {run.Elapsed:mm\\:ss} sürdü · {result.Device}" +
                         (result.EstimatedCostUsd is > 0 ? $" · ~{result.EstimatedCostUsd:0.000} $" : string.Empty);
        }

        AddTrackFromAsr(project, run, options.Engine.DisplayName);
    }

    private async Task<TranscriptionResult?> RunTranscriptionAsync(TranscribeOptions options, MediaInfo media)
    {
        using var cts = new CancellationTokenSource();
        _jobCts = cts;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ErrorText = null;
        var progress = new Progress<TranscriptionProgress>(p =>
        {
            Progress = p.Fraction;
            StatusText = $"{StageText(p.Stage)} %{p.Fraction * 100:0}{(p.Detail is null ? string.Empty : " · " + p.Detail)}";
        });

        try
        {
            var settings = _settings?.Current.Transcription;
            var beam = settings?.BeamSearch ?? (_hardware is not null && HardwareAdvisor.Advise(_hardware.Read()).BeamSearch);
            var hints = settings?.UseGlossaryHints != false && Project is { } project ? AsrReview.SpellingHints(project.Glossary) : null;
            var request = new TranscriptionRequest(media.FilePath, media.Duration, options.Language, options.ModelId, options.Diarize, beam, hints);

            // Native inference is CPU/GPU heavy; keep it off the UI thread.
            return await Task.Run(() => options.Engine.TranscribeAsync(request, progress, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Transkripsiyon iptal edildi.";
        }
        catch (TranscriptionException ex)
        {
            LogFailed(ex, media.FilePath);
            ErrorText = ex.Remedy is null ? $"Transkripsiyon başarısız: {ex.Message}" : $"{ex.Remedy} ({ex.Message})";
            StatusText = "Transkripsiyon başarısız";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or Media.Audio.AudioExtractionException or MediaToolNotFoundException)
        {
            LogFailed(ex, media.FilePath);
            ErrorText = ex is MediaToolNotFoundException ? FfmpegMissingMessage() : $"Transkripsiyon başarısız: {ex.Message}";
            StatusText = "Transkripsiyon başarısız";
        }
        finally
        {
            _jobCts = null;
            CanCancel = false;
            IsBusy = false;
            Progress = 0;
        }

        return null;
    }

    private void AddTrackFromAsr(Project project, AsrRun run, string engineName)
    {
        var language = run.Language ?? run.DetectedLanguage ?? project.SourceLanguage;
        var cues = Segmenter.Segment(run.Words, new SegmentationOptions { Language = language });
        var doubtful = AsrReview.FlagDoubtful(cues);
        var track = new SubtitleTrack
        {
            Name = $"Kaynak · {engineName.Split(' ')[0]} {run.Model}",
            Language = language,
            Kind = TrackKind.Source,
            Cues = [.. cues],
        };
        // Replace the empty placeholder track a new project starts with.
        if (project.ActiveTrack is { Cues.Count: 0 } empty)
        {
            project.Tracks.Remove(empty);
        }

        project.Tracks.Add(track);
        project.ActiveTrackId = track.Id;
        RefreshTracks();
        MarkDirty();
        RefreshPreview(immediate: true);
        StatusText += $" · {cues.Count} satır oluşturuldu ({track.Name})" + (doubtful > 0 ? $" · {doubtful} satır kontrol için işaretlendi (!)" : string.Empty);
    }

    // ---------------------------------------------------------------- glossary / correction

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task ManageTracks()
    {
        if (_dialogs is null || Project is not { } project)
        {
            return;
        }

        var tracks = new TracksViewModel(project, _dialogs);
        await _dialogs.ShowTracksAsync(tracks);
        if (tracks.Changed)
        {
            project.EnsureActiveTrack();
            RefreshTracks();
            MarkDirty();
            RefreshPreview(immediate: true);
        }
    }

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task EditGlossary()
    {
        if (_dialogs is not null && Project is { } project && await _dialogs.EditGlossaryAsync(new GlossaryViewModel(project)))
        {
            MarkDirty();
            StatusText = $"Sözlük güncellendi: {project.Glossary.Count} terim.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCorrect))]
    private async Task Correct()
    {
        if (_dialogs is null || _resolver is null || _settings is null || _loggers is null ||
            Project is not { } project || project.ActiveTrack is not { Cues.Count: > 0 } source)
        {
            return;
        }

        var choice = await _dialogs.AskCorrectOptionsAsync(new CorrectDialogViewModel(_resolver, _settings.Current, project, source));
        if (choice is not null)
        {
            await RunTrackPassAsync(project, source, TrackPassKind.Correction, choice.ModelId, choice.Reflect, choice.RefreshContext, targetLanguage: null, providerId: choice.ProviderId);
        }
    }

    private bool CanTranslate => IsIdle && Project is { } p && p.Tracks.Any(t => t.Cues.Count > 0);

    /// <summary>A local model may load for a minute and generate slowly on CPU.</summary>
    private static readonly TimeSpan LocalRequestTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The active track is a correction or translation with lines the model did not finish.</summary>
    private bool CanRetryFailed => IsIdle && Project?.ActiveTrack is { Kind: TrackKind.Corrected or TrackKind.Translation } t &&
                                   SourceOf(t) is not null && t.Cues.Any(c => c.Marks.HasFlag(CueMarks.LlmFailed));

    /// <summary>
    /// Sends only the failed lines of the active corrected/translated track again (e.g. after a free tier
    /// timed out) and fills them in; finished lines are not paid for twice.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRetryFailed))]
    private async Task RetryFailedLines()
    {
        if (_resolver is null || _settings is null || Project is not { ActiveTrack: { } target } project || SourceOf(target) is not { } source)
        {
            return;
        }

        var ids = target.Cues.Where(c => c.Marks.HasFlag(CueMarks.LlmFailed) && c.SourceCueId is not null)
            .Select(c => c.SourceCueId!.Value).ToHashSet();
        string modelId;
        try
        {
            modelId = await _resolver.ResolveAsync(_settings.Current.Llm);
        }
        catch (LlmException ex)
        {
            ErrorText = ex.Remedy ?? ex.Message;
            return;
        }

        var kind = target.Kind == TrackKind.Translation ? TrackPassKind.Translation : TrackPassKind.Correction;
        await RunTrackPassAsync(project, source, kind, modelId, reflect: false, refreshContext: false,
            kind == TrackPassKind.Translation ? target.Language : null, runLabel: $"{ids.Count} başarısız satır", onlySourceCueIds: ids,
            onResult: result =>
            {
                var (fixedCount, stillFailed) = TrackPassRunner.MergeInto(target, result.Pipeline);
                Editor?.Rebuild(Editor.SelectedCue?.Model.Id);
                MarkDirty();
                RefreshPreview(immediate: true);
                StatusText = stillFailed == 0
                    ? $"{fixedCount} satır tamamlandı."
                    : $"{fixedCount} satır tamamlandı, {stillFailed} satır yine yanıt alamadı; biraz sonra tekrar deneyin.";
            });
        RetryFailedLinesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Translates a track (by default the latest corrected one) into a new track with the same timing.</summary>
    [RelayCommand(CanExecute = nameof(CanTranslate))]
    private async Task Translate()
    {
        if (_dialogs is null || _resolver is null || _settings is null || Project is not { } project)
        {
            return;
        }

        var choice = await _dialogs.AskTranslateOptionsAsync(new TranslateDialogViewModel(_resolver, _settings.Current, project));
        if (choice is null)
        {
            return;
        }

        // One run per language, in order; the video context is extracted at most once; a failure or cancel stops the rest.
        var count = choice.TargetLanguages.Count;
        for (var i = 0; i < count; i++)
        {
            var label = count > 1 ? $"{Core.Text.LanguageCodes.DisplayName(choice.TargetLanguages[i])} {i + 1}/{count}" : null;
            if (!await RunTrackPassAsync(project, choice.Source, TrackPassKind.Translation, choice.ModelId, choice.Reflect,
                    choice.RefreshContext && i == 0, choice.TargetLanguages[i], label, providerId: choice.ProviderId))
            {
                break;
            }
        }
    }

    /// <param name="runLabel">Shown in the status while several runs are queued (e.g. "Korece 2/2").</param>
    /// <returns>True when the run finished and its track was added.</returns>
    private async Task<bool> RunTrackPassAsync(
        Project project, SubtitleTrack source, TrackPassKind kind, string modelId, bool reflect, bool refreshContext, string? targetLanguage,
        string? runLabel = null, IReadOnlySet<int>? onlySourceCueIds = null, Action<TrackPassResult>? onResult = null, string? providerId = null)
    {
        if (_resolver is null || _settings is null || _loggers is null)
        {
            return false;
        }

        var settings = _settings.Current;
        var outputLanguage = targetLanguage ?? source.Language;
        // Local models: no rate limits but slow first loads and slower generation, so one smaller batch at a time and a long timeout.
        var provider = _resolver.ProviderFor(providerId ?? settings.Llm.Provider);
        var local = provider.Id == OllamaProvider.ProviderId;
        var options = new PipelineOptions
        {
            BatchSize = local ? Math.Clamp(settings.Llm.LocalBatchSize, 5, 40)
                : settings.Llm.FreeTier ? Math.Min(settings.Llm.BatchSize, LlmSettings.FreeTierBatchSize) : settings.Llm.BatchSize,
            Parallelism = local ? Math.Clamp(settings.Llm.LocalParallelism, 1, 4) : settings.Llm.FreeTier ? 1 : settings.Llm.Parallelism,
            Pacer = !local && settings.Llm.FreeTier ? RequestPacer.ForRate(settings.Llm.RequestsPerMinute) : null,
            RequestTimeout = local ? LocalRequestTimeout : TimeSpan.FromMinutes(2),
            MaxRetries = settings.Llm.MaxRetries,
            Reflect = reflect,
            ShortenOverlong = settings.Llm.ShortenOverlongLines,
            Rules = settings.Reading.ToRules(outputLanguage), // shortening follows the output language
        };
        var request = new TrackPassRequest(project, source, kind, options, refreshContext, settings.Llm.KeepCircumflex, targetLanguage, onlySourceCueIds);
        var verb = kind == TrackPassKind.Correction ? "Düzeltme" : "Çeviri";

        using var cts = new CancellationTokenSource();
        _jobCts = cts;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ErrorText = null;
        var progress = new Progress<PipelineProgress>(p =>
        {
            Progress = p.Fraction;
            StatusText = $"{(kind == TrackPassKind.Correction ? "Düzeltiliyor" : "Çevriliyor")}{(runLabel is null ? string.Empty : $" · {runLabel}")} ({modelId}) · {p.Stage} {p.Done}/{p.Total}";
        });

        try
        {
            using var client = provider.CreateChatClient(modelId);
            var result = await new TrackPassRunner(client, _loggers).RunAsync(request, progress, cts.Token);
            (onResult ?? (r => AddPassTrack(project, r, modelId, kind)))(result);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{verb} iptal edildi; mevcut izler değişmedi.";
        }
        catch (LlmException ex)
        {
            LogFailed(ex, modelId);
            ErrorText = ex.Remedy is null ? $"{verb} başarısız: {ex.Message}" : $"{ex.Remedy} ({ex.Message})";
            StatusText = $"{verb} başarısız";
        }
#pragma warning disable CA1031 // The provider SDK throws its own exception types; any failure is shown and the source track is untouched.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(ex, modelId);
            ErrorText = $"{verb} başarısız: {ex.Message}";
            StatusText = $"{verb} başarısız";
        }
        finally
        {
            _jobCts = null;
            CanCancel = false;
            IsBusy = false;
            Progress = 0;
        }

        return false;
    }

    private void AddPassTrack(Project project, TrackPassResult result, string modelId, TrackPassKind kind)
    {
        var track = result.Track;
        var existing = project.Tracks.Count(t => t.Kind == track.Kind && t.SourceTrackId == track.SourceTrackId && t.Language == track.Language);
        if (existing > 0)
        {
            track.Name = $"{track.Name} {existing + 1}";
        }

        // A first translation becomes the bilingual partner (bilingual mode itself stays as it was).
        if (kind == TrackPassKind.Translation && project.Presentation.Bilingual.SecondaryTrackId is null)
        {
            project.Presentation = project.Presentation with
            {
                Bilingual = project.Presentation.Bilingual with { SecondaryTrackId = track.Id },
            };
        }

        // A Korean second track needs a font with Hangul, or libass draws boxes (preview and burn-in alike).
        var bilingual = project.Presentation.Bilingual;
        var fontsDirectory = FontCatalog.ProjectFontDirectory(ProjectPath);
        if (bilingual.SecondaryTrackId == track.Id && track.Language?.StartsWith("ko", StringComparison.OrdinalIgnoreCase) == true &&
            _fontCatalog is not null && !_fontCatalog.Check(bilingual.SecondaryStyle.FontName, fontsDirectory).HasHangul &&
            _fontCatalog.SuggestHangulFamily(fontsDirectory) is { } hangulFont)
        {
            project.Presentation = project.Presentation with
            {
                Bilingual = bilingual with { SecondaryStyle = bilingual.SecondaryStyle with { FontName = hangulFont } },
            };
        }

        project.Tracks.Add(track);
        project.ActiveTrackId = track.Id;
        RefreshTracks();
        MarkDirty();
        RefreshPreview(immediate: true);

        var lines = result.Pipeline.Lines;
        var changed = lines.Count(l => l.Status == LineStatus.Changed);
        var review = lines.Count(l => l.Status == LineStatus.NeedsReview);
        var failed = lines.Count(l => l.Status == LineStatus.Failed);
        var usage = result.TotalUsage;
        var cost = _resolver?.Provider.PriceFor(modelId) is { } price
            ? string.Create(CultureInfo.InvariantCulture, $" · ücretli anahtarla ~{price.Cost(usage.InputTokens, usage.OutputTokens):0.000} $")
            : string.Empty;
        var suggestions = result.NewGlossarySuggestions > 0 ? $" · sözlüğe {result.NewGlossarySuggestions} öneri eklendi" : string.Empty;
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var counts = kind == TrackPassKind.Correction
            ? string.Create(tr, $"{changed} değişti, {review} kontrol bekliyor, {failed} başarısız")
            : string.Create(tr, $"{lines.Count - failed} satır çevrildi, {review} kontrol bekliyor, {failed} başarısız");
        StatusText = string.Create(tr,
            $"{(kind == TrackPassKind.Correction ? "Düzeltme" : "Çeviri")} bitti ({track.Name}): {counts} · {usage.InputTokens:N0}+{usage.OutputTokens:N0} token{cost}{suggestions}");
        if (failed > 0)
        {
            ErrorText = $"{failed} satır işlenemedi (✕) ve kaynak metniyle bırakıldı. Altyazı → Başarısız satırları yeniden dene ile yalnızca bu satırları tekrar gönderebilirsiniz" +
                        (_settings?.Current.Llm.FreeTier == true ? "." : "; ücretsiz anahtar kullanıyorsanız Tercihler'de \"Ücretsiz katman\"ı açın.");
        }
    }

    private static string StageText(TranscriptionStage stage) => stage switch
    {
        TranscriptionStage.Preparing => "Hazırlanıyor",
        TranscriptionStage.DownloadingModel => "Model indiriliyor",
        TranscriptionStage.ExtractingAudio => "Ses ayıklanıyor",
        TranscriptionStage.Uploading => "Yükleniyor",
        TranscriptionStage.Recognizing => "Konuşma tanınıyor",
        _ => "Bitiriliyor",
    };

    partial void OnSelectedTrackChanged(SubtitleTrack? value)
    {
        // RefreshTracks loads the editor itself.
        if (value is null || Project is null || _syncingTracks)
        {
            return;
        }

        Project.ActiveTrackId = value.Id;
        Editor?.Load(value, SourceOf(value));
        StyleEditor?.Load(Project, value, ProjectPath);
        RefreshPreview(immediate: true);
    }

    private bool _syncingTracks;

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

    // ---------------------------------------------------------------- welcome screen

    /// <summary>The welcome screen's "Hazırlık" list: what the main features need and whether it is there.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ReadinessItem> Readiness { get; } = [];

    private void RefreshReadiness()
    {
        Readiness.Clear();
        if (_toolLocator is not null)
        {
            var ffmpeg = _toolLocator.TryLocate(out _);
            Readiness.Add(new ReadinessItem("FFmpeg", ffmpeg ? "bulundu" : "kurulu değil — video açmak için gerekli", ffmpeg));
        }

        if (_keys is not null)
        {
            var key = _keys.HasGeminiKey;
            Readiness.Add(new ReadinessItem("Gemini anahtarı", key ? "kayıtlı" : "yok — düzeltme ve çeviri için", key));
        }

        if (_settings is not null && _engines.OfType<Transcription.Whisper.WhisperNetEngine>().FirstOrDefault() is { } whisper)
        {
            var model = _settings.Current.Transcription.WhisperModel;
            var ready = whisper.IsModelDownloaded(model);
            var size = Transcription.Whisper.WhisperModels.All.FirstOrDefault(m => m.Id == model)?.SizeBytes ?? 0;
            Readiness.Add(new ReadinessItem("Whisper modeli", ready ? model : $"ilk transkripsiyonda iner (~{size / 1e9:0.#} GB)", ready));
        }

        if (_hardware is not null)
        {
            var info = _hardware.Read();
            var advice = HardwareAdvisor.Advise(info);
            var suggested = Transcription.Whisper.WhisperModels.For(advice.Whisper);
            var current = _settings?.Current.Transcription.WhisperModel;
            var detail = HardwareAdvisor.Describe(info) + (suggested == current
                ? " · ayarlar bu donanıma uygun"
                : $" · önerilen Whisper modeli: {suggested} (Ayarlar → Genel → Önerilen ayarları uygula)");
            Readiness.Add(new ReadinessItem("Donanım", detail, IsReady: true));
        }

        _ = AddOllamaReadinessAsync();
    }

    /// <summary>Optional local engine: asks Ollama briefly in the background so startup never waits for it.</summary>
    private async Task AddOllamaReadinessAsync()
    {
        if (_resolver?.Providers.FirstOrDefault(p => p.Id == OllamaProvider.ProviderId) is not { } ollama)
        {
            return;
        }

        string detail;
        bool ok;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var models = await ollama.ListModelsAsync(timeout.Token);
            ok = models.Count > 0;
            detail = ok ? $"çalışıyor · {models.Count} model" : "çalışıyor, model indirilmemiş (ollama pull gemma3:12b)";
        }
        catch (Exception ex) when (ex is LlmException or HttpRequestException or OperationCanceledException)
        {
            ok = false;
            detail = "yok — isteğe bağlı: ücretsiz, çevrimdışı çeviri için";
        }

        Readiness.Add(new ReadinessItem("Yerel model (Ollama)", detail, ok));
    }

    // ---------------------------------------------------------------- recent files

    /// <summary>Dosya → Son açılanlar (newest first).</summary>
    public System.Collections.ObjectModel.ObservableCollection<RecentFileItem> RecentFiles { get; } = [];

    public bool HasRecentFiles => RecentFiles.Count > 0;

    private void RememberRecent(string path)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.Save(_settings.Current.WithRecentFile(path));
        LoadRecentFiles();
    }

    private void LoadRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _settings?.Current.RecentFiles ?? [])
        {
            RecentFiles.Add(new RecentFileItem(path, OpenRecentCommand));
        }

        OnPropertyChanged(nameof(HasRecentFiles));
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenRecent(string path)
    {
        if (!File.Exists(path))
        {
            ErrorText = $"Dosya bulunamadı, listeden çıkarıldı: {path}";
            if (_settings is not null)
            {
                _settings.Save(_settings.Current.WithoutRecentFile(path));
                LoadRecentFiles();
            }

            return;
        }

        await OpenFileAsync(path);
    }

    // ---------------------------------------------------------------- undo / redo

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
