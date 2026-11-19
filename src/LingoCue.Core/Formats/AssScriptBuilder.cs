using System.Globalization;
using System.Text;
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

    public static SubtitleDocument BuildDocument(Project project, SubtitleTrack track, int videoWidth = 0, int videoHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var (x, y) = PlayResFor(videoWidth, videoHeight);
        var presentation = project.Presentation;
        var main = project.Style with { Name = MainStyleName };

        var culture = CultureFor(track.Language);
        var events = new List<Cue>();
        foreach (var cue in track.Cues.Where(c => c.End > c.Start))
        {
            // Every cue uses the project style; layer and margins of imported cues are kept.
            AddEvents(events, cue, cue.Format with { Style = null }, presentation, main, culture);
        }

        return new SubtitleDocument
        {
            ScriptInfo = project.ScriptInfo with { PlayResX = x, PlayResY = y, Title = project.ScriptInfo.Title ?? "LingoCue" },
            Styles = [main],
            Cues = events,
        };
    }

    /// <summary>Events for one cue: plain, karaoke fill (one event) or active word (one event per word).</summary>
    private static void AddEvents(List<Cue> events, Cue cue, CueFormatData format, PresentationOptions presentation, SubtitleStyle style, CultureInfo culture)
    {
        var karaoke = presentation.Karaoke;
        var timed = karaoke.Mode == KaraokeMode.Off ? null : KaraokeTiming.Align(cue);
        if (timed is null)
        {
            events.Add(Event(cue, Decorate(cue.Text, presentation, culture), cue.Start, cue.End, format));
            return;
        }

        if (karaoke.Mode == KaraokeMode.Fill)
        {
            events.Add(Event(cue, Decorate(FillText(cue, timed, karaoke.HighlightColor, style.PrimaryColor), presentation, culture), cue.Start, cue.End, format));
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
            events.Add(Event(cue, Decorate(text, fades, culture), start, end, format));
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

    public static string Build(Project project, SubtitleTrack track, int videoWidth = 0, int videoHeight = 0)
        => SubtitleFormats.Ass.Write(BuildDocument(project, track, videoWidth, videoHeight));

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
        };
    }
}
