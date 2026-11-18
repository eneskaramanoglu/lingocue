namespace LingoCue.Media;

public static class SupportedMedia
{
    /// <summary>Video containers the app opens, lowercase with leading dot.</summary>
    public static IReadOnlyList<string> VideoExtensions { get; } = [".mp4", ".mov", ".mkv", ".m4v"];

    /// <summary>Case-insensitive on purpose: macOS/Windows users name files ".MP4" too.</summary>
    public static bool IsSupportedVideo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        return VideoExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
    }
}
