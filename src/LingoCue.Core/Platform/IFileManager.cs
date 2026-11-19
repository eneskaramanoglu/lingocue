namespace LingoCue.Core.Platform;

/// <summary>The OS file manager (Explorer, Finder).</summary>
public interface IFileManager
{
    /// <summary>Opens the folder of <paramref name="path"/> with the file selected.</summary>
    void Reveal(string path);
}
