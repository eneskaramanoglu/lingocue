using LingoCue.Core.Platform;
using LingoCue.Infrastructure.Platform;

namespace LingoCue.Core.Tests.Platform;

public class HardwareAdvisorTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static HardwareInfo Gpu(double gb) => new() { GpuName = "Test GPU", GpuMemoryBytes = (long)(gb * Gb), SystemMemoryBytes = 16 * Gb };

    private static HardwareInfo Mac(int gb) => new() { GpuName = "Apple M3", SystemMemoryBytes = gb * Gb, UnifiedMemory = true };

    [Theory]
    [InlineData(0, HardwareTier.Cpu, WhisperSize.Turbo, 4)]
    [InlineData(4, HardwareTier.Small, WhisperSize.Large, 4)]
    [InlineData(8, HardwareTier.Medium, WhisperSize.LargeFull, 8)]
    [InlineData(12, HardwareTier.Large, WhisperSize.LargeFull, 12)]
    [InlineData(16, HardwareTier.ExtraLarge, WhisperSize.LargeFull, 20)]
    [InlineData(24, HardwareTier.ExtraLarge, WhisperSize.LargeFull, 30)]
    public void Graphics_cards_get_models_that_fit(double gb, HardwareTier tier, WhisperSize whisper, int billions)
    {
        var advice = HardwareAdvisor.Advise(Gpu(gb));

        (advice.Tier, advice.Whisper, advice.LocalModelBillions).ShouldBe((tier, whisper, billions));
        advice.BeamSearch.ShouldBe(tier != HardwareTier.Cpu); // beam search is too slow on the CPU
    }

    [Theory]
    [InlineData(8, HardwareTier.Small)]       // 5.3 GB for the GPU
    [InlineData(16, HardwareTier.Large)]      // 10.6 GB
    [InlineData(24, HardwareTier.ExtraLarge)] // 15.8 GB
    public void Apple_silicon_shares_about_two_thirds_of_its_memory(int gb, HardwareTier tier)
        => HardwareAdvisor.Advise(Mac(gb)).Tier.ShouldBe(tier);

    [Fact]
    public void Describes_the_card_in_turkish()
    {
        HardwareAdvisor.Describe(Gpu(4)).ShouldBe("Test GPU · 4 GB");
        HardwareAdvisor.Describe(Mac(16)).ShouldBe("Apple M3 · 16 GB ortak bellek");
        HardwareAdvisor.Describe(new HardwareInfo { SystemMemoryBytes = 8 * Gb }).ShouldContain("işlemci");
        HardwareAdvisor.Advise(Gpu(8)).LocalModelDownloadGb.ShouldBe(5.2);
    }

    [Fact]
    public void Reads_adapter_memory_in_every_registry_shape()
    {
        HardwareInfoProvider.AdapterMemory(4L * Gb).ShouldBe(4L * Gb);
        HardwareInfoProvider.AdapterMemory(unchecked((int)(3L * Gb))).ShouldBe(3L * Gb); // DWORD above 2 GB
        HardwareInfoProvider.AdapterMemory(BitConverter.GetBytes(12L * Gb)).ShouldBe(12L * Gb);
        HardwareInfoProvider.AdapterMemory(BitConverter.GetBytes((uint)(2L * Gb))).ShouldBe(2L * Gb);
        HardwareInfoProvider.AdapterMemory("x").ShouldBeNull();
    }

    [Fact]
    public void Reads_this_machine_without_throwing()
    {
        var info = new HardwareInfoProvider().Read();

        info.SystemMemoryBytes.ShouldBeGreaterThan(0);
        TestContext.Current.TestOutputHelper?.WriteLine(HardwareAdvisor.Describe(info));
    }
}
