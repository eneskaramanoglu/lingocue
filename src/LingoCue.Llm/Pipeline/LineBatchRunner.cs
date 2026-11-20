using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using LingoCue.Llm.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LingoCue.Llm.Pipeline;

/// <summary>
/// Runs a line task over subtitle lines in ID-keyed JSON batches with read-only context, structured
/// output, validation with corrective retries, rate-limit back-off, optional reflect and shortening
/// passes, bounded parallelism, progress and cancellation. Timing never passes through the model.
/// </summary>
public sealed partial class LineBatchRunner(IChatClient client, ILogger<LineBatchRunner> logger)
{
    /// <summary>Single lines failing in a row before the model is treated as unavailable for this run.</summary>
    internal const int MaxConsecutiveFailures = 3;

    /// <summary>Overridable for tests (back-off waits).</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public async Task<PipelineResult> RunAsync(
        ILineTask task,
        IReadOnlyList<LineItem> lines,
        TaskContext context,
        PipelineOptions options,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(lines);
        var results = new ConcurrentDictionary<int, LineResult>();
        foreach (var locked in lines.Where(l => l.Locked))
        {
            results[locked.Id] = new LineResult(locked.Id, locked.Text, LineStatus.Skipped);
        }

        var work = lines.Where(l => !l.Locked).ToList();
        var batches = work.Chunk(Math.Max(1, options.BatchSize)).ToList();
        var passes = 1 + (options.Reflect ? 1 : 0);
        var total = (batches.Count * passes) + (options.ShortenOverlong ? 1 : 0);
        var done = 0;
        var usage = new UsageCounter();
        var failedBatches = 0;

        void Report(string stage) => progress?.Report(new PipelineProgress(stage, Interlocked.Increment(ref done), total));

        // Pass 1: the task itself. A batch the model does not answer (timeouts, rate limits after all retries)
        // is split in half and retried; a single line that still fails is marked failed and the run goes on, so
        // finished batches are never lost. After several failures in a row the model is treated as unavailable
        // and the remaining lines are marked without calling it again.
        var consecutiveFailures = 0;
        async Task RunOrSplitAsync(LineItem[] batch)
        {
            if (Volatile.Read(ref consecutiveFailures) >= MaxConsecutiveFailures)
            {
                MarkFailed(batch, "Model yanıt vermediği için atlandı; daha sonra yeniden deneyin.");
                return;
            }

            try
            {
                var outcome = await RunBatchAsync(task, batch, BuildPrompt(batch, lines, results, options, drafts: null), task.SystemPrompt(context), context, options, usage, cancellationToken);
                if (outcome.Failed)
                {
                    Interlocked.Increment(ref failedBatches);
                }

                foreach (var r in outcome.Results)
                {
                    results[r.Id] = r;
                }

                Interlocked.Exchange(ref consecutiveFailures, 0);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (ex is LlmException { IsUnanswered: true } || IsTransient(ex)))
            {
                LogBatchUnanswered(task.Name, batch[0].Id, batch.Length, ex.Message);
                if (batch.Length > 1)
                {
                    var half = batch.Length / 2;
                    await RunOrSplitAsync(batch[..half]);
                    await RunOrSplitAsync(batch[half..]);
                    return;
                }

                Interlocked.Increment(ref consecutiveFailures);
                Interlocked.Increment(ref failedBatches);
                MarkFailed(batch, "Model zamanında yanıt vermedi; daha sonra yeniden deneyin.");
            }
        }

        void MarkFailed(LineItem[] batch, string note)
        {
            foreach (var line in batch)
            {
                results[line.Id] = new LineResult(line.Id, line.Text, LineStatus.Failed, note);
            }
        }

        await ForEachBatchAsync(batches, options, async batch =>
        {
            await RunOrSplitAsync(batch);
            Report(task.Name);
        }, cancellationToken);

