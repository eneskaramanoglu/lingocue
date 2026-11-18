using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LingoCue.Core.Subtitles;
using Microsoft.Extensions.Logging;

namespace LingoCue.Transcription.Gemini;

/// <summary>
/// Minimal REST client for Gemini transcription: Files API upload (resumable), Interactions API
/// request with verbatim mode + word timestamps (+ speaker diarization), polling and file cleanup.
/// The API key travels only in the <c>x-goog-api-key</c> header and is never logged.
/// </summary>
public sealed partial class GeminiTranscribeClient(HttpClient http, ILogger<GeminiTranscribeClient> logger)
{
    public static readonly Uri BaseAddress = new("https://generativelanguage.googleapis.com/");
    private static readonly TimeSpan[] s_retryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(30)];
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan s_maxWait = TimeSpan.FromMinutes(20);

    /// <summary>Overridable for tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public async Task<UploadedFile> UploadAsync(string apiKey, string path, string mimeType, CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        using var start = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress, "upload/v1beta/files"))
        {
            Content = JsonContent.Create(new { file = new { display_name = Path.GetFileName(path) } }),
        };
        start.Headers.Add("x-goog-api-key", apiKey);
        start.Headers.Add("X-Goog-Upload-Protocol", "resumable");
        start.Headers.Add("X-Goog-Upload-Command", "start");
        start.Headers.Add("X-Goog-Upload-Header-Content-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType);

        using var startResponse = await SendWithRetryAsync(start, cancellationToken);
        var uploadUrl = startResponse.Headers.TryGetValues("x-goog-upload-url", out var values) ? values.FirstOrDefault() : null;
        if (uploadUrl is null)
        {
            throw new TranscriptionException("Gemini Files API did not return an upload URL.");
        }

        await using var file = File.OpenRead(path);
        using var upload = new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = new StreamContent(file) };
        upload.Content.Headers.ContentLength = length;
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        upload.Headers.Add("X-Goog-Upload-Offset", "0");
        upload.Headers.Add("X-Goog-Upload-Command", "upload, finalize");

        using var response = await SendAsync(upload, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var fileElement = json.RootElement.TryGetProperty("file", out var f) ? f : json.RootElement;
        var uri = fileElement.GetProperty("uri").GetString()!;
        var name = fileElement.GetProperty("name").GetString()!;
        LogUploaded(name, length);
        return new UploadedFile(name, uri, mimeType);
    }

    public async Task<List<Word>> TranscribeAsync(
        string apiKey, string model, UploadedFile file, IReadOnlyList<string>? languageCodes, bool diarize, CancellationToken cancellationToken)
    {
        var body = BuildRequest(model, file, languageCodes, diarize);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress, "v1beta/interactions"))
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await SendWithRetryAsync(request, cancellationToken);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)).RootElement;

        // Long audio may come back still running: poll the interaction until it completes.
        var waited = TimeSpan.Zero;
        while (GeminiTranscriptParser.Status(root) is "in_progress" or "running" or "pending" or "queued")
        {
            if (waited > s_maxWait || GeminiTranscriptParser.InteractionName(root) is not { } name)
            {
                throw new TranscriptionException("Gemini transcription did not finish in time.");
            }

            await Delay(s_pollInterval, cancellationToken);
            waited += s_pollInterval;
            using var poll = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, "v1beta/" + name.TrimStart('/')));
            poll.Headers.Add("x-goog-api-key", apiKey);
            using var pollResponse = await SendWithRetryAsync(poll, cancellationToken);
            root = JsonDocument.Parse(await pollResponse.Content.ReadAsStringAsync(cancellationToken)).RootElement;
        }

        if (GeminiTranscriptParser.Status(root) is "failed" or "cancelled")
        {
            throw new TranscriptionException($"Gemini transcription {GeminiTranscriptParser.Status(root)}.");
        }

        return GeminiTranscriptParser.ParseWords(root);
    }

    public async Task DeleteAsync(string apiKey, UploadedFile file)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(BaseAddress, "v1beta/" + file.Name));
            request.Headers.Add("x-goog-api-key", apiKey);
            using var response = await http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            // Files expire after 48 hours anyway.
            LogDeleteFailed(ex, file.Name);
        }
    }

    internal static JsonObject BuildRequest(string model, UploadedFile file, IReadOnlyList<string>? languageCodes, bool diarize)
    {
        var mode = new JsonObject
        {
            ["type"] = "verbatim",
            ["timestamp_granularities"] = new JsonArray("word"),
        };
        if (diarize)
        {
            mode["diarization_mode"] = "speaker";
        }

        var config = new JsonObject { ["mode"] = mode };
        if (languageCodes is { Count: > 0 })
        {
            config["language_codes"] = new JsonArray([.. languageCodes.Select(c => (JsonNode)c)]);
        }

        return new JsonObject
        {
            ["model"] = model.StartsWith("models/", StringComparison.Ordinal) ? model : "models/" + model,
            ["input"] = new JsonArray(new JsonObject
            {
                ["type"] = "audio",
                ["uri"] = file.Uri,
                ["mime_type"] = file.MimeType,
            }),
            ["generation_config"] = new JsonObject { ["transcription_config"] = config },
        };
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        for (var attempt = 0; ; attempt++)
        {
            using var copy = Clone(request, content);
            var response = await http.SendAsync(copy, cancellationToken);
            var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
            if (!retryable || attempt >= s_retryDelays.Length)
            {
                await EnsureSuccessAsync(response, cancellationToken);
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? s_retryDelays[attempt];
            LogRetrying((int)response.StatusCode, delay);
            response.Dispose();
            await Delay(delay, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original, byte[]? content)
    {
        var copy = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (content is not null)
        {
            copy.Content = new ByteArrayContent(content);
            foreach (var header in original.Content!.Headers)
            {
                copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return copy;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = ExtractError(body) ?? response.ReasonPhrase ?? "error";
        var status = (int)response.StatusCode;
        response.Dispose();
        throw new TranscriptionException($"Gemini API {status}: {message}")
        {
            Remedy = status switch
            {
                400 when message.Contains("API key", StringComparison.OrdinalIgnoreCase) => "Gemini API anahtarı geçersiz. Ayarlar'dan doğru anahtarı girin.",
                401 or 403 => "Gemini API anahtarı geçersiz ya da bu model için yetkisiz. Ayarlar'dan anahtarı kontrol edin.",
                429 => "Gemini kotası doldu. Biraz bekleyip tekrar deneyin veya Google AI Studio'da kotanızı kontrol edin.",
                >= 500 => "Gemini hizmetinde geçici bir sorun var. Birazdan tekrar deneyin.",
                _ => null,
            },
        };
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Uploaded {Name} ({Bytes} bytes) to Gemini Files API")]
    private partial void LogUploaded(string name, long bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini returned {Status}; retrying in {Delay}")]
    private partial void LogRetrying(int status, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete uploaded file {Name}")]
    private partial void LogDeleteFailed(Exception ex, string name);
}

public sealed record UploadedFile(string Name, string Uri, string MimeType);
