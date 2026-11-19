using LingoCue.Core.Platform;

namespace LingoCue.Core.Fonts;

public enum FontSource
{
    Missing,
    System,
    Project,
}

public sealed record FontStatus(FontSource Source, bool HasTurkishLetters);

public interface IFontCatalog
{
    /// <summary>Family names usable in a style: installed fonts plus the project's own font files.</summary>
    IReadOnlyList<string> Families(string? projectFontDirectory);

    FontStatus Check(string family, string? projectFontDirectory);

    /// <summary>Family names in a single font file (empty when it is not a readable font).</summary>
    IReadOnlyList<string> FamiliesIn(string fontFile);
}

/// <summary>
/// Font names exactly as libass sees them (legacy family name of each face), read from the font
/// files in the platform's font folders and in the project's <c>fonts</c> folder.
/// </summary>
public sealed class FontCatalog(IPlatformInfo platform) : IFontCatalog
{
    /// <summary>Folder next to the project file whose fonts are used by preview and burn-in.</summary>
    public const string ProjectFontFolderName = "fonts";

    private readonly Lazy<IReadOnlyList<FontFace>> _system = new(() => Scan(platform.FontDirectories));
    private readonly Lock _gate = new();
    private (string Directory, DateTime Stamp, IReadOnlyList<FontFace> Faces)? _project;

    /// <summary>Fonts that ship with both Windows and macOS, so a style using them renders the same on both.</summary>
    public static IReadOnlySet<string> CrossPlatformFamilies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Arial", "Arial Black", "Comic Sans MS", "Courier New", "Georgia", "Impact", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
    };

    public static string? ProjectFontDirectory(string? projectPath)
        => string.IsNullOrEmpty(projectPath) ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, ProjectFontFolderName);

    public IReadOnlyList<string> Families(string? projectFontDirectory)
        => [.. _system.Value.Concat(ProjectFaces(projectFontDirectory))
            .Select(f => f.Family)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)];

    public FontStatus Check(string family, string? projectFontDirectory)
    {
        ArgumentNullException.ThrowIfNull(family);
        var name = family.Trim();
        var project = ProjectFaces(projectFontDirectory).Where(f => Matches(f, name)).ToList();
        if (project.Count > 0)
        {
            return new FontStatus(FontSource.Project, project.Any(f => f.HasTurkishLetters));
        }

        var system = _system.Value.Where(f => Matches(f, name)).ToList();
        return system.Count > 0
            ? new FontStatus(FontSource.System, system.Any(f => f.HasTurkishLetters))
            : new FontStatus(FontSource.Missing, false);
    }

    public IReadOnlyList<string> FamiliesIn(string fontFile)
        => [.. FontFileReader.ReadFaces(fontFile).Select(f => f.Family).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static bool Matches(FontFace face, string name)
        => string.Equals(face.Family, name, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<FontFace> ProjectFaces(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        // Rescan when files were added or removed (the folder's write time changes).
        var stamp = Directory.GetLastWriteTimeUtc(directory);
        lock (_gate)
        {
            if (_project is { } cached && cached.Directory == directory && cached.Stamp == stamp)
            {
                return cached.Faces;
            }

            var faces = Scan([directory]);
            _project = (directory, stamp, faces);
            return faces;
        }
    }

    private static IReadOnlyList<FontFace> Scan(IEnumerable<string> directories)
        => [.. directories
            .Where(Directory.Exists)
            .SelectMany(d => SafeEnumerate(d))
            .Where(FontFileReader.IsFontFile)
            .SelectMany(FontFileReader.ReadFaces)];

    private static List<string> SafeEnumerate(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
