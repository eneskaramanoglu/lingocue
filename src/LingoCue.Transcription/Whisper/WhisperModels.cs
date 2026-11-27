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
    public const string LargeV3 = "large-v3";

    public static IReadOnlyList<TranscriptionModel> All { get; } =
    [
        new(LargeV3Q5, "large-v3 (q5_0)", "Türkçe için önerilen; tam modele çok yakın. ~1,1 GB, 4 GB GPU'ya sığar.", 1_081_140_203),
        new(LargeV3, "large-v3 (tam)", "Sıkıştırılmamış model; en yüksek isabet. ~3,1 GB, 6 GB ve üstü GPU ister.", 3_095_033_483),
        new(LargeV3TurboQ5, "large-v3-turbo (q5_0)", "Yaklaşık 3-4 kat hızlı, Türkçede biraz daha az isabetli. ~0,6 GB; GPU'suz bilgisayarlar için.", 574_041_195),
    ];

    /// <summary>The catalogue model for a size suggested by <see cref="Core.Platform.HardwareAdvisor"/>.</summary>
    public static string For(Core.Platform.WhisperSize size) => size switch
    {
        Core.Platform.WhisperSize.Turbo => LargeV3TurboQ5,
        Core.Platform.WhisperSize.LargeFull => LargeV3,
        _ => LargeV3Q5,
    };

    public static ModelFile FileFor(string modelId) => modelId switch
    {
        LargeV3 => new("ggml-large-v3.bin", new Uri(WhisperRepo + "ggml-large-v3.bin"), 3_095_033_483,
            "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2"),
        LargeV3Q5 => new("ggml-large-v3-q5_0.bin", new Uri(WhisperRepo + "ggml-large-v3-q5_0.bin"), 1_081_140_203,
            "d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1"),
        LargeV3TurboQ5 => new("ggml-large-v3-turbo-q5_0.bin", new Uri(WhisperRepo + "ggml-large-v3-turbo-q5_0.bin"), 574_041_195,
            "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2"),
        _ => throw new ArgumentOutOfRangeException(nameof(modelId), modelId, "Unknown Whisper model."),
    };

    /// <summary>
    /// Small multilingual model (base, q5_1) that only detects the language of each speech region of a
    /// mixed-language recording: about 30 times faster per region than large-v3, which steps in when it is unsure.
    /// </summary>
    public static ModelFile LanguageDetector { get; } = new(
        "ggml-base-q5_1.bin", new Uri(WhisperRepo + "ggml-base-q5_1.bin"), 59_707_625,
        "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898");

    /// <summary>Silero VAD v6.2.0 (ggml), used to drop hallucinations in silent stretches.</summary>
    public static ModelFile SileroVad { get; } = new(
        "ggml-silero-v6.2.0.bin", new Uri(VadRepo + "ggml-silero-v6.2.0.bin"), 885_098,
        "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987");
}
