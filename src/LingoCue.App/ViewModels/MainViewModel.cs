using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Playback;
using LingoCue.App.Services;
using LingoCue.Core;
using LingoCue.Core.Formats;
using LingoCue.Core.Platform;
using LingoCue.Core.Projects;
using LingoCue.Core.Security;
using LingoCue.Core.Segmentation;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Time;
using LingoCue.Media;
using LingoCue.Media.Probing;
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
    private readonly ModelResolver? _resolver;
    private readonly ILoggerFactory? _loggers;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _jobCts;
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
        IPlatformInfo platform,
        IEnumerable<ITranscriptionEngine> engines,
        ApiKeyProvider keys,
        ISecretStore secretStore,
        ISettingsStore settings,
        ModelResolver resolver,
        ILoggerFactory loggers,
        ILogger<MainViewModel> logger)
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
    [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand), nameof(SaveProjectAsCommand), nameof(ExportSubtitleCommand), nameof(EditGlossaryCommand), nameof(CorrectCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(TranscribeCommand))]
    public partial MediaInfo? CurrentMedia { get; private set; }

    public bool HasMedia => CurrentMedia is not null;

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    /// <summary>Tracks of the project for the track selector (full track management comes in M7).</summary>
    public System.Collections.ObjectModel.ObservableCollection<SubtitleTrack> Tracks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CorrectCommand))]
    public partial SubtitleTrack? SelectedTrack { get; set; }

    public bool HasSeveralTracks => Tracks.Count > 1;

    // Long-running job state.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(OpenVideoCommand), nameof(OpenProjectCommand), nameof(ImportSubtitleCommand), nameof(TranscribeCommand), nameof(CorrectCommand))]
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

        Editor?.Load(track, SourceOf(track));
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
            await _dialogs.ShowSettingsAsync(new SettingsViewModel(_settings, _resolver, _engines, new ApiKeysViewModel(_keys, _secretStore, _platform))))
        {
            Editor?.SetRules(_settings.Current.Reading.ToRules());
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
            var request = new TranscriptionRequest(media.FilePath, media.Duration, options.Language, options.ModelId, options.Diarize);

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
        StatusText += $" · {cues.Count} satır oluşturuldu ({track.Name})";
    }

    // ---------------------------------------------------------------- glossary / correction

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

        var settings = _settings.Current;
        var choice = await _dialogs.AskCorrectOptionsAsync(new CorrectDialogViewModel(_resolver, settings, project, source));
        if (choice is null)
        {
            return;
        }

        var options = new PipelineOptions
        {
            BatchSize = settings.Llm.BatchSize,
            Parallelism = settings.Llm.Parallelism,
            MaxRetries = settings.Llm.MaxRetries,
            Reflect = choice.Reflect,
            ShortenOverlong = settings.Llm.ShortenOverlongLines,
            Rules = settings.Reading.ToRules(),
        };
        var request = new TrackPassRequest(project, source, TrackPassKind.Correction, options, choice.RefreshContext, settings.Llm.KeepCircumflex);

        using var cts = new CancellationTokenSource();
        _jobCts = cts;
        IsBusy = true;
        CanCancel = true;
        Progress = 0;
        ErrorText = null;
        var progress = new Progress<PipelineProgress>(p =>
        {
            Progress = p.Fraction;
            StatusText = $"Düzeltiliyor ({choice.ModelId}) · {p.Stage} {p.Done}/{p.Total}";
        });

        try
        {
            using var client = _resolver.Provider.CreateChatClient(choice.ModelId);
            var result = await new TrackPassRunner(client, _loggers).RunAsync(request, progress, cts.Token);
            AddCorrectedTrack(project, result, choice.ModelId);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Düzeltme iptal edildi; mevcut izler değişmedi.";
        }
        catch (LlmException ex)
        {
            LogFailed(ex, choice.ModelId);
            ErrorText = ex.Remedy is null ? $"Düzeltme başarısız: {ex.Message}" : $"{ex.Remedy} ({ex.Message})";
            StatusText = "Düzeltme başarısız";
        }
#pragma warning disable CA1031 // The provider SDK throws its own exception types; any failure is shown and the source track is untouched.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(ex, choice.ModelId);
            ErrorText = $"Düzeltme başarısız: {ex.Message}";
            StatusText = "Düzeltme başarısız";
        }
        finally
        {
            _jobCts = null;
            CanCancel = false;
            IsBusy = false;
            Progress = 0;
        }
    }

    private void AddCorrectedTrack(Project project, TrackPassResult result, string modelId)
    {
        var track = result.Track;
        var existing = project.Tracks.Count(t => t.Kind == TrackKind.Corrected && t.SourceTrackId == track.SourceTrackId);
        if (existing > 0)
        {
            track.Name = $"{track.Name} {existing + 1}";
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
        StatusText = string.Create(
            CultureInfo.GetCultureInfo("tr-TR"),
            $"Düzeltme bitti ({track.Name}): {changed} değişti, {review} kontrol bekliyor, {failed} başarısız · {usage.InputTokens:N0}+{usage.OutputTokens:N0} token{cost}{suggestions}");
        if (failed > 0)
        {
            ErrorText = $"{failed} satır işlenemedi ve olduğu gibi bırakıldı. Düzelt'i yeniden çalıştırabilir veya elle düzenleyebilirsiniz.";
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
        }
        finally
        {
            _syncingTracks = false;
        }

        OnPropertyChanged(nameof(HasSeveralTracks));
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

        project.EnsureActiveTrack();
        RefreshTracks();
        TranscribeCommand.NotifyCanExecuteChanged();
        CorrectCommand.NotifyCanExecuteChanged();
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
        CorrectCommand.NotifyCanExecuteChanged();
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
