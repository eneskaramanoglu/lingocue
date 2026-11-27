using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Llm.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Llm.Tests;

public class MixedLanguagePassTests
{
    private static (Project Project, SubtitleTrack Track) MixedProject()
    {
        var project = new Project();
        project.Context = new ProjectContext { Summary = "Gezi." };
        var track = project.EnsureActiveTrack();
        track.Language = "tr";
        track.AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "bugün istanbuldayız").Language = "tr";
        track.AddCue(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "thank you for watching").Language = "en";
        return (project, track);
    }

    private static string LastUserText(IReadOnlyList<ChatMessage> messages) => messages.Last(m => m.Role == ChatRole.User).Text;

    [Fact]
    public async Task Translation_keeps_lines_already_in_the_target_language_and_sends_the_others_with_lang()
    {
        var (project, source) = MixedProject();
        string? sent = null;
        string? system = null;
        var client = new FakeChatClient((m, _, _) =>
        {
            sent = LastUserText(m);
            system = m.First(x => x.Role == ChatRole.System).Text;
            return FakeChatClient.Echo(m, (_, _) => "Today we are in Istanbul");
        });

        var result = await new TrackPassRunner(client, NullLoggerFactory.Instance).RunAsync(
            new TrackPassRequest(project, source, TrackPassKind.Translation, new PipelineOptions { ShortenOverlong = false }, RefreshContext: false,
                TargetLanguage: "en"),
            cancellationToken: TestContext.Current.CancellationToken);

        sent.ShouldNotBeNull().ShouldContain("\"lang\":\"tr\"");
        sent.Split("Following lines")[0].ShouldNotContain("thank you for watching"); // only read-only context
        system.ShouldNotBeNull().ShouldContain("several languages");
        result.Track.Cues.Select(c => c.Text).ShouldBe(["Today we are in Istanbul", "thank you for watching"]);
        result.Track.Cues.ShouldAllBe(c => c.Language == null);
    }

    [Fact]
    public async Task Correction_keeps_each_line_in_its_language_and_rejects_a_translated_answer()
    {
        var (project, source) = MixedProject();
        string? system = null;
        var client = new FakeChatClient((m, _, _) =>
        {
            system = m.First(x => x.Role == ChatRole.System).Text;
            // The model "corrects" the English line by translating it, every time.
            return FakeChatClient.Echo(m, (id, t) => id == 2 ? "izlediğiniz için çok teşekkür ederiz" : "Bugün İstanbul'dayız.");
        });

        var result = await new TrackPassRunner(client, NullLoggerFactory.Instance).RunAsync(
            new TrackPassRequest(project, source, TrackPassKind.Correction, new PipelineOptions { ShortenOverlong = false, Reflect = false }, RefreshContext: false),
            cancellationToken: TestContext.Current.CancellationToken);

        system.ShouldNotBeNull().ShouldContain("NEVER translate a line");
        result.Track.Cues[0].Text.ShouldBe("Bugün İstanbul'dayız.");
        result.Track.Cues[0].Language.ShouldBe("tr");
        result.Track.Cues[1].Text.ShouldBe("thank you for watching"); // rejected, the line keeps its text
        result.Track.Cues[1].Marks.HasFlag(CueMarks.LlmFailed).ShouldBeTrue();
        result.Track.Cues[1].Language.ShouldBe("en");
    }

    [Fact]
    public void Plan_translations_are_added_to_a_track_that_lacks_them()
    {
        var (_, source) = MixedProject();
        var target = new SubtitleTrack { Kind = TrackKind.Translation, Language = "en", SourceTrackId = source.Id };
        var pipeline = new PipelineResult(
            [new LineResult(1, "We are in Istanbul today", LineStatus.Changed), new LineResult(2, "x", LineStatus.Failed)], LlmUsage.Zero, 0);

        var (written, failed) = TrackPassRunner.MergeOrAdd(target, source, pipeline);

        (written, failed).ShouldBe((1, 1));
        var cue = target.Cues.ShouldHaveSingleItem();
        (cue.Text, cue.SourceCueId, cue.Start).ShouldBe(("We are in Istanbul today", 1, source.Cues[0].Start));
    }

    [Fact]
    public void A_one_language_track_sends_no_lang_field()
        => LineJson.Serialize([new LineItem(1, "Merhaba", TimeSpan.FromSeconds(1))]).ShouldNotContain("lang");
}
