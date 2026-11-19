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
}
