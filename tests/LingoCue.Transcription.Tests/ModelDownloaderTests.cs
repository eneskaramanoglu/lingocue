using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using LingoCue.Transcription.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.Transcription.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private static readonly byte[] s_payload = [.. Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251))];
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-models-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ModelFile Model(string? sha = null) => new(
        "model.bin", new Uri("https://example.test/model.bin"), s_payload.Length, sha ?? Convert.ToHexStringLower(SHA256.HashData(s_payload)));

    [Fact]
    public async Task Downloads_verifies_and_skips_when_present()
    {
        var handler = new FakeServer(s_payload);
        var downloader = new ModelDownloader(new HttpClient(handler), NullLogger<ModelDownloader>.Instance);
        var reports = new List<DownloadProgress>();

        var path = await downloader.EnsureAsync(_dir, Model(), new SyncProgress(reports), TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).ShouldBe(s_payload);
        reports[^1].Fraction.ShouldBe(1);
        ModelDownloader.IsDownloaded(_dir, Model()).ShouldBeTrue();

        await downloader.EnsureAsync(_dir, Model(), cancellationToken: TestContext.Current.CancellationToken);
        handler.Requests.ShouldBe(1); // second call did not download
    }

    [Fact]
    public async Task Resumes_a_partial_download_with_a_range_request()
    {
        await File.WriteAllBytesAsync(Path.Combine(_dir, "model.bin.part"), s_payload[..100_000], TestContext.Current.CancellationToken);
        var handler = new FakeServer(s_payload);
        var downloader = new ModelDownloader(new HttpClient(handler), NullLogger<ModelDownloader>.Instance);

        var path = await downloader.EnsureAsync(_dir, Model(), cancellationToken: TestContext.Current.CancellationToken);

        handler.LastRangeFrom.ShouldBe(100_000);
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).ShouldBe(s_payload);
    }

    [Fact]
    public async Task Rejects_a_corrupt_file_and_leaves_nothing_behind()
    {
        var downloader = new ModelDownloader(new HttpClient(new FakeServer(s_payload)), NullLogger<ModelDownloader>.Instance);

        var ex = await Should.ThrowAsync<TranscriptionException>(
            () => downloader.EnsureAsync(_dir, Model(new string('0', 64)), cancellationToken: TestContext.Current.CancellationToken));

        ex.Remedy.ShouldNotBeNullOrEmpty();
        Directory.GetFiles(_dir).ShouldBeEmpty();
    }

    private sealed class FakeServer(byte[] content) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public long? LastRangeFrom { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var from = request.Headers.Range?.Ranges.First().From;
            LastRangeFrom = from;
            var body = from is { } f ? content[(int)f..] : content;
            var response = new HttpResponseMessage(from is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(body),
            };
            if (from is { } start)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, content.Length - 1, content.Length);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class SyncProgress(List<DownloadProgress> values) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => values.Add(value);
    }
}
