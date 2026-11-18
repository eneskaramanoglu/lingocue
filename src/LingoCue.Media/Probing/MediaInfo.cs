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
    int Rotation)
{
    public int DisplayWidth => Rotation is 90 or 270 ? Height : Width;

    public int DisplayHeight => Rotation is 90 or 270 ? Width : Height;

    public bool IsPortrait => DisplayHeight > DisplayWidth;

    /// <summary>Duration of one frame, used for frame-stepping. Falls back to 25 fps when unknown.</summary>
    public TimeSpan FrameDuration => TimeSpan.FromSeconds(1d / (FrameRate > 0 ? FrameRate : 25d));
}

public sealed record AudioStreamInfo(
    int Index,
    string Codec,
    int Channels,
    int SampleRate,
    string? Language);
