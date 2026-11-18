namespace LingoCue.Core;

/// <summary>Application-wide constants that are independent of the UI and the platform.</summary>
public static class AppInfo
{
    public const string Name = "LingoCue";

    /// <summary>Extension of LingoCue project files (JSON), including the leading dot.</summary>
    public const string ProjectFileExtension = ".lingocue";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
