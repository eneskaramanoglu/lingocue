namespace LingoCue.Core.Subtitles;

public enum TrackKind
{
    /// <summary>Straight from ASR (or an imported file) in the spoken language.</summary>
    Source,

    /// <summary>Source language after the LLM correction pass and/or manual edits.</summary>
    Corrected,

    /// <summary>Translation into another language; timing copied from its source track.</summary>
    Translation,
}

/// <summary>An ordered list of cues in one language.</summary>
public sealed class SubtitleTrack
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Altyazı";

    /// <summary>BCP 47 language code, e.g. <c>tr</c>, <c>en</c>.</summary>
    public string Language { get; set; } = "tr";

    public TrackKind Kind { get; set; } = TrackKind.Source;

    /// <summary>The track this one was derived from (correction/translation).</summary>
    public Guid? SourceTrackId { get; set; }

    public List<Cue> Cues { get; init; } = [];

    public int NextCueId() => Cues.Count == 0 ? 1 : Cues.Max(c => c.Id) + 1;

    public Cue AddCue(TimeSpan start, TimeSpan end, string text)
    {
        var cue = new Cue(NextCueId(), start, end, text);
        Cues.Add(cue);
        return cue;
    }

    /// <summary>Stable sort by start time, then end time.</summary>
    public void Sort()
    {
        var ordered = Cues.OrderBy(c => c.Start).ThenBy(c => c.End).ToList();
        Cues.Clear();
        Cues.AddRange(ordered);
    }

    /// <summary>Gives every cue a fresh id 1..n in the current order.</summary>
    public void Renumber()
    {
        for (var i = 0; i < Cues.Count; i++)
        {
            Cues[i].Id = i + 1;
        }
    }

    /// <summary>The cue shown at <paramref name="position"/>; the latest-starting one when cues overlap.</summary>
    public Cue? CueAt(TimeSpan position)
    {
        Cue? best = null;
        foreach (var cue in Cues)
        {
            if (cue.Contains(position) && (best is null || cue.Start >= best.Start))
            {
                best = cue;
            }
        }

        return best;
    }

    public void ShiftAll(TimeSpan offset)
    {
        foreach (var cue in Cues)
        {
            cue.Shift(offset);
        }
    }

    /// <summary>Pairs of neighbouring cues (in start order) that overlap in time.</summary>
    public IEnumerable<(Cue First, Cue Second)> FindOverlaps()
    {
        var ordered = Cues.OrderBy(c => c.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i - 1].Overlaps(ordered[i]))
            {
                yield return (ordered[i - 1], ordered[i]);
            }
        }
    }
}
