using LingoCue.Core.Platform;

namespace LingoCue.Media.Tests;

internal sealed class FakePlatform(OsPlatform platform, params string[] toolDirectories) : IPlatformInfo
{
    public OsPlatform Platform { get; } = platform;

    public string RuntimeIdentifier => "test";

    public string AppDataDirectory => Path.GetTempPath();

    public string LogDirectory => Path.GetTempPath();

    public IReadOnlyList<string> ToolSearchDirectories { get; } = toolDirectories;

    public string NativeLibraryDirectory => AppContext.BaseDirectory;

    public IReadOnlyList<string> FontDirectories { get; } = [];

    public string GetExecutableFileName(string name) => Platform == OsPlatform.Windows ? name + ".exe" : name;
}
