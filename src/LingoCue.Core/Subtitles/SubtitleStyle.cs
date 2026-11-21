namespace LingoCue.Core.Subtitles;

public enum AssBorderStyle
{
    /// <summary>Outline + drop shadow.</summary>
    Outline = 1,

    /// <summary>Opaque box behind the text; libass paints the box with <see cref="SubtitleStyle.OutlineColor"/> and its shadow with <see cref="SubtitleStyle.BackColor"/>.</summary>
    OpaqueBox = 3,

    /// <summary>
    /// libass extension: one rectangle behind the whole event (all lines), filled with
    /// <see cref="SubtitleStyle.BackColor"/>, padded by the shadow size; the text keeps its outline.
    /// </summary>
    Box = 4,
}

/// <summary>What LingoCue draws behind the text. Turned into ASS by <c>AssScriptBuilder</c>.</summary>
public enum BackgroundKind
{
    /// <summary>Nothing added; the style's own border style is used as is.</summary>
    None,

    /// <summary>A box behind each line (ASS BorderStyle 3); the text loses its outline.</summary>
    LineBox,

    /// <summary>One rectangle behind all lines (libass BorderStyle 4); the text keeps its outline.</summary>
    BlockBox,

    /// <summary>A band across the full video width, as high as the text.</summary>
    Band,
}

/// <summary>Background box settings. Sizes are script pixels on the 1080-high reference canvas.</summary>
public sealed record SubtitleBackground
{
    public BackgroundKind Kind { get; init; }

    /// <summary>Box fill; alpha sets the opacity.</summary>
    public AssColor Color { get; init; } = new(0, 0, 0, 160);

    /// <summary>Space between the text and the box edge, left and right.</summary>
    public double PaddingX { get; init; } = 18;

    /// <summary>Space between the text and the box edge, above and below.</summary>
    public double PaddingY { get; init; } = 8;

    /// <summary>Frame line around the box; 0 = no frame.</summary>
    public double FrameWidth { get; init; }

    public AssColor FrameColor { get; init; } = AssColor.White;

    /// <summary>Rounded corners of the block box, in script pixels; 0 = square (the plain libass box).</summary>
    public double CornerRadius { get; init; }

    public bool HasFrame => Kind != BackgroundKind.None && FrameWidth > 0;

    /// <summary>
    /// The style's background, reading a plain ASS box (BorderStyle 3 painted with the outline color,
    /// or BorderStyle 4 painted with the back color and padded by the shadow) as the matching background.
    /// </summary>
    public static SubtitleBackground Of(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style switch
        {
            { Background.Kind: not BackgroundKind.None } => style.Background,
            { BorderStyle: AssBorderStyle.OpaqueBox } => new SubtitleBackground
            {
                Kind = BackgroundKind.LineBox, Color = style.OutlineColor, PaddingX = style.Outline, PaddingY = style.Outline,
            },
            { BorderStyle: AssBorderStyle.Box } => new SubtitleBackground
            {
                Kind = BackgroundKind.BlockBox, Color = style.BackColor, PaddingX = style.Shadow, PaddingY = style.Shadow,
            },
            _ => style.Background,
        };
    }
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

    /// <summary>LingoCue background box; not part of the ASS style line (written as override tags and extra events).</summary>
    public SubtitleBackground Background { get; init; } = new();
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
