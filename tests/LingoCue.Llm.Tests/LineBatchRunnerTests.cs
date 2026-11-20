using System.Net;
using LingoCue.Core.Projects;
using LingoCue.Llm.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Llm.Tests;

public class LineBatchRunnerTests
{
    private static readonly TaskContext s_context = new(new ProjectContext { Summary = "İstanbul gezisi" }, [], "tr");

    private static readonly PipelineOptions s_options = new() { BatchSize = 2, Parallelism = 1, ShortenOverlong = false };

    private static List<LineItem> Lines(params string[] texts)
        => [.. texts.Select((t, i) => new LineItem(i + 1, t, TimeSpan.FromSeconds(3)))];

    private static (LineBatchRunner Runner, List<TimeSpan> Delays) Runner(FakeChatClient client)
    {
        var delays = new List<TimeSpan>();
        var runner = new LineBatchRunner(client, NullLogger<LineBatchRunner>.Instance)
        {
            Delay = (d, _) =>
            {
                delays.Add(d);
                return Task.CompletedTask;
            },
        };
        return (runner, delays);
    }

    [Fact]
    public async Task Corrects_in_batches_keeps_order_and_sums_usage()
    {
        var client = new FakeChatClient((m, _, _) => FakeChatClient.Echo(m, (id, t) => id == 2 ? "Boğaz'ın kıyısında." : t));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), Lines("Merhaba.", "Boğazın kıyısında", "Görüşürüz."), s_context, s_options,
            cancellationToken: TestContext.Current.CancellationToken);

        client.CallCount.ShouldBe(2); // 3 lines, batch size 2
        result.Lines.Select(l => l.Id).ShouldBe([1, 2, 3]);
        result.Lines[1].Text.ShouldBe("Boğaz'ın kıyısında.");
        result.Lines[1].Status.ShouldBe(LineStatus.Changed);
        result.Lines[0].Status.ShouldBe(LineStatus.Unchanged);
        result.Usage.ShouldBe(new LlmUsage(200, 100, 2));
        result.FailedBatches.ShouldBe(0);
        client.Calls[0].Options!.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
        client.Calls[0].Messages[0].Text.ShouldContain("İstanbul gezisi");
    }

    [Fact]
    public async Task Retries_with_the_validation_errors_when_an_id_is_missing()
    {
        var client = new FakeChatClient((m, _, i) => i == 0
            ? FakeChatClient.Json("""{"lines":[{"id":1,"text":"bir"}]}""")
            : FakeChatClient.Echo(m, (_, t) => t));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), Lines("bir", "iki"), s_context, s_options,
            cancellationToken: TestContext.Current.CancellationToken);

        client.CallCount.ShouldBe(2);
        client.Calls[1].Messages[^1].Text.ShouldContain("Missing ids: 2");
        client.Calls[1].Messages.Count.ShouldBe(4); // system, user, assistant (bad answer), user (errors)
        result.Lines.ShouldAllBe(l => l.Status == LineStatus.Unchanged);
    }

    [Fact]
    public async Task Gives_up_after_retries_and_keeps_the_source_text()
    {
        var client = new FakeChatClient((_, _, _) => FakeChatClient.Json("not json at all"));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), Lines("bir", "iki"), s_context, s_options,
            cancellationToken: TestContext.Current.CancellationToken);

        client.CallCount.ShouldBe(3); // first try + 2 retries
        result.FailedBatches.ShouldBe(1);
        result.Lines.ShouldAllBe(l => l.Status == LineStatus.Failed);
        result.Lines.Select(l => l.Text).ShouldBe(["bir", "iki"]);
    }

    [Fact]
    public async Task On_the_last_attempt_keeps_the_valid_lines()
    {
        // Line 2 is always returned empty (invalid); line 1 is fine.
        var client = new FakeChatClient((_, _, _) => FakeChatClient.Json("""{"lines":[{"id":1,"text":"Bir."},{"id":2,"text":""}]}"""));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), Lines("bir", "iki"), s_context, s_options,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Lines[0].Status.ShouldBe(LineStatus.Changed);
        result.Lines[0].Text.ShouldBe("Bir.");
        result.Lines[1].Status.ShouldBe(LineStatus.Failed);
    }

    [Fact]
    public async Task Enforces_locked_glossary_terms()
    {
        var context = s_context with { Glossary = [new GlossaryEntry { Term = "lingoq", Preferred = "LingoCue", Locked = true }] };
        var client = new FakeChatClient((m, _, i) => FakeChatClient.Echo(m, (_, t) => i == 0 ? t : t.Replace("lingoq", "LingoCue", StringComparison.Ordinal)));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), Lines("lingoq ile altyazı"), context, s_options,
            cancellationToken: TestContext.Current.CancellationToken);

        client.Calls[1].Messages[^1].Text.ShouldContain("\"LingoCue\"");
        result.Lines[0].Text.ShouldBe("LingoCue ile altyazı");
    }

    [Fact]
    public async Task Locked_lines_are_not_sent()
    {
        var lines = Lines("bir", "iki");
        lines[0] = lines[0] with { Locked = true };
        var client = new FakeChatClient((m, _, _) => FakeChatClient.Echo(m, (_, t) => t + "!"));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), lines, s_context, s_options, cancellationToken: TestContext.Current.CancellationToken);

        FakeChatClient.RequestedLines(client.Calls[0].Messages).Select(l => l.Id).ShouldBe([2]);
        result.Lines[0].Status.ShouldBe(LineStatus.Skipped);
        result.Lines[0].Text.ShouldBe("bir");
        result.Lines[1].Text.ShouldBe("iki!");
    }

    [Fact]
    public async Task Backs_off_on_rate_limits_then_succeeds()
    {
        var client = new FakeChatClient((m, _, i) => i < 2
            ? throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests)
            : FakeChatClient.Echo(m, (_, t) => t));
        var (runner, delays) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), Lines("bir"), s_context, s_options, cancellationToken: TestContext.Current.CancellationToken);

        delays.ShouldBe([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)]);
        result.Lines[0].Status.ShouldBe(LineStatus.Unchanged);
    }

    [Fact]
    public async Task Non_transient_errors_surface()
    {
        var client = new FakeChatClient((_, _, _) => throw new HttpRequestException("bad key", null, HttpStatusCode.Forbidden));
        var (runner, _) = Runner(client);

        await Should.ThrowAsync<HttpRequestException>(() => runner.RunAsync(new CorrectionTask(), Lines("bir"), s_context, s_options,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (runner, _) = Runner(new FakeChatClient((m, _, _) => FakeChatClient.Echo(m, (_, t) => t)));

        await Should.ThrowAsync<OperationCanceledException>(() => runner.RunAsync(new CorrectionTask(), Lines("bir"), s_context, s_options, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Later_batches_see_earlier_results_as_context()
    {
        var client = new FakeChatClient((m, _, _) => FakeChatClient.Echo(m, (_, t) => t.ToUpperInvariant()));
        var (runner, _) = Runner(client);

        await runner.RunAsync(new CorrectionTask(), Lines("bir", "iki", "üç"), s_context, s_options, cancellationToken: TestContext.Current.CancellationToken);

        var second = client.Calls[1].Messages[1].Text;
        second.ShouldContain("Previous lines");
        second.ShouldContain("\"text\":\"IKI\"");
        client.Calls[0].Messages[1].Text.ShouldContain("Following lines");
    }

    [Fact]
    public async Task Flags_large_rewrites_for_review()
    {
        var client = new FakeChatClient((m, _, _) => FakeChatClient.Echo(m, (_, _) => "Tamamen farklı bir cümle oldu."));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), Lines("evet tamam"), s_context, s_options, cancellationToken: TestContext.Current.CancellationToken);

        result.Lines[0].Status.ShouldBe(LineStatus.NeedsReview);
        result.ReviewCount.ShouldBe(1);
    }

    [Fact]
    public async Task Shortens_lines_that_read_too_fast()
    {
        var lines = new List<LineItem> { new(1, "Bu satır bir buçuk saniyede okunamayacak kadar uzun yazılmış.", TimeSpan.FromSeconds(1.5)) };
        var client = new FakeChatClient((m, _, i) => i == 0
            ? FakeChatClient.Echo(m, (_, t) => t)
            : FakeChatClient.Echo(m, (_, _) => "Bu satır çok uzun."));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new CorrectionTask(), lines, s_context, s_options with { ShortenOverlong = true },
            cancellationToken: TestContext.Current.CancellationToken);

        client.CallCount.ShouldBe(2);
        client.Calls[1].Messages[1].Text.ShouldContain("\"maxChars\":25");
        result.Lines[0].Text.ShouldBe("Bu satır çok uzun.");
        result.Lines[0].Note.ShouldBe("Okuma hızı için kısaltıldı");
    }

    [Fact]
    public async Task Reflect_pass_reviews_drafts_and_reports_progress()
    {
        var client = new FakeChatClient((m, _, i) => FakeChatClient.Echo(m, (_, t) => i == 0 ? t + "," : t.TrimEnd(',') + "."));
        var (runner, _) = Runner(client);
        var progress = new List<PipelineProgress>();

        var result = await runner.RunAsync(new CorrectionTask(), Lines("evet"), s_context, s_options with { Reflect = true },
            new SyncProgress(progress), TestContext.Current.CancellationToken);

        client.CallCount.ShouldBe(2);
        client.Calls[1].Messages[0].Text.ShouldContain("draft correction");
        client.Calls[1].Messages[1].Text.ShouldContain("\"text\":\"evet,\"");
        result.Lines[0].Text.ShouldBe("evet.");
        progress[^1].Done.ShouldBe(progress[^1].Total);
    }

    [Fact]
    public async Task Translation_rejects_untranslated_lines()
    {
        var client = new FakeChatClient((m, _, i) => FakeChatClient.Echo(m, (_, t) => i == 0 ? t : "Good morning, everyone."));
        var (runner, _) = Runner(client);

        var result = await runner.RunAsync(new TranslationTask(), Lines("Herkese günaydın arkadaşlar."), s_context with { TargetLanguage = "en" }, s_options,
            cancellationToken: TestContext.Current.CancellationToken);

        client.Calls[1].Messages[^1].Text.ShouldContain("untranslated");
        result.Lines[0].Text.ShouldBe("Good morning, everyone.");
        client.Calls[0].Messages[0].Text.ShouldContain("from Turkish to English");
    }

    [Theory]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    public void Classifies_transient_errors(Type type, bool expected)
        => LineBatchRunner.IsTransient((Exception)Activator.CreateInstance(type, "x")!).ShouldBe(expected);

    [Fact]
    public void Treats_quota_messages_as_transient()
        => LineBatchRunner.IsTransient(new InvalidOperationException("Status 429 RESOURCE_EXHAUSTED")).ShouldBeTrue();

    [Fact]
    public void Google_server_errors_are_transient_but_client_errors_are_not()
    {
        // Seen live: a 503 whose message names no status code.
        const string highDemand = "This model is currently experiencing high demand. Spikes in demand are usually temporary. Please try again later.";
        LineBatchRunner.IsTransient(new Google.GenAI.ServerError(highDemand, 503, "UNAVAILABLE", null!)).ShouldBeTrue();
        LineBatchRunner.IsTransient(new Google.GenAI.ServerError(highDemand, null!)).ShouldBeTrue();
        LineBatchRunner.IsTransient(new Google.GenAI.ClientError("Quota exceeded", 429, "RESOURCE_EXHAUSTED", null!)).ShouldBeTrue();
        LineBatchRunner.IsTransient(new Google.GenAI.ClientError("API key not valid", 400, "INVALID_ARGUMENT", null!)).ShouldBeFalse();
    }

    private sealed class SyncProgress(List<PipelineProgress> values) : IProgress<PipelineProgress>
    {
        public void Report(PipelineProgress value) => values.Add(value);
    }
}
