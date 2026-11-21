using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using LingoCue.Core.Platform;
using Microsoft.Win32;

namespace LingoCue.Infrastructure.Platform;

/// <summary>
/// Windows: the display adapters' <c>HardwareInformation.qwMemorySize</c> registry values (WMI's
/// <c>AdapterRAM</c> is 32-bit and stops at 4 GB). macOS: <c>sysctl</c> for the chip name and memory.
/// Read once and cached; never throws.
/// </summary>
public sealed class HardwareInfoProvider : IHardwareInfoProvider
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private readonly Lazy<HardwareInfo> _info = new(ReadNow);

    public HardwareInfo Read() => _info.Value;

    private static HardwareInfo ReadNow()
    {
        var systemMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var (name, memory) = ReadWindowsAdapter();
                return new HardwareInfo { GpuName = name, GpuMemoryBytes = memory, SystemMemoryBytes = systemMemory };
            }

            if (OperatingSystem.IsMacOS())
            {
                var memory = SysctlLong("hw.memsize") ?? systemMemory;
                var chip = SysctlString("machdep.cpu.brand_string");
                var appleSilicon = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
                return new HardwareInfo { GpuName = chip, SystemMemoryBytes = memory, UnifiedMemory = appleSilicon };
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Unknown hardware only means no suggestion.
        }

        return new HardwareInfo { SystemMemoryBytes = systemMemory };
    }

    /// <summary>The adapter with the most dedicated memory (a laptop's NVIDIA card rather than its Intel one).</summary>
    [SupportedOSPlatform("windows")]
    private static (string? Name, long Memory) ReadWindowsAdapter()
    {
        using var root = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (root is null)
        {
            return (null, 0);
        }

        string? bestName = null;
        long bestMemory = 0;
        foreach (var subKeyName in root.GetSubKeyNames())
        {
            if (!subKeyName.All(char.IsAsciiDigit))
            {
                continue; // "Properties" and similar
            }

            try
            {
                using var adapter = root.OpenSubKey(subKeyName);
                var memory = AdapterMemory(adapter?.GetValue("HardwareInformation.qwMemorySize"))
                             ?? AdapterMemory(adapter?.GetValue("HardwareInformation.MemorySize"))
                             ?? 0;
                if (memory > bestMemory)
                {
                    bestMemory = memory;
                    bestName = adapter?.GetValue("DriverDesc") as string;
                }
            }
            catch (SecurityException)
            {
            }
        }

        return (bestName?.Trim(), bestMemory);
    }

    /// <summary>Drivers store the size as a QWORD, a DWORD or raw little-endian bytes.</summary>
    internal static long? AdapterMemory(object? value) => value switch
    {
        long l => l,
        int i => (uint)i,
        byte[] { Length: >= 8 } b => BitConverter.ToInt64(b, 0),
        byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b, 0),
        _ => null,
    };

    [SupportedOSPlatform("macos")]
    private static long? SysctlLong(string name)
    {
        var size = (nint)sizeof(long);
        var buffer = new byte[sizeof(long)];
        return sysctlbyname(name, buffer, ref size, IntPtr.Zero, 0) == 0 ? BitConverter.ToInt64(buffer, 0) : null;
    }

    [SupportedOSPlatform("macos")]
    private static string? SysctlString(string name)
    {
        nint size = 0;
        if (sysctlbyname(name, null, ref size, IntPtr.Zero, 0) != 0 || size <= 0)
        {
            return null;
        }

        var buffer = new byte[size];
        return sysctlbyname(name, buffer, ref size, IntPtr.Zero, 0) == 0
            ? Encoding.UTF8.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim()
            : null;
    }

    [DllImport("libc", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int sysctlbyname(string name, byte[]? oldValue, ref nint oldLength, IntPtr newValue, nint newLength);
}
