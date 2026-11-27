using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;

namespace LingoCue.Core.Projects;

/// <summary>A translation the plan needs but the project does not have yet.</summary>
/// <param name="SpokenLanguage">Lines spoken in this language…</param>
/// <param name="TargetLanguage">…need a translation into this one.</param>
/// <param name="SourceCueIds">The lines of the shown track that lack it.</param>
public sealed record MissingTranslation(string SpokenLanguage, string TargetLanguage, IReadOnlyList<int> SourceCueIds);

/// <summary>The shown subtitles after applying a plan: main (top) and second (bottom) lines.</summary>
public sealed record PlannedSubtitles(IReadOnlyList<Cue> Main, IReadOnlyList<Cue> Second, IReadOnlyList<MissingTranslation> Missing)
{
    public bool HasSecond => Second.Count > 0;
}

/// <summary>Applies a <see cref="SubtitlePlan"/> to a track using the project's translation tracks.</summary>
public static class SubtitlePlans
{
    /// <summary>
    /// The plan applies to source and corrected tracks while it is enabled; a translation track is shown
    /// as it is.
    /// </summary>
    public static bool AppliesTo(Project project, SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        return project.Presentation.Plan.Enabled && track.Kind != TrackKind.Translation;
    }

    /// <summary>
    /// The lines to show for <paramref name="track"/>. A line whose rule asks for a translation the project
    /// lacks is left out and listed in <see cref="PlannedSubtitles.Missing"/>.
    /// </summary>
    public static PlannedSubtitles Resolve(Project project, SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var plan = project.Presentation.Plan;
        var translations = new Dictionary<string, Dictionary<int, Cue>?>(StringComparer.Ordinal);
        var main = new List<Cue>();
        var second = new List<Cue>();
        var missing = new Dictionary<(string Spoken, string Target), List<int>>();

        foreach (var cue in track.Cues.Where(c => c.End > c.Start))
        {
            var spoken = LanguageCodes.Primary(track.LanguageOf(cue));
            var rule = plan.RuleFor(spoken);
            if (LineFor(rule.Top) is { } top)
            {
                main.Add(top);
            }

            if (LineFor(rule.Bottom) is { } bottom)
            {
                second.Add(bottom);
            }

            Cue? LineFor(PlanLine line)
            {
                switch (line.Kind)
                {
                    case PlanLineKind.Original:
                        var original = cue.Clone();
                        original.Language = spoken;
                        return original;

                    case PlanLineKind.Translation when line.Language is { } target:
                        var code = LanguageCodes.Primary(target);
                        if (code == spoken)
                        {
                            goto case PlanLineKind.Original; // a line already in that language is shown as spoken
                        }

                        if (TranslationOf(project, track, code, translations) is { } byId &&
                            byId.TryGetValue(cue.Id, out var translated) && !string.IsNullOrWhiteSpace(translated.Text))
                        {
                            // Times and id come from the shown line; the text from its translation.
                            var copy = new Cue(cue.Id, cue.Start, cue.End, translated.Text) { Language = code, Speaker = cue.Speaker };
                            return copy;
                        }

                        var key = (spoken, code);
                        if (!missing.TryGetValue(key, out var ids))
                        {
                            missing[key] = ids = [];
                        }

                        ids.Add(cue.Id);
                        return null;

                    default:
                        return null;
                }
            }
        }

        return new PlannedSubtitles(main, second,
            [.. missing.Select(m => new MissingTranslation(m.Key.Spoken, m.Key.Target, m.Value))]);
    }

    /// <summary>
    /// One track with the plan applied, for formats that hold one line set (SRT, VTT): the second line is
    /// written under the first in the same subtitle.
    /// </summary>
    public static SubtitleTrack Flatten(Project project, SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var planned = Resolve(project, track);
        var secondById = planned.Second.ToDictionary(c => c.Id);
        var mainIds = planned.Main.Select(c => c.Id).ToHashSet();
        var result = new SubtitleTrack
        {
            Name = $"{track.Name} · plan",
            Language = track.Language,
            Kind = track.Kind,
            SourceTrackId = track.SourceTrackId,
        };

        foreach (var cue in planned.Main)
        {
            var copy = cue.Clone();
            if (secondById.TryGetValue(cue.Id, out var below))
            {
                copy.Text = $"{cue.Text}\n{below.Text}";
            }

            result.Cues.Add(copy);
        }

        // A line with only a second line shows it alone.
        result.Cues.AddRange(planned.Second.Where(c => !mainIds.Contains(c.Id)).Select(c => c.Clone()));
        result.Sort();
        return result;
    }

    /// <summary>The translations into <paramref name="language"/> of <paramref name="track"/>'s lines, keyed by the line id.</summary>
    private static Dictionary<int, Cue>? TranslationOf(Project project, SubtitleTrack track, string language,
        Dictionary<string, Dictionary<int, Cue>?> cache)
    {
        if (cache.TryGetValue(language, out var found))
        {
            return found;
        }

        // Prefer a translation made from this track; else one made from the track this one was derived from.
        var candidates = project.Tracks
            .Where(t => t.Kind == TrackKind.Translation && LanguageCodes.Primary(t.Language) == language)
            .ToList();
        var direct = candidates.LastOrDefault(t => t.SourceTrackId == track.Id);
        var viaSource = track.SourceTrackId is { } sourceId ? candidates.LastOrDefault(t => t.SourceTrackId == sourceId) : null;
        var chosen = direct ?? viaSource;
        var map = chosen?.Cues.Where(c => c.SourceCueId is not null)
            .GroupBy(c => c.SourceCueId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        // A translation of the source track links to source ids; this track's cues keep them in SourceCueId.
        if (chosen is not null && direct is null && map is not null)
        {
            map = track.Cues.Where(c => c.SourceCueId is { } s && map.ContainsKey(s))
                .ToDictionary(c => c.Id, c => map[c.SourceCueId!.Value]);
        }

        cache[language] = map;
        return map;
    }
}
