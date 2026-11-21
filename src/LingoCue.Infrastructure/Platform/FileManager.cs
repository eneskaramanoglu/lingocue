using System.ComponentModel;
using System.Diagnostics;
using LingoCue.Core.Platform;

namespace LingoCue.Infrastructure.Platform;

public sealed class FileManager(IPlatformInfo platform) : IFileManager
{
    public void Reveal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var start = platform.Platform switch
        {
            OsPlatform.Windows => File.Exists(full)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"")
                : new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(full)}\""),
            OsPlatform.MacOS => File.Exists(full) ? Args("open", "-R", full) : Args("open", Path.GetDirectoryName(full)!),
            _ => Args("xdg-open", Path.GetDirectoryName(full)!),
        };
        start.UseShellExecute = false;

        try
        {
            using var process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            // No file manager available (headless); nothing to show.
        }
    }

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(full);
        Start(platform.Platform switch
        {
            OsPlatform.Windows => new ProcessStartInfo("explorer.exe", $"\"{full}\""),
            OsPlatform.MacOS => Args("open", full),
            _ => Args("xdg-open", full),
        });
    }

    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
        {
            return; // only web pages; never a file or a custom protocol handler
        }

        Start(platform.Platform switch
        {
            OsPlatform.Windows => new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true },
            OsPlatform.MacOS => Args("open", url.AbsoluteUri),
            _ => Args("xdg-open", url.AbsoluteUri),
        });
    }

    private static void Start(ProcessStartInfo start)
    {
        try
        {
            using var process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            // No browser or file manager available (headless); nothing to show.
        }
    }

    private static ProcessStartInfo Args(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file);
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        return info;
    }
}
