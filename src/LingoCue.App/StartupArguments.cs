namespace LingoCue.App;

/// <summary>
/// Command line: <c>LingoCue [file]</c> where file is a video, a subtitle or a <c>.lingocue</c> project.
/// Also how the OS passes "Open with" files.
/// </summary>
internal sealed record StartupArguments(string? FilePath)
{
    public static StartupArguments Parse(IReadOnlyList<string>? args)
        => new(args?.FirstOrDefault(a => !a.StartsWith('-')));
}
