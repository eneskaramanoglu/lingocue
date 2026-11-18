using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using LingoCue.Media;

namespace LingoCue.App.Services;

public interface IFilePickerService
{
    /// <summary>Shows the OS file picker for a video; returns the local path or null when cancelled.</summary>
    Task<string?> PickVideoAsync();
}

internal sealed class FilePickerService : IFilePickerService
{
    public async Task<string?> PickVideoAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return null;
        }

        var videoType = new FilePickerFileType("Video")
        {
            Patterns = [.. SupportedMedia.VideoExtensions.Select(e => "*" + e)],
            AppleUniformTypeIdentifiers = ["public.movie"],
            MimeTypes = ["video/*"],
        };

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Video aç",
            AllowMultiple = false,
            FileTypeFilter = [videoType, FilePickerFileTypes.All],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
