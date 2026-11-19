namespace LingoCue.Core.Subtitles;

public enum AssBorderStyle
{
    /// <summary>Outline + drop shadow.</summary>
    Outline = 1,

    /// <summary>Opaque box behind the text; libass paints the box with <see cref="SubtitleStyle.OutlineColor"/> and its shadow with <see cref="SubtitleStyle.BackColor"/>.</summary>
    OpaqueBox = 3,
}

/// <summary>
/// The ASS V4+ style: the single source of truth for how subtitles look, both in the preview
/// (libmpv/libass) and in burn-in (FFmpeg/libass). Field names follow the ASS spec.
/// </summary>
public sealed record SubtitleStyle
{
    public string Name { get; init; } = "Default";

    public string FontName { get; init; } = "Arial";

    public double FontSize { get; init; } = 48;

    public AssColor PrimaryColor { get; init; } = AssColor.White;

    /// <summary>Karaoke "not yet sung" color.</summary>
    public AssColor SecondaryColor { get; init; } = AssColor.Yellow;

    public AssColor OutlineColor { get; init; } = AssColor.Black;

    /// <summary>Shadow color, or box color with <see cref="AssBorderStyle.OpaqueBox"/>.</summary>
    public AssColor BackColor { get; init; } = new(0, 0, 0, 128);

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public bool StrikeOut { get; init; }

    public double ScaleX { get; init; } = 100;

    public double ScaleY { get; init; } = 100;

    public double Spacing { get; init; }

    public double Angle { get; init; }

    public AssBorderStyle BorderStyle { get; init; } = AssBorderStyle.Outline;

    public double Outline { get; init; } = 2.5;

    public double Shadow { get; init; } = 1;

    /// <summary>Numpad alignment: 1-3 bottom, 4-6 middle, 7-9 top (2 = bottom center).</summary>
    public int Alignment { get; init; } = 2;

    public int MarginL { get; init; } = 60;

    public int MarginR { get; init; } = 60;

    public int MarginV { get; init; } = 40;

    /// <summary>Font charset; 1 = default. 162 is Turkish but libass ignores it for Unicode text.</summary>
    public int Encoding { get; init; } = 1;
}

/// <summary>The <c>[Script Info]</c> section of an ASS file.</summary>
public sealed record AssScriptInfo
{
    public string? Title { get; init; }

    /// <summary>Coordinate space of the script; styles and margins are expressed in these units.</summary>
    public int PlayResX { get; init; } = 1920;

    public int PlayResY { get; init; } = 1080;

    /// <summary>0 = smart wrapping (top line wider), 2 = no wrapping.</summary>
    public int WrapStyle { get; init; }

    public bool ScaledBorderAndShadow { get; init; } = true;

    /// <summary>Other key/value lines, kept in order for round-trips.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Extra { get; init; } = [];
}
