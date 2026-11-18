namespace LingoCue.Core.Subtitles;

[Flags]
public enum CueMarks
{
    None = 0,

    /// <summary>A person should look at this line (e.g. low ASR confidence).</summary>
    NeedsReview = 1,

    /// <summary>The LLM pass could not produce a valid result for this line.</summary>
    LlmFailed = 2,

    /// <summary>Protected from automatic passes (correction/translation won't change it).</summary>
    Locked = 4,
}

/// <summary>
/// One subtitle event. Mutable on purpose: the editor changes cues in place.
/// Times are kept with millisecond precision. Line breaks in <see cref="Text"/> are <c>\n</c>.
/// </summary>
public sealed class Cue
{
    public Cue(int id, TimeSpan start, TimeSpan end, string text)
    {
        Id = id;
        Start = start;
        End = end;
        Text = text;
    }

    /// <summary>Stable id inside its track; used to key LLM batches and to link translations.</summary>
    public int Id { get; set; }

    public TimeSpan Start { get; set; }

    public TimeSpan End { get; set; }

    public string Text { get; set; }

    public string? Speaker { get; set; }

    public CueMarks Marks { get; set; }

    /// <summary>For translated/corrected tracks: id of the cue in the track it was derived from.</summary>
    public int? SourceCueId { get; set; }

    /// <summary>ASR words with timing (cached; used for segmentation and karaoke).</summary>
    public List<Word> Words { get; init; } = [];

    /// <summary>Format-specific data kept for lossless round-trips (ASS layer/style/margins, VTT settings).</summary>
    public CueFormatData Format { get; set; } = CueFormatData.Empty;

    public TimeSpan Duration => End - Start;

    /// <summary>Visible characters (no line breaks or ASS/HTML tags).</summary>
    public int VisibleLength => SubtitleText.VisibleLength(Text);

    /// <summary>Characters per second, the reading-speed metric. 0 for zero-length cues.</summary>
    public double CharactersPerSecond => Duration.TotalSeconds > 0 ? VisibleLength / Duration.TotalSeconds : 0;

    public bool Overlaps(Cue other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start < other.End && other.Start < End;
    }

    public bool Contains(TimeSpan position) => position >= Start && position < End;

    public void Shift(TimeSpan offset)
    {
        Start = Clamp(Start + offset);
        End = Clamp(End + offset);
    }

    public Cue Clone(int? newId = null) => new(newId ?? Id, Start, End, Text)
    {
        Speaker = Speaker,
        Marks = Marks,
        SourceCueId = SourceCueId,
        Words = [.. Words],
        Format = Format,
    };

    public override string ToString() => $"#{Id} {Start}–{End} {Text}";

    private static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}

/// <summary>Per-cue fields that only some formats carry.</summary>
public sealed record CueFormatData
{
    public static CueFormatData Empty { get; } = new();

    /// <summary>ASS style name; null means the default style.</summary>
    public string? Style { get; init; }

    public int Layer { get; init; }

    public int MarginL { get; init; }

    public int MarginR { get; init; }

    public int MarginV { get; init; }

    public string? Effect { get; init; }

    /// <summary>Raw WebVTT cue settings, e.g. <c>line:90% align:center</c>.</summary>
    public string? VttSettings { get; init; }

    /// <summary>WebVTT cue identifier line, if any.</summary>
    public string? VttIdentifier { get; init; }
}

/// <summary>A recognized word with timing, as produced by the ASR engine.</summary>
public sealed record Word(string Text, TimeSpan Start, TimeSpan End, double? Confidence = null, string? Speaker = null);
