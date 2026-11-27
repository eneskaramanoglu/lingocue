using System.Globalization;
using System.Text;
using LingoCue.Core.Fonts;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;

namespace LingoCue.Core.Formats;

/// <summary>
/// Builds the ASS script used for both the live preview (libmpv) and, later, burn-in (FFmpeg) —
/// the "preview = output" principle: both render this exact text with libass.
/// </summary>
public static class AssScriptBuilder
{
    /// <summary>Style sizes are authored against a 1080-pixel-high canvas.</summary>
    public const int ReferenceHeight = 1080;

    /// <summary>
    /// Script resolution for a video: height 1080, width following the display aspect ratio
    /// (1920×1080 for 16:9, 1080×1920 for 9:16 Shorts). Unknown size → 1920×1080.
    /// </summary>
    public static (int Width, int Height) PlayResFor(int videoWidth, int videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0)
        {
            return (1920, ReferenceHeight);
        }

        if (videoHeight >= videoWidth)
        {
            // Portrait: keep the short side at 1080 so font sizes read the same as on landscape.
            return (ReferenceHeight, (int)Math.Round(ReferenceHeight * (double)videoHeight / videoWidth));
        }

        return ((int)Math.Round(ReferenceHeight * (double)videoWidth / videoHeight), ReferenceHeight);
    }

    /// <summary>Name of the main style in generated scripts.</summary>
    public const string MainStyleName = "Default";

    /// <param name="allowBilingual">False for one-track-per-file outputs (e.g. MKV soft subtitles), where the second track is its own file.</param>
    /// <param name="measurer">Measures text for rounded block boxes; without it they are drawn square.</param>
    public static SubtitleDocument BuildDocument(Project project, SubtitleTrack track, int videoWidth = 0, int videoHeight = 0, bool allowBilingual = true, ITextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var (x, y) = PlayResFor(videoWidth, videoHeight);
        var presentation = project.Presentation;
        var main = project.Style with { Name = MainStyleName };

        var culture = CultureFor(track.Language);
        var cues = track.Cues.Where(c => c.End > c.Start).ToList();
        var secondaryTrack = allowBilingual ? SecondaryTrackFor(project, track) : null;
        var sink = new EventSink(x, y, measurer);
        var styles = new List<SubtitleStyle>(StylesFor(main, measurer));

        if (secondaryTrack is null)
        {
            foreach (var cue in cues)
            {
                // Every cue uses the project style; layer and margins of imported cues are kept.
                AddEvents(sink, cue, cue.Format with { Style = null }, presentation, main, CultureOf(cue, culture));
            }
        }
        else
        {
            var bilingual = presentation.Bilingual;
            var secondary = bilingual.SecondaryStyle with
            {
                Name = SecondaryStyleName,
                Alignment = main.Alignment,
                MarginL = main.MarginL,
                MarginR = main.MarginR,
            };
            styles.AddRange(StylesFor(secondary, measurer));
            AddBilingualEvents(sink, cues, secondaryTrack, main, secondary, bilingual.Gap, presentation, culture, x);
        }

        return new SubtitleDocument
        {
            ScriptInfo = project.ScriptInfo with { PlayResX = x, PlayResY = y, Title = project.ScriptInfo.Title ?? "LingoCue" },
            Styles = styles,
            Cues = sink.Result(),
        };
    }

    /// <summary>Name of the second track's style in bilingual mode.</summary>
    public const string SecondaryStyleName = "Secondary";

    /// <summary>The second track for bilingual mode, or null when the mode is off or the track is missing.</summary>
    public static SubtitleTrack? SecondaryTrackFor(Project project, SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var bilingual = project.Presentation.Bilingual;
        return bilingual.Enabled && bilingual.SecondaryTrackId is { } id && id != track.Id
            ? project.Tracks.FirstOrDefault(t => t.Id == id)
            : null;
    }

    /// <summary>
    /// Main track above, second track below (at the bottom; mirrored for top alignments). The two
    /// tracks sit on different layers so libass never pushes one around; instead each event's
    /// vertical margin is computed from the height of the other track's text shown at the same time.
    /// </summary>
    private static void AddBilingualEvents(
        EventSink events, List<Cue> mainCues, SubtitleTrack secondaryTrack, SubtitleStyle main, SubtitleStyle secondary,
        int gap, PresentationOptions presentation, CultureInfo culture, int playResX)
    {
        var secondaryCues = secondaryTrack.Cues.Where(c => c.End > c.Start).ToList();
        var secondaryCulture = CultureFor(secondaryTrack.Language);
        var position = VerticalPosition(main.Alignment);
        var plain = presentation with { Karaoke = new KaraokeOptions() };

        foreach (var cue in mainCues)
        {
            var below = position == Vertical.Bottom
                ? MaxHeight(Overlapping(secondaryCues, cue), secondary, playResX)
                : 0;
            var marginV = below > 0 ? main.MarginV + below + gap : 0;
            AddEvents(events, cue, new CueFormatData { Layer = 1, MarginV = marginV }, presentation, main, CultureOf(cue, culture));
        }

        foreach (var cue in secondaryCues)
        {
            var marginV = position switch
            {
                Vertical.Bottom => main.MarginV,
                Vertical.Top => main.MarginV + MaxHeight(Overlapping(mainCues, cue), main, playResX) + gap,
                _ => 0,
            };
            var format = new CueFormatData { Style = SecondaryStyleName, Layer = 0, MarginV = marginV };
            AddEvents(events, cue, format, plain, secondary, CultureOf(cue, secondaryCulture));
        }
    }

    private enum Vertical
    {
        Bottom,
        Middle,
        Top,
    }

    // Numpad layout: 1–3 bottom, 4–6 middle, 7–9 top.
    private static Vertical VerticalPosition(int alignment) => alignment switch
    {
        >= 7 => Vertical.Top,
        >= 4 => Vertical.Middle,
        _ => Vertical.Bottom,
    };

    private static IEnumerable<Cue> Overlapping(IEnumerable<Cue> cues, Cue cue)
        => cues.Where(c => c.Start < cue.End && c.End > cue.Start);

    private static int MaxHeight(IEnumerable<Cue> cues, SubtitleStyle style, int playResX)
        => cues.Select(c => TextHeight(c.Text, style, playResX)).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Estimated rendered height of a text block: libass makes one line about <c>FontSize</c> high;
    /// long lines are assumed to wrap at the available width (average glyph ≈ 0.5 em).
    /// </summary>
    internal static int TextHeight(string text, SubtitleStyle style, int playResX)
    {
        var size = style.FontSize * style.ScaleY / 100;
        var background = style.Background;
        var edge = background.Kind == BackgroundKind.None
            ? style.Outline
            : background.PaddingY + (background.HasFrame ? background.FrameWidth : 0) + (background.Kind == BackgroundKind.BlockBox ? style.Outline : 0);
        return (int)Math.Ceiling((LineCount(text, style, playResX) * size) + (2 * edge));
    }

    /// <summary>Estimated number of rendered lines, counting the wrapping of long lines.</summary>
    internal static int LineCount(string text, SubtitleStyle style, int playResX)
    {
        var size = style.FontSize * style.ScaleY / 100;
        var available = Math.Max(1, playResX - style.MarginL - style.MarginR);
        var averageGlyph = size * 0.5 * style.ScaleX / 100;
        return SubtitleText.StripTags(text).Split('\n')
            .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length * averageGlyph / available)));
    }

    /// <summary>Events for one cue: plain, karaoke fill (one event) or active word (one event per word).</summary>
    private static void AddEvents(EventSink sink, Cue cue, CueFormatData format, PresentationOptions presentation, SubtitleStyle style, CultureInfo culture)
    {
        string prefix;
        if (RoundedBoxDrawing(sink, cue, format, presentation, style, culture) is { } drawing)
        {
            // The text uses the plain style; the rounded box is a shape one layer below.
            sink.AddUnder(Event(cue, drawing + FadeTag(presentation.FadeInMs, presentation.FadeOutMs), cue.Start, cue.End, format with { MarginV = 0 }));
            prefix = string.Empty;
        }
        else
        {
            if (IsRounded(style, sink.Measurer))
            {
                format = format with { Style = BoxStyleName(format.Style ?? MainStyleName) }; // cannot measure: the square libass box
            }

            AddBackground(sink, cue, format, presentation, style, culture);
            prefix = BoxTags(style.Background);
        }

        var karaoke = presentation.Karaoke;
        var timed = karaoke.Mode == KaraokeMode.Off ? null : KaraokeTiming.Align(cue);
        if (timed is null)
        {
            sink.Add(Event(cue, prefix + Decorate(cue.Text, presentation, culture), cue.Start, cue.End, format));
            return;
        }

        if (karaoke.Mode == KaraokeMode.Fill)
        {
            sink.Add(Event(cue, prefix + Decorate(FillText(cue, timed, karaoke.HighlightColor, style.PrimaryColor), presentation, culture), cue.Start, cue.End, format));
            return;
        }

        // Active word: the text before the first word, then one event per word until the next one.
        var segments = new List<(TimeSpan Start, TimeSpan End, int Active)>();
        if (timed[0].Start > cue.Start)
        {
            segments.Add((cue.Start, timed[0].Start, -1));
        }

        for (var k = 0; k < timed.Count; k++)
        {
            var end = k + 1 < timed.Count ? timed[k + 1].Start : cue.End;
            if (end > timed[k].Start)
            {
                segments.Add((timed[k].Start, end, k));
            }
        }

        for (var s = 0; s < segments.Count; s++)
        {
            var (start, end, active) = segments[s];
            var fades = presentation with
            {
                FadeInMs = s == 0 ? presentation.FadeInMs : 0,
                FadeOutMs = s == segments.Count - 1 ? presentation.FadeOutMs : 0,
            };
            var text = ActiveWordText(timed, active, karaoke.HighlightColor, style.PrimaryColor);
            sink.Add(Event(cue, prefix + Decorate(text, fades, culture), start, end, format));
        }
    }

    /// <summary><c>\kf</c> karaoke: unspoken words in the style color, spoken ones in the highlight.</summary>
    internal static string FillText(Cue cue, IReadOnlyList<TimedToken> timed, AssColor highlight, AssColor primary)
    {
        // \kf sweeps from the secondary color (\2c, not yet spoken) to the primary color (\1c, spoken).
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $@"{{\1c{highlight.ToAssOverride()}\2c{primary.ToAssOverride()}}}");
        var elapsed = Centiseconds(timed[0].Start - cue.Start);
        if (elapsed > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $@"{{\k{elapsed}}}");
        }

        for (var k = 0; k < timed.Count; k++)
        {
            var end = Centiseconds((k + 1 < timed.Count ? timed[k + 1].Start : cue.End) - cue.Start);
            sb.Append(timed[k].Separator);
            sb.Append(CultureInfo.InvariantCulture, $@"{{\kf{Math.Max(0, end - elapsed)}}}");
            sb.Append(timed[k].Text);
            elapsed = Math.Max(elapsed, end);
        }

        return sb.ToString();
    }

    internal static string ActiveWordText(IReadOnlyList<TimedToken> timed, int active, AssColor highlight, AssColor primary)
    {
        var sb = new StringBuilder();
        for (var k = 0; k < timed.Count; k++)
        {
            sb.Append(timed[k].Separator);
            if (k == active)
            {
                sb.Append(@"{\1c").Append(highlight.ToAssOverride()).Append('}')
                  .Append(timed[k].Text)
                  .Append(@"{\1c").Append(primary.ToAssOverride()).Append('}');
            }
            else
            {
                sb.Append(timed[k].Text);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The ASS style line for a style with a LingoCue background: a line box is BorderStyle 3 painted
    /// with the box color, a block box is the libass BorderStyle 4 rectangle painted with BackColour.
    /// The exact padding is set per event (<see cref="BoxTags"/>); the style values are a fallback.
    /// </summary>
    internal static SubtitleStyle EffectiveStyle(SubtitleStyle style) => style.Background.Kind switch
    {
        BackgroundKind.LineBox => style with
        {
            BorderStyle = AssBorderStyle.OpaqueBox,
            OutlineColor = style.Background.Color,
            Outline = Math.Max(style.Background.PaddingX, style.Background.PaddingY),
            Shadow = 0,
        },
        BackgroundKind.BlockBox => style with
        {
            BorderStyle = AssBorderStyle.Box,
            BackColor = style.Background.Color,
            Shadow = style.Background.PaddingY,
        },
        _ => style,
    };

    /// <summary>
    /// Override tags giving the box its horizontal and vertical padding: line boxes grow with the
    /// border size, the BorderStyle 4 rectangle with the shadow size.
    /// </summary>
    internal static string BoxTags(SubtitleBackground background, double extra = 0) => background.Kind switch
    {
        BackgroundKind.LineBox => $@"{{\xbord{Num(background.PaddingX + extra)}\ybord{Num(background.PaddingY + extra)}}}",
        BackgroundKind.BlockBox => $@"{{\xshad{Num(background.PaddingX + extra)}\yshad{Num(background.PaddingY + extra)}}}",
        _ => string.Empty,
    };

    /// <summary>
    /// Events drawn under a cue's text: the frame (a copy of the text with invisible glyphs whose box
    /// is larger by the frame width, painted in the frame color) or the full-width band.
    /// </summary>
    private static void AddBackground(EventSink sink, Cue cue, CueFormatData format, PresentationOptions presentation, SubtitleStyle style, CultureInfo culture)
    {
        var background = style.Background;
        var plain = presentation with { Karaoke = new KaraokeOptions() };
        switch (background.Kind)
        {
            case BackgroundKind.LineBox when background.HasFrame:
                var lineFrame = $@"{{\1a&HFF&\2a&HFF&\3c{background.FrameColor.ToAssOverride()}\3a{Alpha(background.FrameColor)}\shad0}}";
                sink.AddUnder(Event(cue, lineFrame + BoxTags(background, background.FrameWidth) + Decorate(cue.Text, plain, culture), cue.Start, cue.End, format));
                break;
            case BackgroundKind.BlockBox when background.HasFrame:
                var blockFrame = $@"{{\1a&HFF&\2a&HFF&\3a&HFF&\4c{background.FrameColor.ToAssOverride()}\4a{Alpha(background.FrameColor)}}}";
                sink.AddUnder(Event(cue, blockFrame + BoxTags(background, background.FrameWidth) + Decorate(cue.Text, plain, culture), cue.Start, cue.End, format));
                break;
            case BackgroundKind.Band:
                sink.AddUnder(Event(cue, BandDrawing(cue.Text, format, style, sink.Width, sink.Height) + FadeTag(presentation.FadeInMs, presentation.FadeOutMs),
                    cue.Start, cue.End, format with { MarginV = 0 }));
                break;
        }
    }

    /// <summary>A full-width rectangle (ASS vector drawing) as high as the text plus the vertical padding.</summary>
    internal static string BandDrawing(string text, CueFormatData format, SubtitleStyle style, int playResX, int playResY)
    {
        var background = style.Background;
        var size = style.FontSize * style.ScaleY / 100;
        var block = LineCount(text, style, playResX) * size;
        var height = block + (2 * background.PaddingY);
        var marginV = format.MarginV > 0 ? format.MarginV : style.MarginV;
        var top = VerticalPosition(style.Alignment) switch
        {
            Vertical.Top => marginV - background.PaddingY,
            Vertical.Middle => (playResY - height) / 2,
            _ => playResY - marginV - block - background.PaddingY,
        };
        var frame = background.HasFrame
            ? $@"\bord{Num(background.FrameWidth)}\3c{background.FrameColor.ToAssOverride()}\3a{Alpha(background.FrameColor)}"
            : @"\bord0";
        return $@"{{\an7\pos(0,{Num(top)}){frame}\shad0\blur0\1c{background.Color.ToAssOverride()}\1a{Alpha(background.Color)}\p1}}"
             + $"m 0 0 l {playResX} 0 l {playResX} {Num(height)} l 0 {Num(height)}{{\\p0}}";
    }

    // ---------------------------------------------------------------- rounded block box

    private static bool IsRounded(SubtitleStyle style, ITextMeasurer? measurer)
        => measurer is not null && style.Background is { Kind: BackgroundKind.BlockBox, CornerRadius: > 0 };

    /// <summary>Name of the square-box fallback style for lines a rounded box cannot be measured for.</summary>
    internal static string BoxStyleName(string style) => style + " Box";

    /// <summary>
    /// Styles written for <paramref name="style"/>: one, or with a rounded block box two: the text style
    /// (no libass box; the shape is drawn under it) and the square BorderStyle 4 fallback.
    /// </summary>
    internal static IEnumerable<SubtitleStyle> StylesFor(SubtitleStyle style, ITextMeasurer? measurer)
    {
        if (!IsRounded(style, measurer))
        {
            yield return EffectiveStyle(style);
            yield break;
        }

        yield return style with { BorderStyle = AssBorderStyle.Outline };
        yield return EffectiveStyle(style) with { Name = BoxStyleName(style.Name) };
    }

    /// <summary>
    /// A rounded rectangle (ASS vector drawing) around the measured text block, or null when the line
    /// cannot be measured or is wider than the space between the margins (libass would wrap it, and the
    /// box would no longer fit).
    /// </summary>
    private static string? RoundedBoxDrawing(EventSink sink, Cue cue, CueFormatData format, PresentationOptions presentation, SubtitleStyle style, CultureInfo culture)
    {
        if (!IsRounded(style, sink.Measurer))
        {
            return null;
        }

        var text = SubtitleText.StripTags(cue.Text);
        if (presentation.UpperCase)
        {
            text = text.ToUpper(culture);
        }

        var available = sink.Width - style.MarginL - style.MarginR;
        if (sink.Measurer!.LongestLineWidth(text, style) is not { } width || width <= 0 || width > available)
        {
            return null;
        }

        var background = style.Background;
        var lineHeight = style.FontSize * style.ScaleY / 100;
        var lines = text.Split('\n').Length;
        var edge = style.Outline; // libass pads its box from the glyph border too
        var w = width + (2 * (background.PaddingX + edge));
        var h = (lines * lineHeight) + (2 * (background.PaddingY + edge));
        var marginV = format.MarginV > 0 ? format.MarginV : style.MarginV;
        var left = (style.Alignment % 3) switch
        {
            1 => style.MarginL - background.PaddingX - edge,
            0 => sink.Width - style.MarginR - width - background.PaddingX - edge,
            _ => style.MarginL + ((available - width) / 2) - background.PaddingX - edge,
        };
        var top = VerticalPosition(style.Alignment) switch
        {
            Vertical.Top => marginV - background.PaddingY - edge,
            Vertical.Middle => (sink.Height - h) / 2,
            _ => sink.Height - marginV - (lines * lineHeight) - background.PaddingY - edge,
        };
        var frame = background.HasFrame
            ? $@"\bord{Num(background.FrameWidth)}\3c{background.FrameColor.ToAssOverride()}\3a{Alpha(background.FrameColor)}"
            : @"\bord0";
        return $@"{{\an7\pos({Num(left)},{Num(top)}){frame}\shad0\blur0\1c{background.Color.ToAssOverride()}\1a{Alpha(background.Color)}\p1}}"
               + RoundedRectangle(w, h, background.CornerRadius) + @"{\p0}";
    }

    /// <summary>ASS drawing commands for a rectangle with quarter-circle corners (cubic Bézier approximation).</summary>
    internal static string RoundedRectangle(double w, double h, double radius)
    {
        var r = Math.Clamp(radius, 0, Math.Min(w, h) / 2);
        var k = r * 0.4477; // 1 − 0.5523: control point distance from the corner for a circular arc
        return string.Create(CultureInfo.InvariantCulture,
            $"m {Num(r)} 0 l {Num(w - r)} 0 b {Num(w - k)} 0 {Num(w)} {Num(k)} {Num(w)} {Num(r)} " +
            $"l {Num(w)} {Num(h - r)} b {Num(w)} {Num(h - k)} {Num(w - k)} {Num(h)} {Num(w - r)} {Num(h)} " +
            $"l {Num(r)} {Num(h)} b {Num(k)} {Num(h)} 0 {Num(h - k)} 0 {Num(h - r)} " +
            $"l 0 {Num(r)} b 0 {Num(k)} {Num(k)} 0 {Num(r)} 0");
    }

    private static string Alpha(AssColor color) => string.Create(CultureInfo.InvariantCulture, $"&H{255 - color.A:X2}&");

    private static string Num(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Collects text events and the background events drawn under them. With any background event,
    /// layers are spread out (text 2L+1, background 2L) so a box never shares a layer with text:
    /// libass would push events on the same layer apart.
    /// </summary>
    private sealed class EventSink(int width, int height, ITextMeasurer? measurer = null)
    {
        public ITextMeasurer? Measurer => measurer;

        private readonly List<Cue> _text = [];
        private readonly List<Cue> _under = [];

        public int Width => width;

        public int Height => height;

        public void Add(Cue cue) => _text.Add(cue);

        public void AddUnder(Cue cue) => _under.Add(cue);

        public List<Cue> Result()
        {
            if (_under.Count == 0)
            {
                return _text;
            }

            foreach (var cue in _under)
            {
                cue.Format = cue.Format with { Layer = 2 * cue.Format.Layer };
            }

            foreach (var cue in _text)
            {
                cue.Format = cue.Format with { Layer = (2 * cue.Format.Layer) + 1 };
            }

            return [.. _under, .. _text];
        }
    }

    private static int Centiseconds(TimeSpan t) => (int)Math.Round(t.TotalMilliseconds / 10, MidpointRounding.AwayFromZero);

    /// <summary>Applies upper case and fades to a cue's model text (tags are kept).</summary>
    internal static string Decorate(string text, PresentationOptions presentation, CultureInfo culture)
    {
        if (presentation.UpperCase)
        {
            text = SubtitleText.MapVisible(text, s => s.ToUpper(culture));
        }

        return FadeTag(presentation.FadeInMs, presentation.FadeOutMs) + text;
    }

    internal static string FadeTag(int inMs, int outMs)
        => inMs > 0 || outMs > 0
            ? string.Create(CultureInfo.InvariantCulture, $@"{{\fad({Math.Max(0, inMs)},{Math.Max(0, outMs)})}}")
            : string.Empty;

    /// <summary>Culture for casing a track's text; Turkish rules for <c>tr</c> (i → İ).</summary>
    /// <summary>A line in its own language (mixed tracks) is cased by that language: "this" stays "THIS", not "THİS".</summary>
    private static CultureInfo CultureOf(Cue cue, CultureInfo trackCulture)
        => string.IsNullOrWhiteSpace(cue.Language) ? trackCulture : CultureFor(cue.Language);

    internal static CultureInfo CultureFor(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
        {
            return TurkishText.Culture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return TurkishText.Culture;
        }
    }

    private static Cue Event(Cue source, string text, TimeSpan start, TimeSpan end, CueFormatData format)
        => new(source.Id, start, end, text) { Speaker = source.Speaker, Marks = source.Marks, Format = format };

    public static string Build(Project project, SubtitleTrack track, int videoWidth = 0, int videoHeight = 0, bool allowBilingual = true, ITextMeasurer? measurer = null)
        => SubtitleFormats.Ass.Write(BuildDocument(project, track, videoWidth, videoHeight, allowBilingual, measurer));

    /// <summary>
    /// Brings a style authored for another script height (e.g. an imported 720p ASS) to the
    /// 1080-reference canvas so it looks the same size.
    /// </summary>
    public static SubtitleStyle NormalizeStyle(SubtitleStyle style, int sourcePlayResY)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (sourcePlayResY <= 0 || sourcePlayResY == ReferenceHeight)
        {
            return style;
        }

        var f = (double)ReferenceHeight / sourcePlayResY;
        return style with
        {
            FontSize = Math.Round(style.FontSize * f, 1),
            Outline = Math.Round(style.Outline * f, 2),
            Shadow = Math.Round(style.Shadow * f, 2),
            Spacing = Math.Round(style.Spacing * f, 2),
            MarginL = (int)Math.Round(style.MarginL * f),
            MarginR = (int)Math.Round(style.MarginR * f),
            MarginV = (int)Math.Round(style.MarginV * f),
            Background = style.Background with
            {
                PaddingX = Math.Round(style.Background.PaddingX * f, 2),
                PaddingY = Math.Round(style.Background.PaddingY * f, 2),
                FrameWidth = Math.Round(style.Background.FrameWidth * f, 2),
                CornerRadius = Math.Round(style.Background.CornerRadius * f, 2),
            },
        };
    }
}
