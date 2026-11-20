using LingoCue.Llm.Providers;

namespace LingoCue.Llm.Pipeline;

public sealed record CostEstimate(long InputTokens, long OutputTokens, decimal? Usd, bool FreeTierAvailable)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Conservative pre-run estimate. Turkish tokenizes at roughly 3 characters per token; each batch
/// repeats the system prompt, glossary and context lines; output includes JSON overhead and low
/// "thinking" tokens. Real usage is reported after the run.
/// </summary>
public static class CostEstimator
{
    private const double CharsPerToken = 3.0;
    private const int SystemPromptTokens = 900;
    private const int PerLineJsonTokens = 12;
    private const double ThinkingFactor = 1.5;

    public static CostEstimate Estimate(
        IReadOnlyList<LineItem> lines, PipelineOptions options, int glossaryEntries, bool includeContextPass, LlmPrice? price)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);
        var work = lines.Where(l => !l.Locked).ToList();
        if (work.Count == 0)
        {
            return new CostEstimate(0, 0, 0, price?.HasFreeTier ?? false);
        }

        var avgLineTokens = work.Average(l => l.Text.Length) / CharsPerToken;
        var batches = (int)Math.Ceiling(work.Count / (double)Math.Max(1, options.BatchSize));
        var perBatchFixed = SystemPromptTokens + (glossaryEntries * 12) + ((options.PreviousContext + options.NextContext) * (avgLineTokens + PerLineJsonTokens));
        var linesTokens = work.Sum(l => ((l.Text.Length + (l.Reference is { } r && r != l.Text ? r.Length : 0)) / CharsPerToken) + PerLineJsonTokens);

        var passes = options.Reflect ? 2 : 1;
        double input = ((batches * perBatchFixed) + linesTokens) * passes;
        if (options.Reflect)
        {
            input += linesTokens; // the reflect pass sends source + draft
        }

        double output = linesTokens * ThinkingFactor * passes;
        if (options.ShortenOverlong)
        {
            // Typically a handful of lines; budget 10 % of one pass.
            input += (linesTokens * 0.1) + SystemPromptTokens;
            output += linesTokens * 0.1 * ThinkingFactor;
        }

        if (includeContextPass)
        {
            input += (work.Sum(l => l.Text.Length) / CharsPerToken) + 500;
            output += 1500;
        }

        var inputTokens = (long)Math.Ceiling(input);
        var outputTokens = (long)Math.Ceiling(output);
        return new CostEstimate(inputTokens, outputTokens, price?.Cost(inputTokens, outputTokens), price?.HasFreeTier ?? false);
    }

    public static decimal? Cost(LlmUsage usage, LlmPrice? price) => price?.Cost(usage.InputTokens, usage.OutputTokens);
}
