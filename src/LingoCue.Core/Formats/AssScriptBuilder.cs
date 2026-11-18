using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;

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

    public static SubtitleDocument BuildDocument(Project project, SubtitleTrack track, int videoWidth = 0, int videoHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var (x, y) = PlayResFor(videoWidth, videoHeight);
        var style = project.Style with { Name = "Default" };

        return new SubtitleDocument
        {
            ScriptInfo = project.ScriptInfo with { PlayResX = x, PlayResY = y, Title = project.ScriptInfo.Title ?? "LingoCue" },
            Styles = [style],
            Cues = [.. track.Cues.Where(c => c.End > c.Start).Select(UseProjectStyle)],
        };

        // Until the style editor supports several styles, every cue uses the project style.
        static Cue UseProjectStyle(Cue cue)
        {
            var copy = cue.Clone();
            copy.Format = copy.Format with { Style = null };
            return copy;
        }
    }

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
