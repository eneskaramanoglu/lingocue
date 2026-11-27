using System.Globalization;
using LingoCue.Media.Probing;

namespace LingoCue.Media.Rendering;

public enum VideoEncoder
{
    /// <summary>libx264 on the CPU: best quality per size, works everywhere.</summary>
    X264,

    /// <summary>NVIDIA NVENC (Windows): much faster, slightly larger files.</summary>
    Nvenc,

    /// <summary>Apple VideoToolbox (macOS): much faster, slightly larger files.</summary>
    VideoToolbox,

    /// <summary>AMD AMF (Windows, Radeon): much faster, slightly larger files.</summary>
    Amf,

    /// <summary>Intel Quick Sync (Windows, Intel graphics): much faster, slightly larger files.</summary>
    Qsv,
}

public enum SoftSubContainer
{
    /// <summary>MP4 with <c>mov_text</c> tracks: plays everywhere, no styling.</summary>
    Mp4,

    /// <summary>MKV with ASS tracks and attached fonts: keeps the styling.</summary>
    Mkv,
}

/// <summary>An FFmpeg invocation: arguments plus the folder it must run in (filters use relative names).</summary>
public sealed record FfmpegCommand(IReadOnlyList<string> Arguments, string WorkingDirectory, string OutputPath);

/// <param name="WorkDirectory">
/// Folder holding <c>subtitles.ass</c> and a <c>fonts</c> subfolder. FFmpeg runs there so the
/// <c>ass</c> filter gets plain relative names: filter arguments would otherwise need fragile
/// escaping of Windows drive colons and backslashes.
/// </param>
/// <param name="Quality">x264 CRF scale (lower = better, 18–20 is visually lossless); mapped for hardware encoders.</param>
public sealed record BurnInRequest(
    string InputPath,
    string OutputPath,
    MediaInfo Source,
    string WorkDirectory,
    VideoEncoder Encoder = VideoEncoder.X264,
    int Quality = 19);

/// <param name="Path">Subtitle file: SRT for MP4, ASS for MKV.</param>
/// <param name="Language">ISO 639-2 code written into the container (e.g. <c>tur</c>, <c>eng</c>).</param>
public sealed record SoftSubTrack(string Path, string Language, string Title, bool IsDefault);

public sealed record SoftSubRequest(
    string InputPath,
    string OutputPath,
    MediaInfo Source,
    SoftSubContainer Container,
    IReadOnlyList<SoftSubTrack> Tracks,
    string WorkDirectory,
    IReadOnlyList<string>? FontFiles = null);

public enum OverlayCodec
{
    /// <summary>QuickTime Animation (RLE, lossless): tiny files, since frames without subtitles cost almost nothing.</summary>
    Animation,

    /// <summary>Apple ProRes 4444 with alpha: the most widely supported editing codec, much larger files.</summary>
    ProRes4444,
}

/// <summary>
/// A transparent video of the subtitles alone, as long as the source and at its exact frame rate, for an
/// editor's upper video track (Premiere Pro, DaVinci Resolve). The editor makes the only encode of the picture.
/// </summary>
/// <param name="WorkDirectory">Folder holding <c>subtitles.ass</c> and <c>fonts</c> (see <see cref="BurnInRequest"/>).</param>
/// <param name="FrameRate">Exact nominal rate as FFmpeg reads it, e.g. <c>30000/1001</c>.</param>
public sealed record OverlayRequest(
    string OutputPath,
    int Width,
    int Height,
    string FrameRate,
    TimeSpan Duration,
    string WorkDirectory,
    OverlayCodec Codec = OverlayCodec.Animation);

/// <summary>Builds the FFmpeg command lines for burn-in and soft-subtitle exports.</summary>
public static class FfmpegCommands
{
    public const string SubtitleFileName = "subtitles.ass";
    public const string FontsFolderName = "fonts";

