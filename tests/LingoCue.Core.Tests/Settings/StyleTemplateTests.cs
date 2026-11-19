using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Infrastructure.Settings;
using Shouldly;

namespace LingoCue.Core.Tests.Settings;

public sealed class StyleTemplateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lingocue-templates-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Built_in_templates_have_unique_names_and_build_valid_scripts()
    {
        StyleTemplates.BuiltIn.Select(t => t.Name).ShouldBeUnique();
        foreach (var template in StyleTemplates.BuiltIn)
        {
            template.BuiltIn.ShouldBeTrue();
            var project = new Project();
            project.EnsureActiveTrack().AddCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Merhaba");
            StyleTemplates.Apply(template, project);
            AssScriptBuilder.Build(project, project.ActiveTrack!).ShouldContain("Style: Default," + template.Style.FontName + ",");
        }
    }

    [Fact]
    public void Apply_keeps_the_projects_bilingual_track_choice()
    {
        var project = new Project();
        var secondary = Guid.NewGuid();
        project.Presentation = new PresentationOptions { Bilingual = new BilingualOptions { Enabled = true, SecondaryTrackId = secondary } };

        StyleTemplates.Apply(StyleTemplates.BuiltIn[2], project);

        project.Presentation.UpperCase.ShouldBeTrue();
        project.Presentation.Bilingual.Enabled.ShouldBeTrue();
        project.Presentation.Bilingual.SecondaryTrackId.ShouldBe(secondary);
    }

    [Fact]
    public void From_project_drops_track_references_and_round_trips_as_json()
    {
        var project = new Project
        {
            Style = new SubtitleStyle { FontName = "Georgia", PrimaryColor = new AssColor(10, 20, 30) },
            Presentation = new PresentationOptions { FadeInMs = 120, Bilingual = new BilingualOptions { Enabled = true, SecondaryTrackId = Guid.NewGuid() } },
        };

        var template = StyleTemplates.FromProject(project, "  Benim stilim ");
        var json = StyleTemplates.Serialize(template);
        var loaded = StyleTemplates.Deserialize(json);

        template.Name.ShouldBe("Benim stilim");
        template.Presentation.Bilingual.SecondaryTrackId.ShouldBeNull();
        json.ShouldContain("\"primaryColor\": \"#0A141E\"");
        loaded.ShouldBe(template);
    }

    [Fact]
    public void Deserialize_rejects_other_json()
    {
        Should.Throw<ProjectFormatException>(() => StyleTemplates.Deserialize("{\"foo\": 1}"));
        Should.Throw<ProjectFormatException>(() => StyleTemplates.Deserialize("not json"));
    }

    [Fact]
    public void Store_saves_replaces_lists_and_deletes()
    {
        var store = new JsonStyleTemplateStore(_dir);
        store.UserTemplates.ShouldBeEmpty();

        store.Save(new StyleTemplate { Name = "Şık: büyük/kalın", Style = new SubtitleStyle { FontSize = 70 } });
        store.Save(new StyleTemplate { Name = "Açık" });
        store.Save(new StyleTemplate { Name = "şık: büyük/kalın", Style = new SubtitleStyle { FontSize = 80 } }); // same name, other case

        var reloaded = new JsonStyleTemplateStore(_dir);
        reloaded.UserTemplates.Count.ShouldBe(2);
        reloaded.UserTemplates.Single(t => t.Name.StartsWith('ş')).Style.FontSize.ShouldBe(80);
        Directory.GetFiles(_dir).ShouldAllBe(f => !Path.GetFileName(f).Contains('/') && !Path.GetFileName(f).Contains(':'));

        reloaded.Delete("Açık");
        new JsonStyleTemplateStore(_dir).UserTemplates.Select(t => t.Name).ShouldBe(["şık: büyük/kalın"]);
    }

    [Fact]
    public void Store_skips_broken_files()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "bozuk.json"), "{");
        new JsonStyleTemplateStore(_dir).UserTemplates.ShouldBeEmpty();
    }
}
