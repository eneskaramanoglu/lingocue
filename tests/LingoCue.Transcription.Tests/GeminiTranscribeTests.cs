using System.Net;
using System.Text;
using System.Text.Json;
using LingoCue.Transcription.Gemini;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Transcription.Tests;

public sealed class GeminiTranscribeTests : IDisposable
{
    private readonly string _audio = Path.Combine(Path.GetTempPath(), $"lingocue-gemini-{Guid.NewGuid():N}.flac");

    public GeminiTranscribeTests() => File.WriteAllBytes(_audio, [1, 2, 3, 4]);

    public void Dispose() => File.Delete(_audio);

    private const string StepsResponse = """
        {
          "id": "interactions/abc123xyz",
          "status": "completed",
          "steps": [ { "id": "step_001", "type": "model_output", "content": [ {
            "type": "text", "text": "Merhaba dünya",
            "annotations": [
              { "type": "word_info", "text": "Merhaba", "speaker": "spk_1", "start_offset": "0.100s", "end_offset": "0.400s" },
              { "type": "word_info", "text": "dünya", "speaker": "spk_2", "start_offset": "0.500s", "end_offset": "0.800s" }
            ] } ] } ]
        }
        """;

    private const string OutputsResponse = """
        { "status": "completed", "outputs": [ { "type": "text", "text": "Evet",
          "annotations": [ { "type": "word_info", "text": "Evet", "startOffset": "1.5s", "endOffset": "2s" } ] } ] }
        """;

    private static (GeminiTranscribeClient Client, FakeGemini Server) Create(params HttpResponseMessage[] interactionResponses)
    {
        var server = new FakeGemini(interactionResponses);
        var client = new GeminiTranscribeClient(new HttpClient(server), NullLogger<GeminiTranscribeClient>.Instance)
        {
            Delay = (_, _) => Task.CompletedTask,
        };
        return (client, server);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Uploads_then_requests_verbatim_word_timestamps_in_turkish()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, server) = Create(Json(StepsResponse));

        var file = await client.UploadAsync("KEY", _audio, "audio/flac", ct);
        var words = await client.TranscribeAsync("KEY", GeminiTranscribeEngine.ModelId, file, ["tr-TR"], diarize: true, ct);

        file.Uri.ShouldBe("https://files.test/abc");
        server.UploadCommand.ShouldBe("upload, finalize");
        server.AllRequestsHadKey.ShouldBeTrue();

        using var body = JsonDocument.Parse(server.InteractionBody!);
        var root = body.RootElement;
        root.GetProperty("model").GetString().ShouldBe("models/gemini-3.5-transcribe");
        root.GetProperty("input")[0].GetProperty("uri").GetString().ShouldBe("https://files.test/abc");
        var config = root.GetProperty("generation_config").GetProperty("transcription_config");
        config.GetProperty("language_codes")[0].GetString().ShouldBe("tr-TR");
        var mode = config.GetProperty("mode");
        mode.GetProperty("type").GetString().ShouldBe("verbatim");
        mode.GetProperty("timestamp_granularities")[0].GetString().ShouldBe("word");
        mode.GetProperty("diarization_mode").GetString().ShouldBe("speaker");

        words.Count.ShouldBe(2);
        words[0].Text.ShouldBe("Merhaba");
        words[0].Start.ShouldBe(TimeSpan.FromMilliseconds(100));
        words[1].End.ShouldBe(TimeSpan.FromMilliseconds(800));
        words[1].Speaker.ShouldBe("spk_2");
    }

    [Fact]
    public async Task Reads_the_alternative_outputs_shape()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = Create(Json(OutputsResponse));
        var file = await client.UploadAsync("KEY", _audio, "audio/flac", ct);

        var word = (await client.TranscribeAsync("KEY", "gemini-3.5-transcribe", file, null, false, ct)).ShouldHaveSingleItem();

        word.Start.ShouldBe(TimeSpan.FromSeconds(1.5));
        word.End.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Polls_a_running_interaction_and_retries_rate_limits()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, server) = Create(
            Json("""{ "error": { "message": "quota" } }""", HttpStatusCode.TooManyRequests),
            Json("""{ "id": "interactions/x1", "status": "in_progress" }"""),
            Json(StepsResponse));
        var file = await client.UploadAsync("KEY", _audio, "audio/flac", ct);

        var words = await client.TranscribeAsync("KEY", "gemini-3.5-transcribe", file, null, false, ct);

        words.Count.ShouldBe(2);
        server.InteractionCalls.ShouldBe(3);
        server.LastInteractionPath.ShouldBe("/v1beta/interactions/x1");
    }

    [Fact]
    public async Task Invalid_key_gives_a_turkish_remedy()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = Create(Json("""{ "error": { "message": "API key not valid" } }""", HttpStatusCode.Forbidden));
        var file = await client.UploadAsync("KEY", _audio, "audio/flac", ct);

        var ex = await Should.ThrowAsync<TranscriptionException>(() => client.TranscribeAsync("KEY", "m", file, null, false, ct));

        ex.Message.ShouldContain("403");
        ex.Remedy!.ShouldContain("anahtar");
    }

    [Theory]
    [InlineData("tr", "tr-TR")]
    [InlineData("tr-TR", "tr-TR")]
    [InlineData("en", "en-US")]
    public void Maps_languages_to_bcp47_with_region(string input, string expected)
        => GeminiTranscribeEngine.ToGeminiLanguages(input)!.ShouldBe([expected]);

    [Fact]
    public void Auto_language_is_omitted_and_cost_is_estimated()
    {
        GeminiTranscribeEngine.ToGeminiLanguages(null).ShouldBeNull();
        GeminiTranscribeEngine.ToGeminiLanguages("auto").ShouldBeNull();
        GeminiTranscribeEngine.EstimateCost(TimeSpan.FromMinutes(10)).ShouldBe(0.05m);
    }

    private sealed class FakeGemini(HttpResponseMessage[] interactionResponses) : HttpMessageHandler
    {
        private int _next;

        public string? UploadCommand { get; private set; }

        public string? InteractionBody { get; private set; }

        public int InteractionCalls { get; private set; }

        public string? LastInteractionPath { get; private set; }

        public bool AllRequestsHadKey { get; private set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/upload/v1beta/files")
            {
                AllRequestsHadKey &= request.Headers.TryGetValues("x-goog-api-key", out var k) && k.Single() == "KEY";
                var start = new HttpResponseMessage(HttpStatusCode.OK);
                start.Headers.Add("x-goog-upload-url", "https://upload.test/session/1");
                return start;
            }

            if (request.RequestUri.Host == "upload.test")
            {
                UploadCommand = request.Headers.GetValues("X-Goog-Upload-Command").Single();
                return Json("""{ "file": { "name": "files/abc", "uri": "https://files.test/abc", "mimeType": "audio/flac" } }""");
            }

            AllRequestsHadKey &= request.Headers.TryGetValues("x-goog-api-key", out var key) && key.Single() == "KEY";
            InteractionCalls++;
            LastInteractionPath = path;
            if (request.Method == HttpMethod.Post)
            {
                InteractionBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            }

            return interactionResponses[Math.Min(_next++, interactionResponses.Length - 1)];
        }
    }
}
