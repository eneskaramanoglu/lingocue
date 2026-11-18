using LingoCue.Transcription.Models;

namespace LingoCue.Transcription.Whisper;

/// <summary>
/// Whisper models offered in the app, pinned to a commit of the official ggerganov/whisper.cpp
/// Hugging Face repository with sizes and SHA256 (LFS object ids). Quantized q5_0 builds keep
/// accuracy close to the full models while fitting 4 GB GPUs.
/// </summary>
public static class WhisperModels
{
    private const string WhisperRepo = "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/";
    private const string VadRepo = "https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/";

    public const string LargeV3Q5 = "large-v3-q5_0";
    public const string LargeV3TurboQ5 = "large-v3-turbo-q5_0";

    public static IReadOnlyList<TranscriptionModel> All { get; } =
    [
        new(LargeV3Q5, "large-v3 (q5_0)", "En doğru; Türkçe için önerilen. ~1,1 GB, 4 GB GPU'ya sığar.", 1_081_140_203),
        new(LargeV3TurboQ5, "large-v3-turbo (q5_0)", "Yaklaşık 3-4 kat hızlı, Türkçede biraz daha az isabetli. ~0,6 GB.", 574_041_195),
    ];

    public static ModelFile FileFor(string modelId) => modelId switch
    {
        LargeV3Q5 => new("ggml-large-v3-q5_0.bin", new Uri(WhisperRepo + "ggml-large-v3-q5_0.bin"), 1_081_140_203,
            "d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1"),
        LargeV3TurboQ5 => new("ggml-large-v3-turbo-q5_0.bin", new Uri(WhisperRepo + "ggml-large-v3-turbo-q5_0.bin"), 574_041_195,
            "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2"),
        _ => throw new ArgumentOutOfRangeException(nameof(modelId), modelId, "Unknown Whisper model."),
    };

    /// <summary>Silero VAD v6.2.0 (ggml), used to drop hallucinations in silent stretches.</summary>
    public static ModelFile SileroVad { get; } = new(
        "ggml-silero-v6.2.0.bin", new Uri(VadRepo + "ggml-silero-v6.2.0.bin"), 885_098,
        "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987");
}
