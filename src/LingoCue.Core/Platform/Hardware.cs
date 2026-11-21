using System.Globalization;

namespace LingoCue.Core.Platform;

/// <summary>What the machine offers for local models.</summary>
public sealed record HardwareInfo
{
    /// <summary>Graphics adapter with the most memory (on Apple Silicon: the chip, e.g. "Apple M3 Pro").</summary>
    public string? GpuName { get; init; }

    /// <summary>Dedicated video memory; 0 when unknown or none. Not used with <see cref="UnifiedMemory"/>.</summary>
    public long GpuMemoryBytes { get; init; }

    public long SystemMemoryBytes { get; init; }

    /// <summary>Apple Silicon: CPU and GPU share the system memory.</summary>
    public bool UnifiedMemory { get; init; }

    public static HardwareInfo Unknown { get; } = new();
}

/// <summary>Reads <see cref="HardwareInfo"/> (implemented per OS in Infrastructure).</summary>
public interface IHardwareInfoProvider
{
    HardwareInfo Read();
}

/// <summary>Whisper model sizes, from fastest to most accurate. The app maps them to its model catalogue.</summary>
public enum WhisperSize
{
    /// <summary>large-v3-turbo, quantized: for the CPU.</summary>
    Turbo,

    /// <summary>large-v3, 5-bit: fits 4 GB GPUs.</summary>
    Large,

    /// <summary>large-v3, full precision (3.1 GB download): needs ~6 GB of GPU memory.</summary>
    LargeFull,
}

public enum HardwareTier
{
    /// <summary>No usable GPU: the CPU does the work.</summary>
    Cpu,

    /// <summary>About 4 GB of GPU memory.</summary>
    Small,

    /// <summary>About 8 GB.</summary>
    Medium,

    /// <summary>About 12 GB.</summary>
    Large,

    /// <summary>16 GB or more.</summary>
    ExtraLarge,
}

/// <summary>Settings suggested for this machine.</summary>
/// <param name="UsableGpuMemoryGb">Memory local models can use: dedicated video memory, or about two thirds of unified memory.</param>
/// <param name="LocalModelBillions">Largest local LLM size (billions of parameters, 4-bit) that fits next to its context.</param>
public sealed record HardwareAdvice(
    HardwareTier Tier,
    double UsableGpuMemoryGb,
    WhisperSize Whisper,
    bool BeamSearch,
    int LocalModelBillions,
    int LocalBatchSize,
    int LocalParallelism)
{
    /// <summary>Approximate download size of a 4-bit local model that fits, in GB.</summary>
    public double LocalModelDownloadGb => Math.Round(LocalModelBillions * 0.65, 1);
}

/// <summary>Turns the hardware into suggested model sizes and run settings.</summary>
public static class HardwareAdvisor
{
    /// <summary>Metal lets the GPU use roughly two thirds of unified memory; the rest stays with the system.</summary>
    public const double UnifiedMemoryGpuShare = 0.66;

    private const double Gb = 1024d * 1024 * 1024;

    public static double UsableGpuMemoryGb(HardwareInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var bytes = info.UnifiedMemory ? info.SystemMemoryBytes * UnifiedMemoryGpuShare : info.GpuMemoryBytes;
        return Math.Round(bytes / Gb, 1);
    }

    public static HardwareAdvice Advise(HardwareInfo info)
    {
        var gb = UsableGpuMemoryGb(info);
        return gb switch
        {
            < 3.5 => new(HardwareTier.Cpu, gb, WhisperSize.Turbo, BeamSearch: false, LocalModelBillions: 4, LocalBatchSize: 10, LocalParallelism: 1),
            < 6 => new(HardwareTier.Small, gb, WhisperSize.Large, BeamSearch: true, LocalModelBillions: 4, LocalBatchSize: 10, LocalParallelism: 1),
            < 10 => new(HardwareTier.Medium, gb, WhisperSize.LargeFull, BeamSearch: true, LocalModelBillions: 8, LocalBatchSize: 15, LocalParallelism: 1),
            < 14 => new(HardwareTier.Large, gb, WhisperSize.LargeFull, BeamSearch: true, LocalModelBillions: 12, LocalBatchSize: 20, LocalParallelism: 1),
            < 22 => new(HardwareTier.ExtraLarge, gb, WhisperSize.LargeFull, BeamSearch: true, LocalModelBillions: 20, LocalBatchSize: 25, LocalParallelism: 2),
            _ => new(HardwareTier.ExtraLarge, gb, WhisperSize.LargeFull, BeamSearch: true, LocalModelBillions: 30, LocalBatchSize: 25, LocalParallelism: 2),
        };
    }

    /// <summary>"NVIDIA GeForce RTX 3050 Laptop GPU · 4 GB" or "Apple M3 Pro · 18 GB ortak bellek".</summary>
    public static string Describe(HardwareInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        if (info.UnifiedMemory)
        {
            return string.Create(culture, $"{info.GpuName ?? "Apple Silicon"} · {Math.Round(info.SystemMemoryBytes / Gb)} GB ortak bellek");
        }

        return info.GpuMemoryBytes > 0
            ? string.Create(culture, $"{info.GpuName ?? "Ekran kartı"} · {Math.Round(info.GpuMemoryBytes / Gb, 1)} GB")
            : "Ayrı ekran kartı bulunamadı (işlemci kullanılır)";
    }
}
