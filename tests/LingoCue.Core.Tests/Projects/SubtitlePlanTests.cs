using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Tests.Projects;

public class SubtitlePlanTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    /// <summary>A mixed track (tr, en, ko lines) with an English and a Turkish translation of it.</summary>
    private static (Project Project, SubtitleTrack Source) MixedProject()
    {
        var project = new Project();
        var source = project.EnsureActiveTrack();
        source.Language = "tr";
        source.AddCue(S(0), S(2), "Merhaba arkadaşlar.").Language = "tr";
        source.AddCue(S(3), S(5), "Thank you so much.").Language = "en";
        source.AddCue(S(6), S(8), "안녕하세요 여러분.").Language = "ko";

        project.Tracks.Add(Translation(source, "en", ["Hello friends.", "Thank you so much.", "Hello everyone."]));
        project.Tracks.Add(Translation(source, "tr", ["Merhaba arkadaşlar.", "Çok teşekkürler.", "Herkese merhaba."]));
        return (project, source);
    }

    private static SubtitleTrack Translation(SubtitleTrack source, string language, string[] texts)
    {
        var track = new SubtitleTrack { Kind = TrackKind.Translation, Language = language, SourceTrackId = source.Id };
        for (var i = 0; i < source.Cues.Count; i++)
        {
            var copy = source.Cues[i].Clone();
            copy.SourceCueId = source.Cues[i].Id;
            copy.Text = texts[i];
            copy.Language = null;
            track.Cues.Add(copy);
        }

        return track;
    }

    private static SubtitlePlan OwnersPlan() => new SubtitlePlan { Enabled = true }
        .With(new LanguageRule { Language = "tr", Top = PlanLine.None })
        .With(new LanguageRule { Language = "en", Top = PlanLine.TranslationTo("tr") })
        .With(new LanguageRule { Language = "ko", Top = PlanLine.TranslationTo("tr"), Bottom = PlanLine.TranslationTo("en") });

    [Fact]
    public void Each_spoken_language_gets_its_own_subtitles()
    {
        // Turkish speech: nothing. English speech: Turkish. Korean speech: Turkish on top, English below.
        var (project, source) = MixedProject();
        project.Presentation = project.Presentation with { Plan = OwnersPlan() };

        var planned = SubtitlePlans.Resolve(project, source);

        planned.Main.Select(c => c.Text).ShouldBe(["Çok teşekkürler.", "Herkese merhaba."]);
        planned.Main.Select(c => c.Start).ShouldBe([S(3), S(6)]);
        planned.Second.Select(c => (c.Text, c.Language)).ShouldBe([("Hello everyone.", "en")]);
        planned.Missing.ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_translation_is_reported_and_its_lines_left_out()
    {
        var (project, source) = MixedProject();
        project.Tracks.RemoveAll(t => t.Kind == TrackKind.Translation && t.Language == "en");
        project.Presentation = project.Presentation with { Plan = OwnersPlan() };

        var planned = SubtitlePlans.Resolve(project, source);

        planned.Second.ShouldBeEmpty();
        planned.Missing.ShouldHaveSingleItem().ShouldBe(new MissingTranslation("ko", "en", [3]), new MissingComparer());
    }

    [Fact]
    public void A_translation_into_the_spoken_language_shows_the_original()
    {
        var (project, source) = MixedProject();
        project.Tracks.RemoveAll(t => t.Kind == TrackKind.Translation);
        project.Presentation = project.Presentation with
        {
            Plan = new SubtitlePlan { Enabled = true }.With(new LanguageRule { Language = "tr", Top = PlanLine.TranslationTo("tr") }),
        };

        var planned = SubtitlePlans.Resolve(project, source);

        planned.Main[0].Text.ShouldBe("Merhaba arkadaşlar.");
        planned.Missing.ShouldNotContain(m => m.SpokenLanguage == "tr");
    }

    [Fact]
    public void The_preview_script_shows_the_plan_with_the_second_line_in_the_second_style()
    {
        var (project, source) = MixedProject();
        project.Presentation = project.Presentation with { Plan = OwnersPlan() };

        var ass = AssScriptBuilder.Build(project, source);

        ass.ShouldNotContain("Merhaba arkadaşlar.");
        ass.ShouldContain(",Default,,0,0,");
        ass.ShouldContain("Herkese merhaba.");
        ass.ShouldContain($",{AssScriptBuilder.SecondaryStyleName},");
        ass.ShouldContain("Hello everyone.");
    }

    [Fact]
    public void A_one_track_output_writes_both_lines_in_one_subtitle()
    {
        var (project, source) = MixedProject();
        project.Presentation = project.Presentation with { Plan = OwnersPlan() };

        var flat = SubtitlePlans.Flatten(project, source);

        flat.Cues.Select(c => c.Text).ShouldBe(["Çok teşekkürler.", "Herkese merhaba.\nHello everyone."]);
    }

    [Fact]
    public void Translation_tracks_and_disabled_plans_are_shown_as_they_are()
    {
        var (project, source) = MixedProject();
        project.Presentation = project.Presentation with { Plan = OwnersPlan() with { Enabled = false } };

        SubtitlePlans.AppliesTo(project, source).ShouldBeFalse();
        project.Presentation = project.Presentation with { Plan = OwnersPlan() };
        SubtitlePlans.AppliesTo(project, project.Tracks[1]).ShouldBeFalse();
        AssScriptBuilder.Build(project, project.Tracks[1]).ShouldContain("Hello friends.");
    }

    [Fact]
    public void Plans_compare_by_their_rules_and_survive_a_project_round_trip()
    {
        var (project, _) = MixedProject();
        project.Presentation = project.Presentation with { Plan = OwnersPlan() };

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        loaded.Presentation.Plan.ShouldBe(OwnersPlan());
        loaded.Presentation.Plan.RuleFor("ko").Bottom.ShouldBe(PlanLine.TranslationTo("en"));
        OwnersPlan().RuleFor("de").Top.ShouldBe(PlanLine.Original);
    }

    private sealed class MissingComparer : IEqualityComparer<MissingTranslation>
    {
        public bool Equals(MissingTranslation? x, MissingTranslation? y)
            => x is not null && y is not null && x.SpokenLanguage == y.SpokenLanguage && x.TargetLanguage == y.TargetLanguage && x.SourceCueIds.SequenceEqual(y.SourceCueIds);

        public int GetHashCode(MissingTranslation obj) => obj.SpokenLanguage.GetHashCode(StringComparison.Ordinal);
    }
}