        // Pass 2 (optional): review and improve the drafts.
        if (options.Reflect)
        {
            await ForEachBatchAsync(batches, options, async batch =>
            {
                var drafts = batch.ToDictionary(l => l.Id, l => results.TryGetValue(l.Id, out var r) ? r.Text : l.Text);
                BatchOutcome outcome;
                try
                {
                    outcome = await RunBatchAsync(task, batch, BuildPrompt(batch, lines, results, options, drafts), task.ReflectPrompt(context), context, options, usage, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (ex is LlmException { IsUnanswered: true } || IsTransient(ex)))
                {
                    // The review is optional: keep the first drafts.
                    LogBatchUnanswered(task.Name + " (gözden geçirme)", batch[0].Id, batch.Length, ex.Message);
                    Report(task.Name + " (gözden geçirme)");
                    return;
                }

                foreach (var r in outcome.Results.Where(r => r.Status != LineStatus.Failed))
                {
                    var first = results[r.Id];
                    var status = r.Status == LineStatus.Unchanged && first.Status != LineStatus.Unchanged ? first.Status : r.Status;
                    results[r.Id] = r with { Status = status, Note = r.Note ?? first.Note };
                }

                Report(task.Name + " (gözden geçirme)");
            }, cancellationToken);
        }

        // Pass 3 (optional): shorten lines that read too fast.
        if (options.ShortenOverlong)
        {
            var overlong = work
                .Select(l => (Source: l, Current: results.TryGetValue(l.Id, out var r) ? r : null))
                .Where(x => x.Current is { Status: not LineStatus.Failed } && options.Rules.Cps(x.Current.Text, x.Source.Duration) > options.Rules.MaxCps)
                .Select(x => x.Source with { Text = x.Current!.Text, MaxChars = ShortenTask.MaxChars(x.Source, options.Rules) })
                .ToList();
            if (overlong.Count > 0)
            {
                var shorten = new ShortenTask();
                foreach (var batch in overlong.Chunk(Math.Max(1, options.BatchSize)))
                {
                    BatchOutcome outcome;
                    try
                    {
                        outcome = await RunBatchAsync(shorten, batch, BuildPrompt(batch, batch, results, options with { PreviousContext = 0, NextContext = 0 }, null), shorten.SystemPrompt(context), context, options, usage, cancellationToken);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (ex is LlmException { IsUnanswered: true } || IsTransient(ex)))
                    {
                        // Shortening is optional: the lines stay as translated (the QA tab shows them as fast).
                        LogBatchUnanswered(shorten.Name, batch[0].Id, batch.Length, ex.Message);
                        continue;
                    }

                    foreach (var r in outcome.Results.Where(r => r.Status is LineStatus.Changed or LineStatus.NeedsReview))
                    {
                        var before = results[r.Id];
                        results[r.Id] = before with { Text = r.Text, Status = before.Status == LineStatus.Unchanged ? LineStatus.Changed : before.Status, Note = "Okuma hızı için kısaltıldı" };
                    }
                }
            }

            Report("Kısaltma");
        }

        var ordered = lines.Select(l => results.TryGetValue(l.Id, out var r) ? r : new LineResult(l.Id, l.Text, LineStatus.Failed, "İşlenemedi")).ToList();
        var changed = ordered.Count(r => r.Status is LineStatus.Changed or LineStatus.NeedsReview);
        var totals = usage.Value;
        LogFinished(task.Name, ordered.Count, changed, failedBatches, totals.InputTokens, totals.OutputTokens);
        return new PipelineResult(ordered, usage.Value, failedBatches);
    }

