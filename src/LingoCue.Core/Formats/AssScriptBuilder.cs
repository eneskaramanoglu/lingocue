using System.Globalization;
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

        var events = new List<Cue>();
        foreach (var cue in track.Cues.Where(c => c.End > c.Start))
        {
            // Every cue uses the project style; layer and margins of imported cues are kept.
            var text = Decorate(cue.Text, presentation, CultureFor(track.Language));
            events.Add(Event(cue, text, cue.Start, cue.End, cue.Format with { Style = null }));
        }

        return new SubtitleDocument
        {
            ScriptInfo = project.ScriptInfo with { PlayResX = x, PlayResY = y, Title = project.ScriptInfo.Title ?? "LingoCue" },
            Styles = [main],
            Cues = events,
        };
    }

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
