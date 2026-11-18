using System.Runtime.InteropServices;
using LingoCue.Core;
using LingoCue.Core.Platform;

namespace LingoCue.Infrastructure.Platform;

/// <summary>
/// The only place in the code base that branches on the operating system.
/// Windows: %APPDATA%\LingoCue. macOS: ~/Library/Application Support/LingoCue (logs in ~/Library/Logs).
/// </summary>
public sealed class PlatformInfo : IPlatformInfo
{
    public PlatformInfo()
        : this(DetectPlatform(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    internal PlatformInfo(OsPlatform platform, string userHome)
    {
        Platform = platform;
        RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        (AppDataDirectory, LogDirectory) = ResolveDirectories(platform, userHome);
    }

    public OsPlatform Platform { get; }

    public string RuntimeIdentifier { get; }

    public string AppDataDirectory { get; }

    public string LogDirectory { get; }

    private static OsPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return OsPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return OsPlatform.MacOS;
        }

        return OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.Unknown;
    }

    private static (string AppData, string Logs) ResolveDirectories(OsPlatform platform, string userHome)
    {
        switch (platform)
        {
            case OsPlatform.Windows:
            {
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(roaming))
                {
                    roaming = Path.Combine(userHome, "AppData", "Roaming");
                }

                var appData = Path.Combine(roaming, AppInfo.Name);
                return (appData, Path.Combine(appData, "logs"));
            }

            case OsPlatform.MacOS:
            {
                var library = Path.Combine(userHome, "Library");
                return (Path.Combine(library, "Application Support", AppInfo.Name), Path.Combine(library, "Logs", AppInfo.Name));
            }

            default:
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                var dataRoot = string.IsNullOrEmpty(xdg) ? Path.Combine(userHome, ".local", "share") : xdg;
                var appData = Path.Combine(dataRoot, AppInfo.Name.ToLowerInvariant());
                return (appData, Path.Combine(appData, "logs"));
            }
        }
    }
}