    private static async Task ForEachBatchAsync(IReadOnlyList<LineItem[]> batches, PipelineOptions options, Func<LineItem[], Task> body, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, options.Parallelism));
        var running = batches.Select(async batch =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await body(batch);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(running);
    }

    private static string BuildPrompt(
        LineItem[] batch,
        IReadOnlyList<LineItem> all,
        ConcurrentDictionary<int, LineResult> results,
        PipelineOptions options,
        Dictionary<int, string>? drafts)
    {
        var first = IndexOf(all, batch[0].Id);
        var last = IndexOf(all, batch[^1].Id);
        var before = first < 0 ? [] : all.Skip(Math.Max(0, first - options.PreviousContext)).Take(Math.Min(first, options.PreviousContext));
        var after = last < 0 ? [] : all.Skip(last + 1).Take(options.NextContext);

        var sb = new StringBuilder();
        var beforeList = before.ToList();
        if (beforeList.Count > 0)
        {
            sb.Append("Previous lines (read-only context, do NOT return them):\n");
            sb.Append(LineJson.Serialize(beforeList.Select(l => (l.Id, results.TryGetValue(l.Id, out var r) && r.Status != LineStatus.Failed ? r.Text : l.Text, l.Speaker))));
            sb.Append("\n\n");
        }

        if (drafts is null)
        {
            sb.Append("Lines to process. Return every id exactly once, in the same order, as {\"lines\":[{\"id\":…,\"text\":…}]}:\n");
            sb.Append(LineJson.Serialize(batch));
        }
        else
        {
            sb.Append("Source lines:\n");
            sb.Append(LineJson.Serialize(batch));
            sb.Append("\n\nDraft results to review. Return every id exactly once with the final text:\n");
            sb.Append(LineJson.Serialize(batch.Select(l => (l.Id, drafts[l.Id], (string?)null))));
        }

        var afterList = after.ToList();
        if (afterList.Count > 0)
        {
            sb.Append("\n\nFollowing lines (read-only context, do NOT return them):\n");
            sb.Append(LineJson.Serialize(afterList.Select(l => (l.Id, l.Text, l.Speaker))));
        }

        return sb.ToString();
    }

    private static int IndexOf(IReadOnlyList<LineItem> all, int id)
    {
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private async Task<BatchOutcome> RunBatchAsync(
        ILineTask task, LineItem[] batch, string prompt, string system, TaskContext context,
        PipelineOptions options, UsageCounter usage, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages = [new(ChatRole.System, system), new(ChatRole.User, prompt)];
        var chatOptions = new ChatOptions
        {
            Temperature = (float)options.Temperature,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(LineJson.Schema, "subtitle_lines", "Subtitle lines keyed by id."),
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low },
        };

        Dictionary<int, string>? lastParsed = null;
        for (var attempt = 0; attempt <= options.MaxRetries; attempt++)
        {
            var response = await CallWithBackoffAsync(messages, chatOptions, options, cancellationToken);
            usage.Add(response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);

            var parsed = LineJson.TryParse(response.Text, out var parseError);
            var errors = new List<string>();
            if (parsed is null)
            {
                errors.Add(parseError!);
            }
            else
            {
                lastParsed = parsed;
                var expected = batch.Select(l => l.Id).ToHashSet();
                var missing = expected.Where(id => !parsed.ContainsKey(id)).ToList();
                var extra = parsed.Keys.Where(id => !expected.Contains(id)).ToList();
                if (missing.Count > 0)
                {
                    errors.Add($"Missing ids: {string.Join(", ", missing)}.");
                }

                if (extra.Count > 0)
                {
                    errors.Add($"Unexpected ids (return only the requested ones): {string.Join(", ", extra)}.");
                }

                foreach (var line in batch.Where(l => parsed.ContainsKey(l.Id)))
                {
                    if (task.Validate(line, Clean(parsed[line.Id]), context, options) is { } error)
                    {
                        errors.Add(error);
                    }
                }
            }

            if (errors.Count == 0)
            {
                return new BatchOutcome(batch.Select(l => Accept(task, l, Clean(parsed![l.Id]), options)).ToList(), Failed: false);
            }

            LogInvalid(task.Name, batch[0].Id, attempt + 1, string.Join(" ", errors));
            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text));
            messages.Add(new ChatMessage(ChatRole.User,
                "Your answer has problems:\n- " + string.Join("\n- ", errors) +
                "\nReturn the complete corrected JSON for all requested ids."));
        }

        // Out of retries: keep the lines that are individually valid, fail the rest.
        var results = batch.Select(l =>
            lastParsed is not null && lastParsed.TryGetValue(l.Id, out var text) && task.Validate(l, Clean(text), context, options) is null
                ? Accept(task, l, Clean(text), options)
                : new LineResult(l.Id, l.Text, LineStatus.Failed, "Model geçerli bir yanıt vermedi; orijinal metin korundu.")).ToList();
        return new BatchOutcome(results, Failed: true);
    }

    private static string Clean(string text)
        => string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal)
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    private static LineResult Accept(ILineTask task, LineItem source, string output, PipelineOptions options)
    {
        if (string.Equals(output, source.Text.Trim(), StringComparison.Ordinal))
        {
            return new LineResult(source.Id, source.Text, LineStatus.Unchanged);
        }

        return task.NeedsReview(source, output, options)
            ? new LineResult(source.Id, output, LineStatus.NeedsReview, "Büyük değişiklik; gözden geçirin.")
            : new LineResult(source.Id, output, LineStatus.Changed);
    }

    private Task<ChatResponse> CallWithBackoffAsync(List<ChatMessage> messages, ChatOptions chatOptions, PipelineOptions options, CancellationToken cancellationToken)
        => ResilientChat.GetResponseAsync(client, messages, chatOptions, options, Delay, LogTransient, cancellationToken);

    /// <summary>Rate limits and server-side hiccups worth retrying; everything else surfaces.</summary>
    internal static bool IsTransient(Exception ex)
    {
        // Google.GenAI reports the HTTP status in its own StatusCode (the HttpRequestException one stays
        // empty), and a 503 "high demand" message names no status at all.
        if (ex is Google.GenAI.ServerError)
        {
            return true;
        }

        if (ex is Google.GenAI.ApiException { StatusCode: > 0 } api)
        {
            return api.StatusCode is 408 or 429 or >= 500 ||
                   api.Status is "RESOURCE_EXHAUSTED" or "UNAVAILABLE" or "INTERNAL" or "DEADLINE_EXCEEDED";
        }

        if (ex is HttpRequestException { StatusCode: { } code })
        {
            return code is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
        }

        if (ex is TimeoutException or TaskCanceledException)
        {
            return true;
        }

        var text = ex.Message;
        return text.Contains("429", StringComparison.Ordinal) || text.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("503", StringComparison.Ordinal) || text.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("500", StringComparison.Ordinal) || text.Contains("overloaded", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("high demand", StringComparison.OrdinalIgnoreCase) || text.Contains("try again later", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record BatchOutcome(IReadOnlyList<LineResult> Results, bool Failed);

    private sealed class UsageCounter
    {
        private readonly Lock _gate = new();

        public LlmUsage Value { get; private set; } = LlmUsage.Zero;

        public void Add(long input, long output)
        {
            lock (_gate)
            {
                Value = Value.Add(input, output);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Task}: batch starting at line {FirstId}, attempt {Attempt} invalid: {Errors}")]
    private partial void LogInvalid(string task, int firstId, int attempt, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Task}: batch starting at line {FirstId} ({Count} lines) got no answer: {Error}")]
    private partial void LogBatchUnanswered(string task, int firstId, int count, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM call failed with {Error}; retrying in {Delay}")]
    private partial void LogTransient(string error, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Task}: {Lines} lines, {Changed} changed, {FailedBatches} failed batches, {Input} in / {Output} out tokens")]
    private partial void LogFinished(string task, int lines, int changed, int failedBatches, long input, long output);
}
