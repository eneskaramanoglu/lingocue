using System.Text.Json;
using System.Text.Json.Serialization;
using LingoCue.Core.Formats;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Projects;

public sealed class ProjectFormatException : Exception
{
    public ProjectFormatException()
    {
    }

    public ProjectFormatException(string message)
        : base(message)
    {
    }

    public ProjectFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Saves/loads <c>.lingocue</c> files (indented UTF-8 JSON). Times are integer milliseconds, colors
/// <c>#RRGGBB[AA]</c>, enums strings. The video path is stored relative to the project file when it
/// shares the same root, so a project folder can be moved or synced between Windows and macOS.
/// </summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    public static string Serialize(Project project, string? projectPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var original = project.VideoPath;
        try
        {
            project.VideoPath = ToStoredPath(original, projectPath);
            return JsonSerializer.Serialize(project, s_options);
        }
        finally
        {
            project.VideoPath = original;
        }
    }

    public static Project Deserialize(string json, string? projectPath = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        Project? project;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var version) && version > Project.CurrentSchemaVersion)
            {
                throw new ProjectFormatException($"This project was saved by a newer LingoCue (schema {version}).");
            }

            project = doc.RootElement.Deserialize<Project>(s_options);
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException("The project file is damaged or not a LingoCue project.", ex);
        }

        if (project is null)
        {
            throw new ProjectFormatException("The project file is empty.");
        }

        project.VideoPath = FromStoredPath(project.VideoPath, projectPath);
        return project;
    }

    public static Task SaveAsync(Project project, string path, CancellationToken cancellationToken = default)
        => AtomicFile.WriteAllTextAsync(path, Serialize(project, path), cancellationToken);

    public static async Task<Project> LoadAsync(string path, CancellationToken cancellationToken = default)
        => Deserialize(await File.ReadAllTextAsync(path, cancellationToken), path);

    internal static string? ToStoredPath(string? videoPath, string? projectPath)
    {
        if (string.IsNullOrEmpty(videoPath) || string.IsNullOrEmpty(projectPath))
        {
            return videoPath;
        }

        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var full = Path.GetFullPath(videoPath);
        if (!string.Equals(Path.GetPathRoot(full), Path.GetPathRoot(projectDir), StringComparison.OrdinalIgnoreCase))
        {
            return full;
        }

        // Forward slashes so the file works on both Windows and macOS.
        return Path.GetRelativePath(projectDir, full).Replace('\\', '/');
    }

    internal static string? FromStoredPath(string? stored, string? projectPath)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(projectPath))
        {
            return stored;
        }

        var native = stored.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(native))
        {
            return native;
        }

        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        return Path.GetFullPath(Path.Combine(projectDir, native));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true, // computed values (Duration, CPS, ActiveTrack) are not stored
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep ğüşıöç readable
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new MillisecondsConverter());
        options.Converters.Add(new AssColorConverter());
        return options;
    }

    private sealed class MillisecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => TimeSpan.FromMilliseconds(reader.GetInt64());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            => writer.WriteNumberValue((long)Math.Round(value.TotalMilliseconds, MidpointRounding.AwayFromZero));
    }

    private sealed class AssColorConverter : JsonConverter<AssColor>
    {
        public override AssColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var hex = reader.GetString() ?? throw new JsonException("Color expected.");
            var bytes = Convert.FromHexString(hex.TrimStart('#'));
            return bytes.Length switch
            {
                3 => new AssColor(bytes[0], bytes[1], bytes[2]),
                4 => new AssColor(bytes[0], bytes[1], bytes[2], bytes[3]),
                _ => throw new JsonException($"Invalid color '{hex}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, AssColor value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToHex());
    }
}
