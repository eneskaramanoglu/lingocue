using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;
using LingoCue.Llm;
using LingoCue.Llm.Providers;

namespace LingoCue.App.ViewModels;

/// <summary>The subtitle plan: translating only the lines a per-language plan still needs.</summary>
public partial class MainViewModel
{
    /// <summary>
    /// For every translation the plan lacks, sends only those lines (e.g. the Korean lines to English) and
    /// writes them into the project's translation track for that language, creating it when needed. The
    /// active track stays the shown one.
    /// </summary>
    private async Task FillPlanTranslationsAsync()
    {
        if (_resolver is null || _settings is null || Project is not { ActiveTrack: { } shown } project || !SubtitlePlans.AppliesTo(project, shown))
        {
            return;
        }

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

        var byTarget = SubtitlePlans.Resolve(project, shown).Missing
            .GroupBy(m => m.TargetLanguage)
            .Select(g => (Target: g.Key, Ids: g.SelectMany(m => m.SourceCueIds).ToHashSet()))
            .ToList();
        var written = 0;
        var failed = 0;
        for (var i = 0; i < byTarget.Count; i++)
        {
            var (target, ids) = byTarget[i];
            var label = $"plan: {LanguageCodes.DisplayName(target)} · {ids.Count} satır{(byTarget.Count > 1 ? $" ({i + 1}/{byTarget.Count})" : string.Empty)}";
            var ok = await RunTrackPassAsync(project, shown, TrackPassKind.Translation, modelId, reflect: false, refreshContext: false, target, label,
                onlySourceCueIds: ids,
                onResult: result =>
                {
                    var track = SubtitlePlans.TranslationTrackFor(project, shown, target);
                    if (track is null)
                    {
                        track = new SubtitleTrack
                        {
                            Name = $"Çeviri · {target}",
                            Language = target,
                            Kind = TrackKind.Translation,
                            SourceTrackId = shown.Id,
                        };
                        project.Tracks.Add(track);
                    }

                    var (w, f) = TrackPassRunner.MergeOrAdd(track, shown, result.Pipeline);
                    written += w;
                    failed += f;
                });
            if (!ok)
            {
                break;
            }
        }

        project.ActiveTrackId = shown.Id;
        RefreshTracks();
        MarkDirty();
        RefreshPreview(immediate: true);
        StyleEditor?.UpdatePlanMissing();
        StatusText = failed == 0
            ? $"Plan için {written} satır çevrildi."
            : $"Plan için {written} satır çevrildi, {failed} satır yanıt alamadı; tekrar deneyebilirsiniz.";
    }
}
