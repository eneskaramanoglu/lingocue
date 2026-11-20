using System.Windows.Input;

namespace LingoCue.App.ViewModels;

/// <summary>One recent file: Dosya → Son açılanlar and the welcome screen's cards.</summary>
public sealed record RecentFileItem(string Path, ICommand Open)
{
    private const int MaxFolderLength = 32;

    public string Header => $"{FileName}  —  {ShortFolder}";

    public string FileName => System.IO.Path.GetFileName(Path);

    public bool IsProject => string.Equals(System.IO.Path.GetExtension(Path), Core.AppInfo.ProjectFileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>"Proje" for a .lingocue file, otherwise "Video".</summary>
    public string Kind => IsProject
        ? "Proje"
        : "Video";

    /// <summary>The folder, cut from the left when long (the end of the path tells folders apart).</summary>
    public string ShortFolder
    {
        get
        {
            var folder = System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
            return folder.Length <= MaxFolderLength ? folder : string.Concat("…", folder.AsSpan(folder.Length - (MaxFolderLength - 1)));
        }
    }
}

/// <summary>A line of the welcome screen's readiness list.</summary>
public sealed record ReadinessItem(string Label, string Detail, bool IsReady);
