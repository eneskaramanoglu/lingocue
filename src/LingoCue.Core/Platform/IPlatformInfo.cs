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
}
