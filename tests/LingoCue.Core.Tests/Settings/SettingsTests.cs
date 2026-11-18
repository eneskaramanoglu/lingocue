using System.Globalization;
using LingoCue.Core.Projects;
using LingoCue.Core.Settings;
using LingoCue.Infrastructure.Settings;

namespace LingoCue.Core.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-settings-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File_ => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var store = new JsonSettingsStore(File_);

        store.Current.Transcription.DefaultEngine.ShouldBe("whisper");
        store.Current.Llm.Parallelism.ShouldBe(2);
        store.Current.Reading.ToRules().MaxCharsPerLine.ShouldBe(42);
    }

    [Fact]
    public void Saves_reloads_and_raises_changed()
    {
        var store = new JsonSettingsStore(File_);
        AppSettings? raised = null;
        store.Changed += (_, s) => raised = s;

        store.Save(store.Current with { Llm = store.Current.Llm with { FastModel = "gemini-3.8-flash", Reflect = true } });

        raised.ShouldNotBeNull();
        var reloaded = new JsonSettingsStore(File_).Current;
        reloaded.Llm.FastModel.ShouldBe("gemini-3.8-flash");
        reloaded.Llm.Reflect.ShouldBeTrue();
        File.ReadAllText(File_).ShouldContain("\"fastModel\": \"gemini-3.8-flash\"");
    }

    [Fact]
    public void Partial_and_corrupt_files_fall_back_safely()
    {
        File.WriteAllText(File_, """{ "llm": { "batchSize": 25 } }""");
        var partial = new JsonSettingsStore(File_).Current;
        partial.Llm.BatchSize.ShouldBe(25);
        partial.Llm.Parallelism.ShouldBe(2);
        partial.Transcription.Language.ShouldBe("tr");

        File.WriteAllText(File_, "{ not json");
        new JsonSettingsStore(File_).Current.ShouldBe(new AppSettings() with { Transcription = new(), Llm = new(), Reading = new() });
        File.Exists(File_ + ".bad").ShouldBeTrue();
    }

    [Fact]
    public void Reading_rules_are_clamped_to_sane_values()
    {
        var rules = new ReadingRulesSettings { MaxCharsPerLine = 5, TargetCps = 30, MaxCps = 10 }.ToRules();

        rules.MaxCharsPerLine.ShouldBe(10);
        rules.MaxCps.ShouldBe(30); // never below the target
    }
}

public class GlossaryTests
{
    [Fact]
    public void Merges_suggestions_without_touching_user_entries()
    {
        List<GlossaryEntry> existing = [new() { Term = "İstanbul", Locked = true }];
        GlossaryEntry[] suggestions =
        [
            new() { Term = "istanbul", Preferred = "Istanbul" }, // same term, Turkish case-insensitive
            new() { Term = "Boğaz", Kind = GlossaryKind.ProperNoun },
            new() { Term = "İstanbul", TargetLanguage = "en", Preferred = "Istanbul" }, // translation entry is separate
        ];

        var merged = existing.MergeSuggestions(suggestions, CultureInfo.GetCultureInfo("tr-TR"));

        merged.Count.ShouldBe(3);
        merged[0].Locked.ShouldBeTrue();
        merged.Skip(1).ShouldAllBe(e => e.Suggested && !e.Locked);
        merged.For(null).Select(e => e.Term).ShouldBe(["İstanbul", "Boğaz"]);
        merged.For("en").ShouldHaveSingleItem().Effective.ShouldBe("Istanbul");
    }
}
