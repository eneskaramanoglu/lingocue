using LingoCue.Core.Fonts;
using LingoCue.Core.Projects;
using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;
using LingoCue.Llm.Pipeline;

namespace LingoCue.Llm.Tests;

public class PassPlanningTests
{
    [Fact]
    public void Cloud_runs_use_the_normal_batch_settings()
    {
        var options = PassPlanning.Options(new AppSettings { Llm = new LlmSettings { BatchSize = 30, Parallelism = 3 } }, local: false, reflect: true, "en");

        (options.BatchSize, options.Parallelism, options.Pacer, options.Reflect).ShouldBe((30, 3, null, true));
        options.RequestTimeout.ShouldBe(PassPlanning.CloudRequestTimeout);
        options.Rules.MaxCps.ShouldBe(new ReadingRulesSettings().MaxCps + ReadingRulesSettings.EnglishCpsBonus); // output language rules
    }

    [Fact]
    public void Free_tier_runs_are_small_single_and_paced()
    {
        var options = PassPlanning.Options(new AppSettings { Llm = new LlmSettings { BatchSize = 30, Parallelism = 3, FreeTier = true, RequestsPerMinute = 10 } },
            local: false, reflect: false, "tr");

        (options.BatchSize, options.Parallelism).ShouldBe((LlmSettings.FreeTierBatchSize, 1));
        options.Pacer.ShouldNotBeNull().Interval.ShouldBe(TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void Local_runs_use_their_own_settings_and_a_long_timeout()
    {
        var options = PassPlanning.Options(new AppSettings { Llm = new LlmSettings { FreeTier = true, LocalBatchSize = 20, LocalParallelism = 9 } },
            local: true, reflect: false, "ko");

        (options.BatchSize, options.Parallelism, options.Pacer).ShouldBe((20, 4, null)); // parallelism capped, no pacing
        options.RequestTimeout.ShouldBe(PassPlanning.LocalRequestTimeout);
        options.Rules.MaxCharsPerLine.ShouldBe(16); // Korean
    }

    private static TrackPassResult Result(SubtitleTrack source, string language, params LineStatus[] statuses)
    {
        var track = new SubtitleTrack { Name = "Çeviri · " + language, Language = language, Kind = TrackKind.Translation, SourceTrackId = source.Id };
        var lines = statuses.Select((s, i) => new LineResult(i + 1, "x", s)).ToList();
        return new TrackPassResult(track, new PipelineResult(lines, LlmUsage.Zero, 0), LlmUsage.Zero, 0);
    }

    [Fact]
    public void A_translation_joins_the_project_as_the_bilingual_partner_and_repeats_get_numbers()
    {
        var project = new Project();
        var source = project.EnsureActiveTrack();

        var counts = PassPlanning.AddTrack(project, Result(source, "en", LineStatus.Changed, LineStatus.NeedsReview, LineStatus.Failed), TrackPassKind.Translation);
        var again = Result(source, "en");
        PassPlanning.AddTrack(project, again, TrackPassKind.Translation);

        counts.ShouldBe(new PassCounts(3, 1, 1, 1));
        project.Presentation.Bilingual.SecondaryTrackId.ShouldBe(project.Tracks[1].Id); // the first translation stays the partner
        again.Track.Name.ShouldBe("Çeviri · en 2");
        project.ActiveTrackId.ShouldBe(again.Track.Id);
    }

    [Fact]
    public void A_korean_partner_gets_a_hangul_font()
    {
        var project = new Project();
        var source = project.EnsureActiveTrack();

        PassPlanning.AddTrack(project, Result(source, "ko"), TrackPassKind.Translation, new HangulFonts(), fontsDirectory: null);

        project.Presentation.Bilingual.SecondaryStyle.FontName.ShouldBe("Malgun Gothic");
    }

    private sealed class HangulFonts : IFontCatalog
    {
        public IReadOnlyList<string> Families(string? projectFontDirectory) => ["Arial", "Malgun Gothic"];

        public FontStatus Check(string family, string? projectFontDirectory) => new(FontSource.System, true, HasHangul: family == "Malgun Gothic");

        public IReadOnlyList<string> FamiliesIn(string fontFile) => [];

        public string? SuggestHangulFamily(string? projectFontDirectory) => "Malgun Gothic";
    }
}