    // Audio codecs an MP4 can carry as they are; anything else (PCM, Vorbis, …) is re-encoded to AAC.
    private static readonly HashSet<string> s_mp4Audio = new(StringComparer.OrdinalIgnoreCase) { "aac", "mp3", "ac3", "eac3", "alac" };

    private static readonly string[] s_common = ["-nostdin", "-hide_banner", "-v", "error", "-nostats", "-progress", "pipe:1", "-y"];

    public static FfmpegCommand BurnIn(BurnInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var args = new List<string>(s_common) { "-i", request.InputPath };
        args.AddRange(["-map", "0:v:0", "-map", "0:a:0?"]);

        // The same libass renderer as the preview; fonts from the project are copied into ./fonts.
        args.AddRange(["-vf", $"ass={SubtitleFileName}:fontsdir={FontsFolderName}"]);
        args.AddRange(VideoEncoderArguments(request.Encoder, request.Quality));
        args.AddRange(AudioArguments(request.Source, SoftSubContainer.Mp4));

        // Keep every frame's timestamp: phone videos often have a variable frame rate.
        args.AddRange(["-fps_mode", "passthrough", "-map_metadata", "0", "-movflags", "+faststart", "-f", "mp4", request.OutputPath]);
        return new FfmpegCommand(args, request.WorkDirectory, request.OutputPath);
    }

    public static FfmpegCommand Overlay(OverlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Width <= 0 || request.Height <= 0 || request.Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("The overlay needs the video's size and duration.", nameof(request));
        }

