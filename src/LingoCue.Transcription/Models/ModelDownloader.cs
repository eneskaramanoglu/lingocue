using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace LingoCue.Transcription.Models;

/// <summary>A downloadable model file pinned to an exact URL, size and SHA256.</summary>
public sealed record ModelFile(string FileName, Uri Url, long SizeBytes, string Sha256);

public sealed record DownloadProgress(long BytesDone, long BytesTotal)
{
    public double Fraction => BytesTotal > 0 ? (double)BytesDone / BytesTotal : 0;
}

/// <summary>
/// Downloads model files into a cache folder with resume (<c>.part</c> + HTTP Range), verifies the
/// SHA256 before making the file visible, and never re-downloads a verified file.
/// </summary>
public sealed partial class ModelDownloader(HttpClient http, ILogger<ModelDownloader> logger)
{
    private const int BufferSize = 1 << 20;

    public static string PathFor(string directory, ModelFile model) => Path.Combine(directory, model.FileName);

    public static bool IsDownloaded(string directory, ModelFile model)
    {
        var path = PathFor(directory, model);
        return File.Exists(path) && new FileInfo(path).Length == model.SizeBytes;
    }

    public async Task<string> EnsureAsync(
        string directory, ModelFile model, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        Directory.CreateDirectory(directory);
        var target = PathFor(directory, model);
        if (IsDownloaded(directory, model))
        {
            return target;
        }

        var part = target + ".part";
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > model.SizeBytes)
        {
            File.Delete(part);
            existing = 0;
        }

        LogDownloading(model.FileName, model.Url, existing);
        using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            // Server ignored the range: start over.
            existing = 0;
        }

        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            var buffer = new byte[BufferSize];
            var done = existing;
            var lastReport = 0L;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                if (done - lastReport >= 4 * BufferSize || done == model.SizeBytes)
                {
                    progress?.Report(new DownloadProgress(done, model.SizeBytes));
                    lastReport = done;
                }
            }
        }

        var actual = await HashAsync(part, cancellationToken);
        if (!string.Equals(actual, model.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new TranscriptionException($"Downloaded {model.FileName} is corrupt (SHA256 {actual}, expected {model.Sha256}).")
            {
                Remedy = "İndirme bozuk geldi; tekrar deneyin. Sorun sürerse internet bağlantınızı veya proxy ayarlarınızı kontrol edin.",
            };
        }

        File.Move(part, target, overwrite: true);
        progress?.Report(new DownloadProgress(model.SizeBytes, model.SizeBytes));
        LogDownloaded(model.FileName);
        return target;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading {File} from {Url} (resuming at {Offset} bytes)")]
    private partial void LogDownloading(string file, Uri url, long offset);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded and verified {File}")]
    private partial void LogDownloaded(string file);
}
