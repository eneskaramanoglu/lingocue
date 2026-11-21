namespace LingoCue.Core.Platform;

/// <summary>Hands files, folders and links to the operating system's own apps.</summary>
public interface IFileManager
{
    /// <summary>Opens the folder of <paramref name="path"/> with the file selected.</summary>
    void Reveal(string path);

    /// <summary>Opens a folder in Explorer/Finder.</summary>
    void OpenFolder(string path);

    /// <summary>Opens an https link in the default browser; other schemes are ignored.</summary>
    void OpenUrl(Uri url);
}
