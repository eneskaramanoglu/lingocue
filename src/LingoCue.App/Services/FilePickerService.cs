using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using LingoCue.Core;
using LingoCue.Core.Formats;
using LingoCue.Media;

namespace LingoCue.App.Services;

public interface IFilePickerService
{
    /// <summary>Shows the OS file picker for a video; returns the local path or null when cancelled.</summary>
    Task<string?> PickVideoAsync();

    Task<string?> PickProjectAsync();

    Task<string?> PickSubtitleAsync();

    Task<string?> SaveProjectAsync(string suggestedName);

    Task<string?> SaveSubtitleAsync(ISubtitleFormat format, string suggestedName);
}

internal sealed class FilePickerService : IFilePickerService
{
    private static readonly FilePickerFileType s_video = new("Video")
    {
        Patterns = [.. SupportedMedia.VideoExtensions.Select(e => "*" + e)],
        AppleUniformTypeIdentifiers = ["public.movie"],
        MimeTypes = ["video/*"],
    };

    private static readonly FilePickerFileType s_project = new("LingoCue projesi")
    {
        Patterns = ["*" + AppInfo.ProjectFileExtension],
        AppleUniformTypeIdentifiers = ["public.json", "public.data"],
    };

    private static readonly FilePickerFileType s_subtitles = new("Altyazı (SRT, VTT, ASS)")
    {
        Patterns = [.. SubtitleFormats.AllExtensions.Select(e => "*" + e)],
        AppleUniformTypeIdentifiers = ["public.text", "public.data"],
    };

    public Task<string?> PickVideoAsync() => OpenAsync("Video aç", s_video);

    public Task<string?> PickProjectAsync() => OpenAsync("Proje aç", s_project);

    public Task<string?> PickSubtitleAsync() => OpenAsync("Altyazı içe aktar", s_subtitles);

    public Task<string?> SaveProjectAsync(string suggestedName)
        => SaveAsync("Projeyi kaydet", suggestedName, AppInfo.ProjectFileExtension, s_project);

    public Task<string?> SaveSubtitleAsync(ISubtitleFormat format, string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(format);
        var extension = format.Extensions[0];
        var type = new FilePickerFileType(format.Name)
        {
            Patterns = ["*" + extension],
            AppleUniformTypeIdentifiers = ["public.text"],
        };
        return SaveAsync("Altyazıyı dışa aktar", suggestedName, extension, type);
    }

    private static async Task<string?> OpenAsync(string title, FilePickerFileType type)
    {
        if (GetWindow() is not { } window)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [type, FilePickerFileTypes.All],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static async Task<string?> SaveAsync(string title, string suggestedName, string extension, FilePickerFileType type)
    {
        if (GetWindow() is not { } window)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = Path.ChangeExtension(suggestedName, extension),
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [type],
            ShowOverwritePrompt = true,
        });

        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return null;
        }

        // Some platforms return the name without the chosen extension.
        return string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;
    }

    private static Window? GetWindow()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } ? window : null;
}
