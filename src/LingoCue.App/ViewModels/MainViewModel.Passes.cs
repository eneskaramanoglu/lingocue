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

/// <summary>Tracks, glossary, correction, translation and retrying failed lines.</summary>
public partial class MainViewModel
{

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
        var provider = _resolver.ProviderFor(providerId ?? settings.Llm.Provider);
        var options = PassPlanning.Options(settings, local: provider.Id == OllamaProvider.ProviderId, reflect, targetLanguage ?? source.Language);
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
        var (total, changed, review, failed) = PassPlanning.AddTrack(project, result, kind, _fontCatalog, FontCatalog.ProjectFontDirectory(ProjectPath));
        RefreshTracks();
        MarkDirty();
        RefreshPreview(immediate: true);

        var usage = result.TotalUsage;
        var cost = _resolver?.Provider.PriceFor(modelId) is { } price
            ? string.Create(CultureInfo.InvariantCulture, $" · ücretli anahtarla ~{price.Cost(usage.InputTokens, usage.OutputTokens):0.000} $")
            : string.Empty;
        var suggestions = result.NewGlossarySuggestions > 0 ? $" · sözlüğe {result.NewGlossarySuggestions} öneri eklendi" : string.Empty;
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var counts = kind == TrackPassKind.Correction
            ? string.Create(tr, $"{changed} değişti, {review} kontrol bekliyor, {failed} başarısız")
            : string.Create(tr, $"{total - failed} satır çevrildi, {review} kontrol bekliyor, {failed} başarısız");
        StatusText = string.Create(tr,
            $"{(kind == TrackPassKind.Correction ? "Düzeltme" : "Çeviri")} bitti ({track.Name}): {counts} · {usage.InputTokens:N0}+{usage.OutputTokens:N0} token{cost}{suggestions}");
        if (failed > 0)
        {
            ErrorText = $"{failed} satır işlenemedi (✕) ve kaynak metniyle bırakıldı. Altyazı → Başarısız satırları yeniden dene ile yalnızca bu satırları tekrar gönderebilirsiniz" +
                        (_settings?.Current.Llm.FreeTier == true ? "." : "; ücretsiz anahtar kullanıyorsanız Tercihler'de \"Ücretsiz katman\"ı açın.");
        }
    }
}
