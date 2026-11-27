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

/// <summary>Saving the project, exporting subtitles and videos, and confirming close.</summary>
public partial class MainViewModel
{

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

    private string ExportBaseName() => SubtitleExportNames.BaseName(Project?.VideoPath, ProjectPath);

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
            var path = singlePath ?? Path.Combine(folder, SubtitleExportNames.For(ExportBaseName(), choice.Tracks, track) + extension);
            var document = format is AssFormat
                ? AssScriptBuilder.BuildDocument(project, track, width, height, allowBilingual: choice.Tracks.Count == 1, measurer: Measurer())
                : SubtitleDocument.FromTrack(SubtitlePlans.AppliesTo(project, track) ? SubtitlePlans.Flatten(project, track) : track);
            await SubtitleFile.WriteAsync(path, document, format, options);
            written.Add(path);
        }

        StatusText = written.Count == 1
            ? $"Dışa aktarıldı ({format.Name}): {written[0]}"
            : $"{written.Count} altyazı dosyası dışa aktarıldı ({format.Name}): {folder}";
    }

    private async Task QueueVideoExportAsync(Project project, ExportChoice choice)
    {
        if (CurrentMedia is not { } media || _renderer is null || RenderQueue is null)
        {
            return;
        }

        var burnIn = choice.Kind == ExportKind.BurnIn;
        var overlay = choice.Kind == ExportKind.Overlay;
        var extension = overlay || (burnIn && choice.VideoFormat == VideoFormat.ProRes422Hq) ? ".mov"
            : burnIn || choice.Container == SoftSubContainer.Mp4 ? ".mp4" : ".mkv";
        var suffix = overlay ? "altyazi-katmani" : burnIn ? "altyazili" : "altyazi-izli";
        if (await _files!.SaveVideoAsync($"{ExportBaseName()}.{suffix}", extension) is not { } output)
        {
            return;
        }

        var jobsRoot = Path.Combine(Path.GetTempPath(), "LingoCue", "jobs");
        var fonts = FontCatalog.ProjectFontDirectory(ProjectPath);
        var job = overlay
            ? await ExportJobs.PrepareOverlayAsync(project, choice.Tracks[0], media, output, choice.OverlayCodec, fonts, jobsRoot, Measurer())
            : burnIn
            ? await ExportJobs.PrepareBurnInAsync(project, choice.Tracks[0], media, output, choice.Encoder, choice.Quality, fonts, jobsRoot, Measurer(), choice.VideoFormat)
            : await ExportJobs.PrepareSoftSubAsync(project, choice.Tracks, media, output, choice.Container, fonts, jobsRoot, Measurer());
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
}
