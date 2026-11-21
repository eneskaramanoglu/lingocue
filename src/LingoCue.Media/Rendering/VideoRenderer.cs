using System.Globalization;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// True when this FFmpeg has the libass <c>ass</c> filter that burn-in needs. Some builds lack it,
    /// notably Homebrew's plain <c>ffmpeg</c> (its <c>ffmpeg-full</c> has it).
    /// </summary>
    Task<bool> SupportsBurnInAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether each hardware encoder works on this machine (a one-frame test encode each).</summary>
    Task<IReadOnlyList<HardwareEncoderStatus>> HardwareEncodersAsync(CancellationToken cancellationToken = default);
}

/// <param name="Present">The encoder exists in this FFmpeg build and on this kind of machine.</param>
/// <param name="Problem">Why a present encoder cannot be used, in Turkish for the UI (e.g. an old driver).</param>
public sealed record HardwareEncoderStatus(VideoEncoder Encoder, bool Available, bool Present, string? Problem)
{
    public static HardwareEncoderStatus FromProbe(VideoEncoder encoder, int exitCode, string stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        if (exitCode == 0)
        {
            return new(encoder, true, true, null);
        }

        // Not compiled into this FFmpeg (e.g. VideoToolbox on Windows): simply not offered.
        if (stderr.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
        {
            return new(encoder, false, false, null);
        }

        // AMF and Quick Sync are compiled into Windows builds but need an AMD or Intel GPU; any
        // failure there means "not this machine" (an Intel/AMD GPU that works opens them fine).
        if (encoder is VideoEncoder.Amf or VideoEncoder.Qsv)
        {
            return new(encoder, false, false, null);
        }

        var driver = Regex.Match(stderr, @"minimum required Nvidia driver for nvenc is (?<v>[\d.]+)", RegexOptions.IgnoreCase);
        if (driver.Success)
        {
            return new(encoder, false, true, $"NVIDIA sürücüsü eski: NVENC için en az {driver.Groups["v"].Value} sürümü gerekiyor. Sürücüyü güncelleyin (nvidia.com/drivers veya GeForce Experience).");
        }

        // No NVIDIA GPU / no capable device: NVENC is compiled into most Windows builds anyway.
        return stderr.Contains("No capable devices found", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("Cannot load nvcuda", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("cuInit", StringComparison.OrdinalIgnoreCase)
            ? new(encoder, false, false, null)
            : new(encoder, false, true, "Donanım kodlayıcı açılamadı; CPU (x264) kullanılacak.");
    }
}

public sealed class VideoRenderer(IMediaToolLocator locator, IProcessRunner runner) : IVideoRenderer
{
    private readonly Lock _probeGate = new();
    private Task<IReadOnlyList<HardwareEncoderStatus>>? _hardware;
    private Task<bool>? _burnIn;

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

    public Task<bool> SupportsBurnInAsync(CancellationToken cancellationToken = default)
    {
        lock (_probeGate)
        {
            _burnIn ??= ProbeBurnInAsync();
        }

        return _burnIn.WaitAsync(cancellationToken);
    }

    private async Task<bool> ProbeBurnInAsync()
    {
        var result = await runner.RunAsync(locator.Locate().FfmpegPath, ["-hide_banner", "-filters"]);
        return result.ExitCode == 0 && HasAssFilter(result.StandardOutput);
    }

    /// <summary>Finds the <c>ass</c> line in <c>ffmpeg -filters</c> output (" .. ass   V->V   Render ASS …").</summary>
    internal static bool HasAssFilter(string filters)
        => Regex.IsMatch(filters, @"^\s*\S+\s+ass\s+V->V", RegexOptions.Multiline);

    public Task<IReadOnlyList<HardwareEncoderStatus>> HardwareEncodersAsync(CancellationToken cancellationToken = default)
    {
        // Probed once per app run; callers that cancel just stop waiting.
        lock (_probeGate)
        {
            _hardware ??= ProbeHardwareAsync();
        }

        return _hardware.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<HardwareEncoderStatus>> ProbeHardwareAsync()
    {
        var tools = locator.Locate();
        var statuses = new List<HardwareEncoderStatus>();
        foreach (var encoder in new[] { VideoEncoder.Nvenc, VideoEncoder.Amf, VideoEncoder.Qsv, VideoEncoder.VideoToolbox })
        {
            // Listed encoders are not enough (NVENC is compiled in even without an NVIDIA GPU):
            // encode one small frame with the same settings the export uses.
            string[] args =
            [
                "-nostdin", "-hide_banner", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=320x240:d=0.1",
                "-frames:v", "1", .. FfmpegCommands.VideoEncoderArguments(encoder, 19), "-f", "null", "-",
            ];
            var result = await runner.RunAsync(tools.FfmpegPath, args);
            statuses.Add(HardwareEncoderStatus.FromProbe(encoder, result.ExitCode, result.StandardError));
        }

        return statuses;
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
        // A killed FFmpeg may hold the file for a moment (antivirus scanners too): retry briefly.
        for (var attempt = 0; attempt < 10 && File.Exists(path); attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
