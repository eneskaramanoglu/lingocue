using System.Text.RegularExpressions;

namespace LingoCue.Transcription.Whisper;

/// <summary>
/// Names the device whisper.cpp actually used, read from its own log lines while the model loads
/// ("whisper_backend_init_gpu: using Metal backend", "Core ML model loaded", "no GPU found").
/// The loaded runtime alone is not enough: the macOS runtimes fall back from CoreML to Metal and
/// from Metal to the CPU without failing.
/// </summary>
public static partial class WhisperDevice
{
    public static string Describe(string? loadedRuntime, IEnumerable<string> nativeLog)
    {
        ArgumentNullException.ThrowIfNull(nativeLog);
        string? gpu = null;
        var coreMl = false;
        var noGpu = false;
        foreach (var line in nativeLog)
        {
            if (BackendLine().Match(line) is { Success: true } match && !match.Groups[1].Value.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)
                && !match.Groups[1].Value.StartsWith("BLAS", StringComparison.OrdinalIgnoreCase))
            {
                gpu = match.Groups[1].Value.Trim();
            }

            coreMl |= line.Contains("Core ML model loaded", StringComparison.OrdinalIgnoreCase);
            noGpu |= line.Contains("no GPU found", StringComparison.OrdinalIgnoreCase);
        }

        var gpuText = gpu switch
        {
            null => null,
            _ when gpu.StartsWith("CUDA", StringComparison.OrdinalIgnoreCase) => "NVIDIA CUDA (GPU)",
            _ when gpu.StartsWith("Vulkan", StringComparison.OrdinalIgnoreCase) => "Vulkan (GPU)",
            _ when gpu.StartsWith("Metal", StringComparison.OrdinalIgnoreCase) || gpu.StartsWith("MTL", StringComparison.OrdinalIgnoreCase) => "Apple Metal (GPU)",
            _ => $"{gpu} (GPU)",
        };

        if (coreMl)
        {
            return gpuText is null ? "Apple Neural Engine (CoreML)" : $"Apple Neural Engine (CoreML) + {gpuText}";
        }

        if (gpuText is not null)
        {
            return gpuText;
        }

        if (noGpu)
        {
            return "CPU";
        }

        // No log lines (e.g. an older native build): fall back to the runtime that was loaded.
        return loadedRuntime switch
        {
            "Cuda" or "Cuda12" => "NVIDIA CUDA (GPU)",
            "Vulkan" => "Vulkan (GPU)",
            "CoreML" => "Apple CoreML",
            null => "?",
            _ => "CPU",
        };
    }

    public static bool IsGpu(string description) => description.Contains("GPU", StringComparison.Ordinal) || description.Contains("Neural Engine", StringComparison.Ordinal);

    [GeneratedRegex(@"using\s+(\S+)\s+backend", RegexOptions.IgnoreCase)]
    private static partial Regex BackendLine();
}
