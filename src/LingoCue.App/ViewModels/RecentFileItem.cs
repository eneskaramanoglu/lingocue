using System.Windows.Input;

namespace LingoCue.App.ViewModels;

/// <summary>One entry of Dosya → Son açılanlar: the file name, its folder as a hint, and the open command.</summary>
public sealed record RecentFileItem(string Path, ICommand Open)
{
    private const int MaxFolderLength = 32;

    public string Header => $"{System.IO.Path.GetFileName(Path)}  —  {ShortFolder}";

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
