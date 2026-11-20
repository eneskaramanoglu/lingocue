using LingoCue.Core.Formats;
using LingoCue.Core.Platform;
using LingoCue.Core.Projects;

namespace LingoCue.Infrastructure.Settings;

/// <summary>
/// User style templates as one JSON file each in <c>&lt;app data&gt;/templates</c>, so a template can
/// also be copied to another machine by hand. Unreadable files are skipped.
/// </summary>
public sealed class JsonStyleTemplateStore : IStyleTemplateStore
{
    private readonly string _directory;
    private readonly Lock _gate = new();
    private List<StyleTemplate> _templates;

    public JsonStyleTemplateStore(IPlatformInfo platform)
        : this(Path.Combine(platform.AppDataDirectory, "templates"))
    {
    }

    internal JsonStyleTemplateStore(string directory)
    {
        _directory = directory;
        _templates = Load(directory);
    }

    public IReadOnlyList<StyleTemplate> UserTemplates
    {
        get
        {
            lock (_gate)
            {
                return [.. _templates];
            }
        }
    }

    public void Save(StyleTemplate styleTemplate)
    {
        ArgumentNullException.ThrowIfNull(styleTemplate);
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);

            // Same name in another case: drop its file first (case-sensitive file systems would keep both).
            foreach (var old in _templates.Where(t => SameName(t.Name, styleTemplate.Name) && PathFor(t.Name) != PathFor(styleTemplate.Name)))
            {
                File.Delete(PathFor(old.Name));
            }

            AtomicFile.WriteAllText(PathFor(styleTemplate.Name), StyleTemplates.Serialize(styleTemplate)); // UI thread: never block on async
            _templates = [.. _templates.Where(t => !SameName(t.Name, styleTemplate.Name)), styleTemplate with { BuiltIn = false }];
            _templates.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    public void Delete(string name)
    {
        lock (_gate)
        {
            var path = PathFor(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            _templates = [.. _templates.Where(t => !SameName(t.Name, name))];
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, FileNameFor(name));

    /// <summary>A file name safe on Windows and macOS (invalid characters become <c>_</c>).</summary>
    internal static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var safe = new string([.. name.Trim().Select(c => invalid.Contains(c) ? '_' : c)]);
        return (safe.Length == 0 ? "template" : safe) + ".json";
    }

    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private static List<StyleTemplate> Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var list = new List<StyleTemplate>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                list.Add(StyleTemplates.Deserialize(File.ReadAllText(file)));
            }
            catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException)
            {
                // A broken template must not block the style panel; it is simply not listed.
            }
        }

        return [.. list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
