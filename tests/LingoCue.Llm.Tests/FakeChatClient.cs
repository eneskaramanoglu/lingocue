using System.Text.Json;
using Microsoft.Extensions.AI;

namespace LingoCue.Llm.Tests;

/// <summary>Scripted <see cref="IChatClient"/>: each call is answered by the handler; calls are recorded.</summary>
internal sealed class FakeChatClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, int, ChatResponse> handler) : IChatClient
{
    private int _calls;

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public int CallCount => _calls;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = messages.ToList();
        int index;
        lock (Calls)
        {
            Calls.Add((list, options));
            index = _calls++;
        }

        return Task.FromResult(handler(list, options, index));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static ChatResponse Json(string json, long input = 100, long output = 50)
        => new(new ChatMessage(ChatRole.Assistant, json)) { Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output } };

    /// <summary>Echoes the requested lines through <paramref name="transform"/> (reads the last user message).</summary>
    public static ChatResponse Echo(IReadOnlyList<ChatMessage> messages, Func<int, string, string> transform)
    {
        var lines = RequestedLines(messages);
        var items = lines.Select(l => new { id = l.Id, text = transform(l.Id, l.Text) });
        return Json(JsonSerializer.Serialize(new { lines = items }));
    }

    /// <summary>The lines in the "Lines to process" / "Draft results" block of the first user message.</summary>
    public static List<(int Id, string Text)> RequestedLines(IReadOnlyList<ChatMessage> messages)
    {
        var prompt = messages.First(m => m.Role == ChatRole.User).Text;
        var marker = prompt.Contains("Draft results", StringComparison.Ordinal) ? "Draft results" : "Lines to process";
        // The JSON starts on the line after the marker (the marker line itself shows an example shape).
        var start = prompt.IndexOf('{', prompt.IndexOf('\n', prompt.IndexOf(marker, StringComparison.Ordinal)));
        var end = prompt.IndexOf("\n\n", start, StringComparison.Ordinal);
        var json = end < 0 ? prompt[start..] : prompt[start..end];
        using var doc = JsonDocument.Parse(json);
        return [.. doc.RootElement.GetProperty("lines").EnumerateArray().Select(e => (e.GetProperty("id").GetInt32(), e.GetProperty("text").GetString()!))];
    }
}
