using System.Windows.Input;

namespace LingoCue.App.ViewModels;

/// <summary>One entry of Dosya → Son açılanlar: the file name, its folder as a hint, and the open command.</summary>
public sealed record RecentFileItem(string Path, ICommand Open)
{
    public string Header => $"{System.IO.Path.GetFileName(Path)}  —  {System.IO.Path.GetDirectoryName(Path)}";
}
