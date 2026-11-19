using System.Text.Json;
using System.Text.Json.Nodes;

namespace LingoCue.Llm.Pipeline;

/// <summary>JSON contract for line batches: <c>{"lines":[{"id":1,"text":"…"}]}</c>.</summary>
internal static class LineJson
{
    public static JsonElement Schema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "lines": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "id": { "type": "integer" },
                  "text": { "type": "string" }
                },
                "required": ["id", "text"]
              }
            }
          },
          "required": ["lines"]
        }
        """).RootElement.Clone();

    public static string Serialize(IEnumerable<(int Id, string Text, string? Speaker)> lines)
        => Serialize(lines.Select(l => (l.Id, l.Text, l.Speaker, (int?)null)));

    public static string Serialize(IEnumerable<(int Id, string Text, string? Speaker, int? MaxChars)> lines)
    {
        var array = new JsonArray();
        foreach (var (id, text, speaker, maxChars) in lines)
        {
            var item = new JsonObject { ["id"] = id, ["text"] = text };
            if (maxChars is { } max)
            {
                item["maxChars"] = max;
            }

            if (!string.IsNullOrWhiteSpace(speaker))
            {
                item["speaker"] = speaker;
            }

            array.Add(item);
        }

        return new JsonObject { ["lines"] = array }.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    /// <summary>Parses the model's answer; tolerates code fences and a bare array. Null when unreadable.</summary>
    public static Dictionary<int, string>? TryParse(string? text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The answer was empty.";
            return null;
        }

        var json = StripFences(text);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var lines = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("lines", out var l) ? l : default;
            if (lines.ValueKind != JsonValueKind.Array)
            {
                error = "The answer has no \"lines\" array.";
                return null;
            }

            var result = new Dictionary<int, string>();
            foreach (var item in lines.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) ||
                    !item.TryGetProperty("text", out var t) || t.ValueKind != JsonValueKind.String)
                {
                    error = "Every item needs an integer \"id\" and a string \"text\".";
                    return null;
                }

                if (!result.TryAdd(number, t.GetString()!))
                {
                    error = $"Id {number} appears more than once.";
                    return null;
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            error = $"The answer is not valid JSON ({ex.Message}).";
            return null;
        }
    }

    internal static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? trimmed[(firstNewline + 1)..lastFence].Trim() : trimmed;
    }
}
