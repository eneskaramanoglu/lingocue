using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Projects;

/// <summary>
/// Operations on a project's tracks that keep the links between them consistent: a corrected or
/// translated track points to its source track (<see cref="SubtitleTrack.SourceTrackId"/>) and each of
/// its cues to the source cue (<see cref="Cue.SourceCueId"/>).
/// </summary>
public static class TrackOperations
{
    /// <summary>
    /// The text of the line at the start of the chain (normally what speech recognition produced), or
    /// null when the cue has no linked origin or the origin was deleted.
    /// </summary>
    public static string? OriginalText(Project project, SubtitleTrack track, Cue cue)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(cue);

        Cue? origin = null;
        var currentTrack = track;
        var currentCue = cue;
        var guard = 0;
        while (currentTrack.SourceTrackId is { } parentId && currentCue.SourceCueId is { } parentCueId && guard++ < 16)
        {
            var parent = project.Tracks.FirstOrDefault(t => t.Id == parentId);
            var parentCue = parent?.Cues.FirstOrDefault(c => c.Id == parentCueId);
            if (parent is null || parentCue is null)
            {
                break;
            }

            origin = parentCue;
            currentTrack = parent;
            currentCue = parentCue;
        }

        return origin?.Text;
    }

    /// <summary>A copy right after <paramref name="track"/> (same cues, ids and links), made active.</summary>
    public static SubtitleTrack Duplicate(Project project, SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var copy = new SubtitleTrack
        {
            Name = track.Name + " (kopya)",
            Language = track.Language,
            Kind = track.Kind,
            SourceTrackId = track.SourceTrackId,
            Cues = [.. track.Cues.Select(c => c.Clone())],
        };
        project.Tracks.Insert(project.Tracks.IndexOf(track) + 1, copy);
        project.ActiveTrackId = copy.Id;
        return copy;
    }

    /// <summary>
    /// One track per language of a mixed-language track (most lines first), for exporting each language as its
    /// own file or stream. The tracks are copies outside the project; a one-language track comes back as is.
    /// </summary>
    public static IReadOnlyList<SubtitleTrack> SplitByLanguage(SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var languages = track.SpokenLanguages();
        if (languages.Count < 2)
        {
            return [track];
        }

        return [.. languages.Select(language => new SubtitleTrack
        {
            Name = $"{track.Name} · {language}",
            Language = language,
            Kind = track.Kind,
            SourceTrackId = track.SourceTrackId,
            Cues = [.. track.Cues.Where(c => string.Equals(track.LanguageOf(c), language, StringComparison.OrdinalIgnoreCase))
                .Select(c => { var copy = c.Clone(); copy.Language = null; return copy; })],
        })];
    }

    public static SubtitleTrack AddEmpty(Project project, string name, string language)
    {
        ArgumentNullException.ThrowIfNull(project);
        var track = new SubtitleTrack { Name = name, Language = language, Kind = TrackKind.Source };
        project.Tracks.Add(track);
        project.ActiveTrackId = track.Id;
        return track;
    }

    /// <summary>Adds imported cues as a new track; the language comes from a <c>name.en.srt</c> style file name when present.</summary>
    public static SubtitleTrack AddImported(Project project, IEnumerable<Cue> cues, string fileName)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(fileName);
        var language = LanguageFromFileName(fileName) ?? project.SourceLanguage;
        var track = new SubtitleTrack
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            Language = language,
            Kind = TrackKind.Source,
            Cues = [.. cues],
        };
        track.Sort();
        track.Renumber();
        project.Tracks.Add(track);
        project.ActiveTrackId = track.Id;
        return track;
    }

    /// <summary><c>film.en.srt</c> → <c>en</c>; <c>film.pt-BR.vtt</c> → <c>pt-BR</c>; otherwise null.</summary>
    public static string? LanguageFromFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var dot = stem.LastIndexOf('.');
        if (dot < 0)
        {
            return null;
        }

        var tag = stem[(dot + 1)..];
        var primary = tag.Split('-')[0];
        return primary.Length is 2 or 3 && primary.All(char.IsAsciiLetter) && tag.Length <= 8 ? tag : null;
    }

    /// <summary>Moves a track up (-1) or down (+1) in the list (the order used for exports).</summary>
    public static void Move(Project project, SubtitleTrack track, int delta)
    {
        ArgumentNullException.ThrowIfNull(project);
        var index = project.Tracks.IndexOf(track);
        var target = Math.Clamp(index + delta, 0, project.Tracks.Count - 1);
        if (index < 0 || target == index)
        {
            return;
        }

        project.Tracks.RemoveAt(index);
        project.Tracks.Insert(target, track);
    }

    /// <summary>
    /// Removes a track. Tracks derived from it are re-linked to its own source (their cues point to the
    /// same origin lines as before), or unlinked when it had none; bilingual mode and the active
    /// track stop referring to it.
    /// </summary>
    public static void Delete(Project project, SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var parentOf = track.Cues.Where(c => c.SourceCueId is not null).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().SourceCueId);
        foreach (var child in project.Tracks.Where(t => t.SourceTrackId == track.Id))
        {
            child.SourceTrackId = track.SourceTrackId;
            foreach (var cue in child.Cues)
            {
                cue.SourceCueId = track.SourceTrackId is not null && cue.SourceCueId is { } id && parentOf.TryGetValue(id, out var grand)
                    ? grand
                    : null;
            }
        }

        project.Tracks.Remove(track);
        if (project.Presentation.Bilingual.SecondaryTrackId == track.Id)
        {
            project.Presentation = project.Presentation with
            {
                Bilingual = project.Presentation.Bilingual with { Enabled = false, SecondaryTrackId = null },
            };
        }

        if (project.ActiveTrackId == track.Id)
        {
            project.ActiveTrackId = project.Tracks.FirstOrDefault()?.Id;
        }
    }

    /// <summary>
    /// Copies start and end times from the linked source lines (after the timing was fixed in the
    /// source track). Lines without a counterpart keep their times and are counted as skipped.
    /// </summary>
    public static (int Updated, int Skipped) SyncTimingFromSource(Project project, SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var source = project.Tracks.FirstOrDefault(t => t.Id == track.SourceTrackId);
        if (source is null)
        {
            return (0, track.Cues.Count);
        }

        var byId = source.Cues.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        int updated = 0, skipped = 0;
        foreach (var cue in track.Cues)
        {
            if (cue.SourceCueId is { } id && byId.TryGetValue(id, out var origin))
            {
                if (cue.Start != origin.Start || cue.End != origin.End)
                {
                    cue.Start = origin.Start;
                    cue.End = origin.End;
                    updated++;
                }
            }
            else
            {
                skipped++;
            }
        }

        track.Sort();
        return (updated, skipped);
    }
}
