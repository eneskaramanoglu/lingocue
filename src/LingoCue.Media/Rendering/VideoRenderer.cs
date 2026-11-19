using System.Globalization;
using LingoCue.Media.Audio;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;

namespace LingoCue.Media.Rendering;

/// <param name="Fraction">0..1 of the video duration.</param>
/// <param name="Speed">Encoding speed relative to real time (2.0 = twice as fast as playback); null until known.</param>
/// <param name="Remaining">Estimated time left; null until the speed is known.</param>
public sealed record RenderProgress(double Fraction, double? Speed, TimeSpan? Remaining);

public sealed class RenderException : Exception
{
    public RenderException()
    {
    }

    public RenderException(string message)
        : base(message)
    {
    }

    public RenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Reads FFmpeg's <c>-progress pipe:1</c> stream (<c>out_time_us</c>, <c>speed</c>, <c>progress</c>).</summary>
public sealed class RenderProgressParser(TimeSpan duration, IProgress<RenderProgress>? sink)
{
    private TimeSpan _position;
    private double? _speed;

    public void OnLine(string line)
    {
        if (sink is null || string.IsNullOrEmpty(line))
        {
            return;
        }

        if (FfmpegProgress.TryReadPosition(line) is { } position)
        {
            _position = position;
        }
        else if (line.StartsWith("speed=", StringComparison.Ordinal))
        {
            var value = line.AsSpan(6).TrimEnd('x').Trim();
            _speed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0 ? s : _speed;
        }
        else if (line == "progress=end")
        {
            sink.Report(new RenderProgress(1, _speed, TimeSpan.Zero));
        }
        else if (line == "progress=continue")
        {
            // One block of key=value lines ends here: report once per block.
            var fraction = duration > TimeSpan.Zero ? Math.Clamp(_position / duration, 0, 1) : 0;
            TimeSpan? remaining = _speed is { } speed && duration > TimeSpan.Zero
                ? TimeSpan.FromSeconds(Math.Max(0, (duration - _position).TotalSeconds / speed))
                : null;
            sink.Report(new RenderProgress(fraction, _speed, remaining));
        }
    }
}

public interface IVideoRenderer
{
    /// <summary>
    /// Runs an export command. FFmpeg writes to a temporary file next to the target, which replaces
    /// the target only on success; a failed or cancelled run leaves no partial file behind.
    /// </summary>
    Task RenderAsync(FfmpegCommand command, TimeSpan duration, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Hardware encoders that actually work on this machine (a one-frame test encode each).</summary>
    Task<IReadOnlyList<VideoEncoder>> AvailableHardwareEncodersAsync(CancellationToken cancellationToken = default);
}

public sealed class VideoRenderer(IMediaToolLocator locator, IProcessRunner runner) : IVideoRenderer
{
    private readonly Lock _probeGate = new();
    private Task<IReadOnlyList<VideoEncoder>>? _hardware;

    public async Task RenderAsync(FfmpegCommand command, TimeSpan duration, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tools = locator.Locate();
        var target = Path.GetFullPath(command.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var input = command.Arguments.SkipWhile(a => a != "-i").Skip(1).FirstOrDefault();
        if (input is not null && string.Equals(Path.GetFullPath(input), target, StringComparison.OrdinalIgnoreCase))
        {
            throw new RenderException("The output file cannot be the source video itself.");
        }

        // The output path is always the last argument; FFmpeg writes a temporary file first.
        var partial = PartialPath(target);
        var args = command.Arguments.Take(command.Arguments.Count - 1).Append(partial).ToList();

        var parser = new RenderProgressParser(duration, progress);
        try
        {
            var result = await runner.RunAsync(tools.FfmpegPath, args, parser.OnLine, command.WorkingDirectory, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new RenderException($"FFmpeg stopped with code {result.ExitCode}: {LastLines(result.StandardError)}");
            }

            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            TryDelete(partial);
        }

        progress?.Report(new RenderProgress(1, null, TimeSpan.Zero));
    }

    public Task<IReadOnlyList<VideoEncoder>> AvailableHardwareEncodersAsync(CancellationToken cancellationToken = default)
    {
        // Probed once per app run; callers that cancel just stop waiting.
        lock (_probeGate)
        {
            _hardware ??= ProbeHardwareAsync();
        }

        return _hardware.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<VideoEncoder>> ProbeHardwareAsync()
    {
        var tools = locator.Locate();
        var found = new List<VideoEncoder>();
        foreach (var encoder in new[] { VideoEncoder.Nvenc, VideoEncoder.VideoToolbox })
        {
            // Listed encoders are not enough (NVENC is compiled in even without an NVIDIA GPU):
            // encode one small frame with the same settings the export uses.
            string[] args =
            [
                "-nostdin", "-hide_banner", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=320x240:d=0.1",
                "-frames:v", "1", .. FfmpegCommands.VideoEncoderArguments(encoder, 19), "-f", "null", "-",
            ];
            var result = await runner.RunAsync(tools.FfmpegPath, args);
            if (result.ExitCode == 0)
            {
                found.Add(encoder);
            }
        }

        return found;
    }

    /// <summary><c>film.mp4</c> → <c>film.lingocue-partial.mp4</c> (same folder, so the final move is a rename).</summary>
    internal static string PartialPath(string target)
        => Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + ".lingocue-partial" + Path.GetExtension(target));

    private static string LastLines(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "(no error output)" : string.Join(" | ", lines.TakeLast(3));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Still open by a dying process; it is a .lingocue-partial file and harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
