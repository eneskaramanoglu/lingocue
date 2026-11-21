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

/// <summary>Opening videos, projects and subtitle files, and the recent-files list.</summary>
public partial class MainViewModel
{

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
}
