using LingoCue.Core.Projects;
using LingoCue.Core.Settings;
using LingoCue.Core.Subtitles;
using LingoCue.Llm.Pipeline;
using LingoCue.Llm.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Llm.Tests;

public class TrackPassRunnerTests
{
    private const string ContextJson = """
        { "summary": "Gezi.", "tone": "samimi", "address": "siz", "speakers": [],
          "terms": [ { "term": "bogaz", "preferred": "Boğaz", "kind": "properNoun" } ] }
        """;

    private static (Project Project, SubtitleTrack Track) CreateProject()
    {
        var project = new Project();
        var track = project.EnsureActiveTrack();
        track.AddCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "bogaz cok guzel");
        var locked = track.AddCue(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "dokunma bana");
        locked.Marks = CueMarks.Locked;
        track.AddCue(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(9), "tamamen başka hale gelecek");
        project.Glossary.Add(new GlossaryEntry { Term = "İstanbul", Locked = true });
        return (project, track);
    }

    private static FakeChatClient Client() => new((m, o, _) =>
        o?.ResponseFormat is ChatResponseFormatJson { SchemaName: "video_context" }
            ? FakeChatClient.Json(ContextJson)
            : FakeChatClient.Echo(m, (id, t) => id switch
            {
                1 => "Boğaz çok güzel.",
                3 => "Bambaşka bir cümleye dönüştü burada.",
                _ => t,
            }));

    [Fact]
    public async Task Correction_creates_a_linked_track_with_same_timing_and_marks()
    {
        var (project, source) = CreateProject();
        var runner = new TrackPassRunner(Client(), NullLoggerFactory.Instance);

        var result = await runner.RunAsync(new TrackPassRequest(project, source, TrackPassKind.Correction,
            new PipelineOptions { ShortenOverlong = false }, RefreshContext: false), cancellationToken: TestContext.Current.CancellationToken);

        var track = result.Track;
        track.Kind.ShouldBe(TrackKind.Corrected);
        track.SourceTrackId.ShouldBe(source.Id);
        track.Cues.Select(c => (c.Start, c.End)).ShouldBe(source.Cues.Select(c => (c.Start, c.End)));
        track.Cues.Select(c => c.SourceCueId).ShouldBe(source.Cues.Select(c => (int?)c.Id));
        track.Cues[0].Text.ShouldBe("Boğaz çok güzel.");
        track.Cues[1].Text.ShouldBe("dokunma bana");
        track.Cues[1].Marks.ShouldBe(CueMarks.Locked);
        track.Cues[2].Marks.HasFlag(CueMarks.NeedsReview).ShouldBeTrue();
        source.Cues[0].Text.ShouldBe("bogaz cok guzel"); // source untouched
    }

    [Fact]
    public async Task Context_pass_runs_when_missing_and_adds_glossary_suggestions()
    {
        var (project, source) = CreateProject();
        var client = Client();

        var result = await new TrackPassRunner(client, NullLoggerFactory.Instance).RunAsync(
            new TrackPassRequest(project, source, TrackPassKind.Correction, new PipelineOptions { ShortenOverlong = false }, RefreshContext: false),
            cancellationToken: TestContext.Current.CancellationToken);

        project.Context.Summary.ShouldBe("Gezi.");
        result.NewGlossarySuggestions.ShouldBe(1);
        project.Glossary.ShouldContain(g => g.Term == "bogaz" && g.Preferred == "Boğaz" && g.Suggested);
        client.CallCount.ShouldBe(2); // context + one batch
        result.TotalUsage.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task Translating_a_corrected_track_sends_the_recognized_original_as_a_hint()
    {
        var (project, asr) = CreateProject();
        project.Context = new ProjectContext { Summary = "Gezi." };
        var correct = await new TrackPassRunner(Client(), NullLoggerFactory.Instance).RunAsync(
            new TrackPassRequest(project, asr, TrackPassKind.Correction, new PipelineOptions { ShortenOverlong = false }, RefreshContext: false),
            cancellationToken: TestContext.Current.CancellationToken);
        project.Tracks.Add(correct.Track);
        string? prompt = null;
        string? system = null;
        var client = new FakeChatClient((m, _, _) =>
        {
            system = m.First(x => x.Role == ChatRole.System).Text;
            prompt = m.Last(x => x.Role == ChatRole.User).Text;
            return FakeChatClient.Echo(m, (id, _) => $"line {id} in English");
        });

        var result = await new TrackPassRunner(client, NullLoggerFactory.Instance).RunAsync(
            new TrackPassRequest(project, correct.Track, TrackPassKind.Translation, new PipelineOptions { ShortenOverlong = false }, RefreshContext: false, TargetLanguage: "en"),
            cancellationToken: TestContext.Current.CancellationToken);

        prompt.ShouldNotBeNull();
        prompt.ShouldContain("\"text\":\"Boğaz çok güzel.\"");
        prompt.ShouldContain("\"original\":\"bogaz cok guzel\"");
        prompt.ShouldNotContain("\"original\":\"dokunma bana\""); // same as the text: no hint needed
        system!.ShouldContain("no gender");
        result.Track.SourceTrackId.ShouldBe(correct.Track.Id);
        result.Track.Cues.Select(c => (c.Start, c.End)).ShouldBe(asr.Cues.Select(c => (c.Start, c.End)));
    }

    [Fact]
    public void Original_text_follows_the_links_back_to_the_first_track()
    {
        var (project, asr) = CreateProject();
        var corrected = new SubtitleTrack { Kind = TrackKind.Corrected, SourceTrackId = asr.Id };
        var copy = asr.Cues[0].Clone();
        copy.SourceCueId = asr.Cues[0].Id;
        copy.Text = "Boğaz";
        corrected.Cues.Add(copy);
        var english = new SubtitleTrack { Kind = TrackKind.Translation, SourceTrackId = corrected.Id };
        var en = copy.Clone();
        en.SourceCueId = copy.Id;
        en.Text = "The Bosphorus";
        english.Cues.Add(en);
        project.Tracks.AddRange([corrected, english]);

        TrackOperations.OriginalText(project, english, en).ShouldBe("bogaz cok guzel");
        TrackOperations.OriginalText(project, asr, asr.Cues[0]).ShouldBeNull();
        project.Tracks.Remove(corrected);
        TrackOperations.OriginalText(project, english, en).ShouldBeNull(); // broken chain
    }

    [Fact]
    public async Task Retrying_failed_lines_only_sends_those_lines_and_fills_them_in()
    {
        var (project, source) = CreateProject();
        project.Context = new ProjectContext { Summary = "Gezi." };
        var english = new SubtitleTrack { Kind = TrackKind.Translation, Language = "en", SourceTrackId = source.Id };
        foreach (var cue in source.Cues)
        {
            var copy = cue.Clone();
            copy.SourceCueId = cue.Id;
            copy.Text = cue.Id == 3 ? cue.Text : "done";
            copy.Marks = cue.Id == 3 ? CueMarks.LlmFailed : CueMarks.None;
            english.Cues.Add(copy);
        }

        var client = new FakeChatClient((m, _, _) => FakeChatClient.Echo(m, (_, _) => "It turned into something else."));
        var result = await new TrackPassRunner(client, NullLoggerFactory.Instance).RunAsync(
            new TrackPassRequest(project, source, TrackPassKind.Translation, new PipelineOptions { ShortenOverlong = false }, RefreshContext: false,
                TargetLanguage: "en", OnlySourceCueIds: new HashSet<int> { 3 }),
            cancellationToken: TestContext.Current.CancellationToken);
        var (fixedCount, stillFailed) = TrackPassRunner.MergeInto(english, result.Pipeline);

        FakeChatClient.RequestedLines(client.Calls[0].Messages).Select(l => l.Id).ShouldBe([3]);
        (fixedCount, stillFailed).ShouldBe((1, 0));
        english.Cues[2].Text.ShouldBe("It turned into something else.");
        english.Cues[2].Marks.HasFlag(CueMarks.LlmFailed).ShouldBeFalse();
        english.Cues[0].Text.ShouldBe("done"); // untouched
    }

    [Fact]
    public void Pronoun_rule_only_for_turkish_sources()
    {
        TranslationTask.PronounRule(new TaskContext(new ProjectContext(), [], "tr", "en")).ShouldContain("no gender");
        TranslationTask.PronounRule(new TaskContext(new ProjectContext(), [], "en", "tr")).ShouldBeEmpty();
    }

    [Fact]
    public void Korean_targets_get_speech_level_and_spacing_rules()
    {
        var korean = new TranslationTask().SystemPrompt(new TaskContext(new ProjectContext(), [], "tr", "ko"));
        korean.ShouldContain("해요체");
        korean.ShouldContain("Hangul");
        new TranslationTask().SystemPrompt(new TaskContext(new ProjectContext(), [], "tr", "en")).ShouldNotContain("해요체");
        new ShortenTask().SystemPrompt(new TaskContext(new ProjectContext(), [], "tr", "ko")).ShouldContain("punctuation as 0.5");
    }

    [Fact]
    public async Task Translation_uses_target_glossary_and_clears_word_timings()
    {
        var (project, source) = CreateProject();
        project.Context = new ProjectContext { Summary = "Gezi." };
        source.Cues[0].Words.Add(new Word("bogaz", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        var client = new FakeChatClient((m, _, _) => FakeChatClient.Echo(m, (id, _) => $"line {id} in English"));

        var result = await new TrackPassRunner(client, NullLoggerFactory.Instance).RunAsync(
            new TrackPassRequest(project, source, TrackPassKind.Translation, new PipelineOptions { ShortenOverlong = false }, RefreshContext: false, TargetLanguage: "en"),
            cancellationToken: TestContext.Current.CancellationToken);

        client.CallCount.ShouldBe(1); // context already present
        result.Track.Language.ShouldBe("en");
        result.Track.Kind.ShouldBe(TrackKind.Translation);
        result.Track.Cues[0].Words.ShouldBeEmpty();
        result.Track.Cues[0].Text.ShouldBe("line 1 in English");
    }

    [Fact]
    public async Task Resolver_uses_explicit_ids_or_picks_from_the_list()
    {
        var provider = new StubProvider([new("gemini-3.8-flash", "Flash"), new("gemini-2.5-pro", "Pro")]);
        var resolver = new ModelResolver(provider);
        var ct = TestContext.Current.CancellationToken;

        (await resolver.ResolveAsync(new LlmSettings(), ct)).ShouldBe("gemini-3.8-flash");
        (await resolver.ResolveAsync(new LlmSettings { UseQualityModel = true }, ct)).ShouldBe("gemini-3.8-flash");
        (await resolver.ResolveAsync(new LlmSettings { FastModel = "gemini-3.5-flash-lite" }, ct)).ShouldBe("gemini-3.5-flash-lite");
        provider.ListCalls.ShouldBe(1); // cached
    }

    private sealed class StubProvider(LlmModelInfo[] models) : ILlmProvider
    {
        public int ListCalls { get; private set; }

        public string Id => "stub";

        public string DisplayName => "Stub";

        public bool IsConfigured => true;

        public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<LlmModelInfo>>(models);
        }

        public IChatClient CreateChatClient(string modelId) => throw new NotSupportedException();

        public LlmPrice? PriceFor(string modelId) => null;
    }
}
