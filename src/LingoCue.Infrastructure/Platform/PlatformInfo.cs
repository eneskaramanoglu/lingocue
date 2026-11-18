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
        ToolSearchDirectories = ResolveToolDirectories(platform, userHome, AppDataDirectory);
    }

    public OsPlatform Platform { get; }

    public string RuntimeIdentifier { get; }

    public string AppDataDirectory { get; }

    public string LogDirectory { get; }

    public IReadOnlyList<string> ToolSearchDirectories { get; }

    public string GetExecutableFileName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Platform == OsPlatform.Windows && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name + ".exe"
            : name;
    }

    private static string[] ResolveToolDirectories(OsPlatform platform, string userHome, string appData)
    {
        // Tools shipped next to the app or downloaded by the app come first.
        var dirs = new List<string>
        {
            AppContext.BaseDirectory,
            Path.Combine(appData, "tools", "ffmpeg"),
        };

        switch (platform)
        {
            case OsPlatform.Windows:
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData))
                {
                    localAppData = Path.Combine(userHome, "AppData", "Local");
                }

                dirs.Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links")); // winget
                dirs.Add(Path.Combine(userHome, "scoop", "shims"));                   // scoop
                dirs.Add(@"C:\ProgramData\chocolatey\bin");                           // chocolatey
                dirs.Add(@"C:\ffmpeg\bin");
                break;

            case OsPlatform.MacOS:
                // .app bundle: Contents/MacOS is the base directory; Contents/Resources may hold tools.
                dirs.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources")));
                dirs.Add("/opt/homebrew/bin"); // Homebrew on Apple Silicon
                dirs.Add("/usr/local/bin");
                dirs.Add("/opt/local/bin");    // MacPorts
                break;

            default:
                dirs.Add("/usr/bin");
                dirs.Add("/usr/local/bin");
                break;
        }

        return [.. dirs];
    }

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
