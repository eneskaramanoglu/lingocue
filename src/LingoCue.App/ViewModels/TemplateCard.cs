using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.Core.Projects;

namespace LingoCue.App.ViewModels;

/// <summary>A template in the style gallery: its name and a thumbnail of how it looks on the video.</summary>
public sealed partial class TemplateCard(StyleTemplate template) : ObservableObject
{
    public StyleTemplate Template { get; } = template;

    public string Name => Template.Name;

    public bool BuiltIn => Template.BuiltIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial Bitmap? Thumbnail { get; private set; }

    public bool HasThumbnail => Thumbnail is not null;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    private string? _thumbnailPath;

    /// <summary>Loads the rendered PNG (on the UI thread); the same file is not loaded twice.</summary>
    public void SetThumbnail(string path)
    {
        if (path == _thumbnailPath)
        {
            return;
        }

        try
        {
            var bitmap = new Bitmap(path);
            var previous = Thumbnail;
            Thumbnail = bitmap;
            _thumbnailPath = path;
            previous?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A broken cache file only means no picture for this card.
        }
    }
}
