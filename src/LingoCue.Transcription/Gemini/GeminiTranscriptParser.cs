using System.Globalization;
using System.Text.Json;
using LingoCue.Core.Subtitles;

namespace LingoCue.Transcription.Gemini;

/// <summary>
/// Reads word annotations from a Gemini Interactions response. The documentation shows the words
/// under <c>steps[].content[].annotations[]</c> in one place and <c>outputs[]</c> in another, so the
/// parser collects every object with <c>"type": "word_info"</c> wherever it appears.
/// </summary>
internal static class GeminiTranscriptParser
{
    public static string? Status(JsonElement root)
        => root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;

    public static string? InteractionName(JsonElement root)
        => root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()
         : root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()
         : null;

    public static List<Word> ParseWords(JsonElement root)
    {
        var words = new List<Word>();
        Collect(root, words);
        return words;
    }

    /// <summary>Parses protobuf-JSON durations such as <c>"1.250s"</c>.</summary>
    public static TimeSpan ParseOffset(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return TimeSpan.FromSeconds(value.GetDouble());
        }

        var text = value.GetString() ?? "0s";
        text = text.EndsWith('s') ? text[..^1] : text;
        return TimeSpan.FromSeconds(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    private static void Collect(JsonElement element, List<Word> words)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "word_info")
                {
                    if (TryWord(element) is { } word)
                    {
                        words.Add(word);
                    }

                    return;
                }

                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, words);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, words);
                }

                break;
        }
    }

    private static Word? TryWord(JsonElement e)
    {
        if (!e.TryGetProperty("text", out var text) || text.GetString() is not { Length: > 0 } t)
        {
            return null;
        }

        var start = Find(e, "start_offset", "startOffset") is { } s ? ParseOffset(s) : TimeSpan.Zero;
        var end = Find(e, "end_offset", "endOffset") is { } en ? ParseOffset(en) : start;
        var speaker = Find(e, "speaker", "speaker_label") is { ValueKind: JsonValueKind.String } sp ? sp.GetString() : null;
        double? confidence = Find(e, "confidence") is { ValueKind: JsonValueKind.Number } c ? c.GetDouble() : null;
        return new Word(t, start, end, confidence, speaker);
    }

    private static JsonElement? Find(JsonElement e, params string[] names)
    {
        foreach (var name in names)
        {
            if (e.TryGetProperty(name, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
