namespace LingoCue.App;

/// <summary>
/// Command line: <c>LingoCue [video] [--subtitle file.ass]</c>. The video argument also serves
/// "Open with" from the OS; <c>--subtitle</c> is a developer aid until subtitles can be loaded from the UI (M2).
/// </summary>
internal sealed record StartupArguments(string? VideoPath, string? SubtitlePath)
{
    public static StartupArguments Parse(IReadOnlyList<string>? args)
    {
        string? video = null;
        string? subtitle = null;

        for (var i = 0; args is not null && i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--subtitle", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                subtitle = args[++i];
            }
            else if (!arg.StartsWith('-') && video is null)
            {
                video = arg;
            }
        }

        return new StartupArguments(video, subtitle);
    }
}
