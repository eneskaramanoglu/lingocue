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

public class LanguageCodesTests
{
    [Theory]
    [InlineData("tr", "tur")]
    [InlineData("en-US", "eng")]
    [InlineData("de", "ger")]
    [InlineData("pl", "pol")]
    [InlineData("", "und")]
    [InlineData(null, "und")]
    [InlineData("zz", "und")]
    public void Maps_to_iso_639_2(string? input, string expected)
        => LingoCue.Core.Text.LanguageCodes.ToIso6392(input).ShouldBe(expected);
}

public class ReadingRulesLanguageTests
{
    [Fact]
    public void English_reads_faster_than_turkish()
    {
        var settings = new LingoCue.Core.Settings.ReadingRulesSettings { TargetCps = 17, MaxCps = 21 };

        settings.ToRules("tr").TargetCps.ShouldBe(17);
        settings.ToRules().MaxCps.ShouldBe(21);
        settings.ToRules("en").TargetCps.ShouldBe(20);
        settings.ToRules("en-GB").MaxCps.ShouldBe(24);
        settings.ToRules("en").MaxCharsPerLine.ShouldBe(42);
    }
}

public class RecentFilesTests
{
    [Fact]
    public void Newest_first_without_duplicates_and_capped()
    {
        var settings = new LingoCue.Core.Settings.AppSettings();
        var dir = Path.GetTempPath();
        for (var i = 0; i < 12; i++)
        {
            settings = settings.WithRecentFile(Path.Combine(dir, $"p{i}.lingocue"));
        }

        settings = settings.WithRecentFile(Path.Combine(dir, "P5.lingocue"));

        settings.RecentFiles.Count.ShouldBe(LingoCue.Core.Settings.AppSettings.MaxRecentFiles);
        settings.RecentFiles[0].ShouldEndWith("P5.lingocue");
        settings.RecentFiles.Count(p => p.EndsWith("p5.lingocue", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        settings.WithoutRecentFile(settings.RecentFiles[0]).RecentFiles.Count.ShouldBe(9);
    }
}
