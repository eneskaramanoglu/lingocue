using LingoCue.Transcription.Whisper;

namespace LingoCue.Transcription.Tests;

public class WhisperDeviceTests
{
    [Theory]
    [InlineData("Vulkan", "whisper_backend_init_gpu: using Vulkan0 backend", "Vulkan (GPU)")]
    [InlineData("Cuda", "whisper_backend_init_gpu: using CUDA0 backend", "NVIDIA CUDA (GPU)")]
    [InlineData("Cpu", "whisper_backend_init_gpu: using Metal backend", "Apple Metal (GPU)")]
    [InlineData("CoreML", "whisper_backend_init_gpu: using MTL0 backend", "Apple Metal (GPU)")]
    [InlineData("Cpu", "whisper_backend_init_gpu: no GPU found", "CPU")]
    public void Reads_the_backend_whisper_cpp_chose(string runtime, string line, string expected)
        => WhisperDevice.Describe(runtime, ["whisper_init_from_file_with_params_no_state: loading model", line, "whisper_backend_init: using BLAS backend"])
            .ShouldBe(expected);

    [Fact]
    public void A_coreml_runtime_without_its_encoder_falls_back_to_metal()
    {
        string[] log =
        [
            "whisper_init_state: loading Core ML model from 'ggml-large-v3-q5_0-encoder.mlmodelc'",
            "whisper_init_state: failed to load Core ML model from 'ggml-large-v3-q5_0-encoder.mlmodelc'",
            "whisper_backend_init_gpu: using Metal backend",
        ];

        WhisperDevice.Describe("CoreML", log).ShouldBe("Apple Metal (GPU)");
        WhisperDevice.Describe("CoreML", [.. log[..1], "whisper_init_state: Core ML model loaded", log[2]])
            .ShouldBe("Apple Neural Engine (CoreML) + Apple Metal (GPU)");
    }

    [Theory]
    [InlineData("failed to load Core ML model from '~/models/ggml-large-v3-encoder.mlmodelc'", "Apple Metal (GPU)")]
    [InlineData("Core ML model loaded", "Apple Neural Engine (CoreML) + Apple Metal (GPU)")]
    public void Reads_the_lines_whisper_net_1_9_logs_on_an_m2_when_decoding_starts(string coreMlResult, string expected)
    {
        // Captured on an M2 (macOS 15) with the CoreML runtime; these come with the first decode, not Build().
        string[] log =
        [
            "whisper_init_with_params_no_state: use gpu    = 1",
            "ggml_metal_device_init: GPU name:   MTL0 (Apple M2)",
            "whisper_backend_init_gpu: found GPU device 0: MTL0 (type: 1, cnt: 0)",
            "whisper_backend_init_gpu: using MTL0 backend",
            "whisper_backend_init: using BLAS backend",
            "whisper_init_state: loading Core ML model from '~/models/ggml-large-v3-encoder.mlmodelc'",
            "whisper_init_state: " + coreMlResult,
        ];

        WhisperDevice.Describe("CoreML", log).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Cuda", "NVIDIA CUDA (GPU)")]
    [InlineData("Vulkan", "Vulkan (GPU)")]
    [InlineData("Cpu", "CPU")]
    public void Without_log_lines_the_runtime_name_is_used(string runtime, string expected)
        => WhisperDevice.Describe(runtime, []).ShouldBe(expected);

    [Theory]
    [InlineData(Core.Platform.WhisperSize.Turbo, WhisperModels.LargeV3TurboQ5)]
    [InlineData(Core.Platform.WhisperSize.Large, WhisperModels.LargeV3Q5)]
    [InlineData(Core.Platform.WhisperSize.LargeFull, WhisperModels.LargeV3)]
    public void Suggested_sizes_map_to_catalogue_models(Core.Platform.WhisperSize size, string id)
    {
        WhisperModels.For(size).ShouldBe(id);
        WhisperModels.All.ShouldContain(m => m.Id == id);
    }

    [Fact]
    public void Gpu_devices_are_told_apart_from_the_cpu()
    {
        WhisperDevice.IsGpu("Apple Metal (GPU)").ShouldBeTrue();
        WhisperDevice.IsGpu("Apple Neural Engine (CoreML)").ShouldBeTrue();
        WhisperDevice.IsGpu("CPU").ShouldBeFalse();
    }
}
