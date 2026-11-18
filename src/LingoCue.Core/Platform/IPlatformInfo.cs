namespace LingoCue.Core.Platform;

/// <summary>
/// The single source of truth for platform-specific facts. Code outside the Infrastructure
/// implementation must not call <c>OperatingSystem.IsWindows()</c>/<c>IsMacOS()</c> directly.
/// </summary>
public interface IPlatformInfo
{
    OsPlatform Platform { get; }

    /// <summary>Runtime identifier the app was built for, e.g. <c>win-x64</c> or <c>osx-arm64</c>.</summary>
    string RuntimeIdentifier { get; }

    /// <summary>Per-user application data root (settings, downloaded models, caches).</summary>
    string AppDataDirectory { get; }

    /// <summary>Directory for rolling log files.</summary>
    string LogDirectory { get; }

    /// <summary>
    /// Well-known directories where command-line tools (ffmpeg, ffprobe) are installed on this OS,
    /// in priority order. Needed because GUI apps started from Finder do not inherit the shell PATH.
    /// </summary>
    IReadOnlyList<string> ToolSearchDirectories { get; }

    /// <summary>Returns the platform file name of an executable, e.g. <c>ffmpeg</c> → <c>ffmpeg.exe</c> on Windows.</summary>
    string GetExecutableFileName(string name);
}
