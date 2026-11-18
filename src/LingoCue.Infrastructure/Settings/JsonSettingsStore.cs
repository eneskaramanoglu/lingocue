using System.Text.Json;
using LingoCue.Core.Formats;
using LingoCue.Core.Platform;
using LingoCue.Core.Settings;

namespace LingoCue.Infrastructure.Settings;

/// <summary>
/// <c>settings.json</c> in the app-data folder. A missing file means defaults; an unreadable one is
/// kept as <c>settings.json.bad</c> and defaults are used, so a broken file never blocks startup.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    public JsonSettingsStore(IPlatformInfo platform)
        : this(Path.Combine(platform.AppDataDirectory, "settings.json"))
    {
    }

    internal JsonSettingsStore(string path)
    {
        _path = path;
        Current = Load(path);
    }

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current { get; private set; }

    public string FilePath => _path;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            AtomicFile.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, s_options)).GetAwaiter().GetResult();
            Current = settings;
        }

        Changed?.Invoke(this, settings);
    }

    private static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), s_options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".bad", overwrite: true);
            return new AppSettings();
        }
    }
}
