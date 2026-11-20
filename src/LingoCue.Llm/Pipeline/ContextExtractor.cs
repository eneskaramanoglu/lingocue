using System.Text;
using System.Text.Json;
using LingoCue.Core.Projects;
using Microsoft.Extensions.AI;

namespace LingoCue.Llm.Pipeline;

public sealed record ContextResult(ProjectContext Context, IReadOnlyList<GlossaryEntry> Suggestions, LlmUsage Usage);

/// <summary>
/// One call over the whole transcript: summary, tone, form of address, speakers and proper nouns /
/// terms with suggested spellings. The result seeds the editable project glossary.
/// </summary>
public sealed class ContextExtractor(IChatClient client)
{
    /// <summary>Roughly 60k tokens; longer transcripts are sampled from start, middle and end.</summary>
    private const int MaxTranscriptChars = 200_000;

    private static readonly JsonElement s_schema = JsonElement.Parse("""
        {
          "type": "object",
          "properties": {
            "summary": { "type": "string" },
            "tone": { "type": "string" },
            "address": { "type": "string", "enum": ["sen", "siz", "karışık", "bilinmiyor"] },
            "speakers": { "type": "array", "items": { "type": "string" } },
            "terms": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "term": { "type": "string" },
                  "preferred": { "type": "string" },
                  "kind": { "type": "string", "enum": ["properNoun", "term", "other"] },
                  "note": { "type": "string" }
                },
                "required": ["term", "kind"]
              }
            }
          },
          "required": ["summary", "tone", "address", "speakers", "terms"]
        }
        """);

    /// <summary>Retry/timeout policy for the call (the pipeline's options when run from a track pass).</summary>
    internal PipelineOptions Policy { get; init; } = new();

    /// <summary>Replaceable wait for tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public async Task<ContextResult> ExtractAsync(
        IReadOnlyList<LineItem> lines, string sourceLanguage, IReadOnlyList<GlossaryEntry> existingGlossary, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var transcript = Sample(lines);
        var known = existingGlossary.Count == 0 ? "(none)" : string.Join(", ", existingGlossary.Select(g => g.Effective));
        var system = $"""
            You analyse a video transcript produced by speech recognition (language: {sourceLanguage}) to help
            proofread and translate its subtitles. Write summary and tone in Turkish, 1–3 sentences each.
            "address" is how the speaker addresses the audience in Turkish terms: sen, siz, karışık or bilinmiyor.
            "speakers": names or roles if they can be inferred, otherwise the speaker labels.
            "terms": proper nouns (people, places, brands, products, channels) and domain terms that recognition
            may have misspelled. For each, "term" is the form as it appears (or would appear) and "preferred" the
            correct spelling when you are confident; leave "preferred" empty when unsure. At most 40 terms,
            most important first. Skip terms already in the glossary: {known}.
            Answer with JSON only.
            """;

        var response = await ResilientChat.GetResponseAsync(
            client,
            [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, transcript)],
            new ChatOptions
            {
                Temperature = 0.2f,
                ResponseFormat = ChatResponseFormat.ForJsonSchema(s_schema, "video_context"),
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low },
            },
            Policy,
            Delay,
            onRetry: null,
            cancellationToken);

        var usage = LlmUsage.Zero.Add(response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
        try
        {
            using var doc = JsonDocument.Parse(LineJson.StripFences(response.Text));
            var root = doc.RootElement;
            var context = new ProjectContext
            {
                Summary = Text(root, "summary"),
                Tone = Text(root, "tone"),
                Address = Text(root, "address") is { } a && a != "bilinmiyor" ? a : null,
                Speakers = root.TryGetProperty("speakers", out var s) && s.ValueKind == JsonValueKind.Array
                    ? [.. s.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)]
                    : [],
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var terms = new List<GlossaryEntry>();
            if (root.TryGetProperty("terms", out var t) && t.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in t.EnumerateArray())
                {
                    if (Text(item, "term") is not { } term)
                    {
                        continue;
                    }

                    terms.Add(new GlossaryEntry
                    {
                        Term = term,
                        Preferred = Text(item, "preferred") is { } p && p != term ? p : null,
                        Kind = Text(item, "kind") switch { "properNoun" => GlossaryKind.ProperNoun, "term" => GlossaryKind.Term, _ => GlossaryKind.Other },
                        Note = Text(item, "note"),
                        Suggested = true,
                    });
                }
            }

            return new ContextResult(context, terms, usage);
        }
        catch (JsonException ex)
        {
            throw new Providers.LlmException("The context answer was not valid JSON.", ex);
        }
    }

    private static string? Text(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static string Sample(IReadOnlyList<LineItem> lines)
    {
        static string Line(LineItem l) => string.IsNullOrWhiteSpace(l.Speaker) ? l.Text.Replace('\n', ' ') : $"[{l.Speaker}] {l.Text.Replace('\n', ' ')}";

        var full = string.Join('\n', lines.Select(Line));
        if (full.Length <= MaxTranscriptChars)
        {
            return full;
        }

        var third = MaxTranscriptChars / 3;
        var sb = new StringBuilder();
        sb.Append(full.AsSpan(0, third)).Append("\n[…]\n");
        sb.Append(full.AsSpan((full.Length / 2) - (third / 2), third)).Append("\n[…]\n");
        sb.Append(full.AsSpan(full.Length - third));
        return sb.ToString();
    }
}
