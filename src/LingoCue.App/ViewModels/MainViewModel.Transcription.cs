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

/// <summary>Transcription: the Transkribe dialog, the run and the source track it creates.</summary>
public partial class MainViewModel
{

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
            var request = new TranscriptionRequest(media.FilePath, media.Duration, options.Language, options.ModelId, options.Diarize, beam, hints,
                UseNeuralEngine: settings?.UseNeuralEngine == true);

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
        var (track, doubtful) = AsrTracks.Add(project, run, engineName);
        RefreshTracks();
        MarkDirty();
        RefreshPreview(immediate: true);
        StatusText += $" · {track.Cues.Count} satır oluşturuldu ({track.Name})" + (doubtful > 0 ? $" · {doubtful} satır kontrol için işaretlendi (!)" : string.Empty);
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
}
