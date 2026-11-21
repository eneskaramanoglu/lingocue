using System.Text.Json;
using System.Text.Json.Serialization;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Projects;

/// <summary>A named look: the main style plus presentation options (no track references).</summary>
public sealed record StyleTemplate
{
    public required string Name { get; init; }

    public SubtitleStyle Style { get; init; } = new();

    public PresentationOptions Presentation { get; init; } = new();

    /// <summary>Shipped with the app (cannot be overwritten or deleted).</summary>
    [JsonIgnore]
    public bool BuiltIn { get; init; }
}

/// <summary>User templates kept in the app-data folder (shared by all projects).</summary>
public interface IStyleTemplateStore
{
    IReadOnlyList<StyleTemplate> UserTemplates { get; }

    /// <summary>Adds or replaces the template with the same name.</summary>
    void Save(StyleTemplate styleTemplate);

    void Delete(string name);
}

public static class StyleTemplates
{
    // Fonts available on both Windows and macOS out of the box.
    public static IReadOnlyList<StyleTemplate> BuiltIn { get; } =
    [
        new()
        {
            Name = "YouTube klasik",
            BuiltIn = true,
            Style = new SubtitleStyle { FontName = "Arial", FontSize = 52, Outline = 3, Shadow = 1, MarginV = 50 },
        },
        new()
        {
            Name = "Netflix tarzı",
            BuiltIn = true,
            Style = new SubtitleStyle
            {
                FontName = "Arial",
                FontSize = 50,
                Outline = 2,
                Shadow = 2,
                OutlineColor = new AssColor(0, 0, 0, 230),
                BackColor = new AssColor(0, 0, 0, 160),
                MarginV = 70,
            },
            Presentation = new PresentationOptions { FadeInMs = 80, FadeOutMs = 80 },
        },
        new()
        {
            Name = "Shorts / Reels (9:16)",
            BuiltIn = true,
            Style = new SubtitleStyle
            {
                FontName = "Arial Black",
                FontSize = 80,
                Bold = true,
                Outline = 6,
                Shadow = 0,
                Alignment = 5,
                MarginL = 80,
                MarginR = 80,
            },
            Presentation = new PresentationOptions
            {
                UpperCase = true,
                Karaoke = new KaraokeOptions { Mode = KaraokeMode.ActiveWord, HighlightColor = new AssColor(255, 220, 0) },
            },
        },
        new()
        {
            Name = "Belgesel",
            BuiltIn = true,
            Style = new SubtitleStyle
            {
                FontName = "Georgia",
                FontSize = 46,
                Outline = 0,
                Shadow = 0,
                MarginV = 60,
                Background = new SubtitleBackground { Kind = BackgroundKind.BlockBox, Color = new AssColor(0, 0, 0, 170), PaddingX = 22, PaddingY = 10 },
            },
            Presentation = new PresentationOptions { FadeInMs = 200, FadeOutMs = 200 },
        },
        new()
        {
            Name = "Haber şeridi",
            BuiltIn = true,
            Style = new SubtitleStyle
            {
                FontName = "Arial",
                FontSize = 48,
                Bold = true,
                Outline = 0,
                Shadow = 0,
                MarginV = 70,
                Background = new SubtitleBackground
                {
                    Kind = BackgroundKind.Band, Color = new AssColor(12, 28, 64, 225), PaddingY = 14,
                    FrameWidth = 3, FrameColor = new AssColor(255, 196, 0),
                },
            },
        },
    ];

    /// <summary>Captures the project's current look; track choices of bilingual mode are left out.</summary>
    public static StyleTemplate FromProject(Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new StyleTemplate
        {
            Name = name.Trim(),
            Style = project.Style with { Name = "Default" },
            Presentation = project.Presentation with
            {
                Bilingual = project.Presentation.Bilingual with { Enabled = false, SecondaryTrackId = null },
            },
        };
    }

    /// <summary>Applies a template; the project's bilingual on/off and track choice are kept.</summary>
    public static void Apply(StyleTemplate template, Project project)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(project);
        var bilingual = project.Presentation.Bilingual;
        project.Style = template.Style with { Name = "Default" };
        project.Presentation = template.Presentation with
        {
            Bilingual = template.Presentation.Bilingual with { Enabled = bilingual.Enabled, SecondaryTrackId = bilingual.SecondaryTrackId },
        };
    }

    /// <summary>
    /// The project looks like the template: same style and presentation (bilingual track choices aside).
    /// Colors may differ by a couple of steps per channel: color pickers round them through HSV.
    /// </summary>
    public static bool LooksLike(Project project, StyleTemplate template)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(template);
        return Quantize(project.Style with { Name = "Default" }) == Quantize(template.Style with { Name = "Default" }) &&
               Quantize(project.Presentation with { Bilingual = template.Presentation.Bilingual }) == Quantize(template.Presentation);
    }

    private static SubtitleStyle Quantize(SubtitleStyle s) => s with
    {
        PrimaryColor = Q(s.PrimaryColor),
        SecondaryColor = Q(s.SecondaryColor),
        OutlineColor = Q(s.OutlineColor),
        BackColor = Q(s.BackColor),
        Background = s.Background with { Color = Q(s.Background.Color), FrameColor = Q(s.Background.FrameColor) },
    };

    private static PresentationOptions Quantize(PresentationOptions p) => p with
    {
        Karaoke = p.Karaoke with { HighlightColor = Q(p.Karaoke.HighlightColor) },
    };

    private static AssColor Q(AssColor c) => new((byte)((c.R + 2) / 5), (byte)((c.G + 2) / 5), (byte)((c.B + 2) / 5), (byte)((c.A + 2) / 5));

    public static string Serialize(StyleTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return JsonSerializer.Serialize(template, ProjectSerializer.Options);
    }

    /// <exception cref="ProjectFormatException">The text is not a style template.</exception>
    public static StyleTemplate Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var template = JsonSerializer.Deserialize<StyleTemplate>(json, ProjectSerializer.Options);
            return template is { Name.Length: > 0 }
                ? template with { Presentation = template.Presentation with { Bilingual = template.Presentation.Bilingual with { SecondaryTrackId = null } } }
                : throw new ProjectFormatException("The file is not a LingoCue style template.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException("The file is not a LingoCue style template.", ex);
        }
    }
}
