namespace LingoCue.Media.Probing;

/// <summary>What ffprobe tells us about a media file.</summary>
public sealed record MediaInfo(
    string FilePath,
    string FormatName,
    TimeSpan Duration,
    long? SizeBytes,
    VideoStreamInfo? Video,
    IReadOnlyList<AudioStreamInfo> AudioStreams)
{
    public bool HasAudio => AudioStreams.Count > 0;
}

/// <param name="Width">Encoded width (before rotation).</param>
/// <param name="Height">Encoded height (before rotation).</param>
/// <param name="Rotation">Display rotation in degrees (0, 90, 180, 270). Phones record portrait video as rotated landscape.</param>
public sealed record VideoStreamInfo(
    int Index,
    string Codec,
    int Width,
    int Height,
    double FrameRate,
    int Rotation,
    string? PixelFormat = null,
    string? ColorTransfer = null,
    string? FrameRateText = null,
    string? ColorPrimaries = null,
    string? ColorSpace = null,
    string? ColorRange = null)
{
    /// <summary>The exact nominal rate for FFmpeg (<c>30000/1001</c>, <c>25/1</c>), so an overlay matches every frame.</summary>
    public string RateForFfmpeg => FrameRateText is { Length: > 0 } text && text != "0/0"
        ? text
        : FrameRate > 0 ? FrameRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "25";

    public int DisplayWidth => Rotation is 90 or 270 ? Height : Width;

    public int DisplayHeight => Rotation is 90 or 270 ? Width : Height;

    public bool IsPortrait => DisplayHeight > DisplayWidth;

    /// <summary>More than 8 bits per sample (e.g. yuv420p10le), typical of HDR phone video.</summary>
    public bool IsHighBitDepth => PixelFormat is { } f && (f.Contains("10", StringComparison.Ordinal) || f.Contains("12", StringComparison.Ordinal));

    /// <summary>HDR transfer (PQ or HLG): converting to 8-bit SDR H.264 without tone mapping looks washed out.</summary>
    public bool IsHdr => ColorTransfer is "smpte2084" or "arib-std-b67";

    /// <summary>Duration of one frame, used for frame-stepping. Falls back to 25 fps when unknown.</summary>
    public TimeSpan FrameDuration => TimeSpan.FromSeconds(1d / (FrameRate > 0 ? FrameRate : 25d));
}

public sealed record AudioStreamInfo(
    int Index,
    string Codec,
    int Channels,
    int SampleRate,
    string? Language);