        // A fully transparent canvas; libass draws onto it with alpha=1, so the subtitles keep their own
        // transparency (without it the ass filter leaves the alpha channel at zero).
        var canvas = Inv($"color=c=black@0.0:s={request.Width}x{request.Height}:r={request.FrameRate}:d={request.Duration.TotalSeconds:0.###},format=rgba");
        var args = new List<string>(s_common) { "-f", "lavfi", "-i", canvas };
        args.AddRange(["-vf", $"ass={SubtitleFileName}:fontsdir={FontsFolderName}:alpha=1"]);
        args.AddRange(request.Codec switch
        {
            // A key frame every 5 s keeps seeking quick; between them only changed rows are stored.
            OverlayCodec.Animation => ["-c:v", "qtrle", "-pix_fmt", "argb", "-g", Inv($"{KeyFrameInterval(request.FrameRate)}")],
            _ => ["-c:v", "prores_ks", "-profile:v", "4444", "-pix_fmt", "yuva444p10le", "-vendor", "apl0"],
        });
        args.AddRange(["-an", "-f", "mov", request.OutputPath]);
        return new FfmpegCommand(args, request.WorkDirectory, request.OutputPath);
    }

    /// <summary>Frames in five seconds at an FFmpeg rate (<c>30000/1001</c> → 150).</summary>
    internal static int KeyFrameInterval(string frameRate)
    {
        var parts = frameRate.Split('/');
        var rate = parts.Length == 2 &&
                   double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
                   double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0
            ? n / d
            : double.TryParse(frameRate, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) ? plain : 25;
        return Math.Max(1, (int)Math.Round(rate * 5));
    }

    public static FfmpegCommand SoftSub(SoftSubRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Tracks.Count == 0)
        {
            throw new ArgumentException("At least one subtitle track is required.", nameof(request));
        }

        var args = new List<string>(s_common) { "-i", request.InputPath };
        foreach (var track in request.Tracks)
        {
            args.AddRange(["-i", track.Path]);
        }

        args.AddRange(["-map", "0:v:0", "-map", "0:a?"]);
        for (var i = 0; i < request.Tracks.Count; i++)
        {
            args.AddRange(["-map", Inv($"{i + 1}:0")]);
        }

        args.AddRange(["-c:v", "copy"]);
        args.AddRange(AudioArguments(request.Source, request.Container));
        args.AddRange(["-c:s", request.Container == SoftSubContainer.Mp4 ? "mov_text" : "copy"]);

        for (var i = 0; i < request.Tracks.Count; i++)
        {
            var track = request.Tracks[i];
            args.AddRange([Inv($"-metadata:s:s:{i}"), $"language={track.Language}"]);
            args.AddRange([Inv($"-metadata:s:s:{i}"), $"title={track.Title}"]);
            args.AddRange([Inv($"-disposition:s:{i}"), track.IsDefault ? "default" : "0"]);
        }

        if (request.Container == SoftSubContainer.Mkv)
        {
            // Fonts travel inside the file so players render the ASS tracks as designed.
            var fonts = request.FontFiles ?? [];
            for (var i = 0; i < fonts.Count; i++)
            {
                args.AddRange(["-attach", fonts[i], Inv($"-metadata:s:t:{i}"), $"mimetype={FontMimeType(fonts[i])}"]);
            }

            args.AddRange(["-map_metadata", "0", "-f", "matroska", request.OutputPath]);
        }
        else
        {
            args.AddRange(["-map_metadata", "0", "-movflags", "+faststart", "-f", "mp4", request.OutputPath]);
        }

        return new FfmpegCommand(args, request.WorkDirectory, request.OutputPath);
    }

    internal static IEnumerable<string> VideoEncoderArguments(VideoEncoder encoder, int quality)
    {
        var crf = Math.Clamp(quality, 10, 35);
        return encoder switch
        {
            // NVENC's constant-quality scale reads about two steps "worse" than x264 CRF.
            VideoEncoder.Nvenc =>
                ["-c:v", "h264_nvenc", "-preset", "p5", "-tune", "hq", "-rc", "vbr", "-cq", Inv($"{Math.Min(51, crf + 2)}"), "-b:v", "0", "-pix_fmt", "yuv420p"],

            // VideoToolbox takes a 1–100 quality (higher = better).
            VideoEncoder.VideoToolbox =>
                ["-c:v", "h264_videotoolbox", "-q:v", Inv($"{VideoToolboxQuality(crf)}"), "-pix_fmt", "yuv420p"],

            // AMF and Quick Sync constant-quality scales also read a step "worse" than x264 CRF.
            VideoEncoder.Amf =>
                ["-c:v", "h264_amf", "-quality", "quality", "-rc", "cqp", "-qp_i", Inv($"{Math.Min(51, crf + 1)}"), "-qp_p", Inv($"{Math.Min(51, crf + 1)}"), "-qp_b", Inv($"{Math.Min(51, crf + 1)}"), "-pix_fmt", "yuv420p"],

            // Quick Sync encodes NV12 frames; ICQ (global_quality) needs look-ahead off.
            VideoEncoder.Qsv =>
                ["-c:v", "h264_qsv", "-preset", "slower", "-global_quality", Inv($"{Math.Min(51, crf + 1)}"), "-look_ahead", "0", "-pix_fmt", "nv12"],

            _ => ["-c:v", "libx264", "-preset", "veryfast", "-crf", Inv($"{crf}"), "-pix_fmt", "yuv420p"],
        };
    }

    internal static int VideoToolboxQuality(int crf) => Math.Clamp((int)Math.Round(100 - ((crf - 10) * 3.5), MidpointRounding.AwayFromZero), 1, 100);

    /// <summary>Copies the audio when the container can hold it, otherwise AAC 192k.</summary>
    internal static IEnumerable<string> AudioArguments(MediaInfo source, SoftSubContainer container)
    {
        var codec = source.AudioStreams.Count > 0 ? source.AudioStreams[0].Codec : null;
        return codec is null || container == SoftSubContainer.Mkv || s_mp4Audio.Contains(codec)
            ? ["-c:a", "copy"]
            : ["-c:a", "aac", "-b:a", "192k"];
    }

    internal static string FontMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".otf" => "font/otf",
        ".ttc" or ".otc" => "font/collection",
        _ => "font/ttf",
    };

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
