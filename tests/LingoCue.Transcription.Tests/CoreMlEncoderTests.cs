using System.IO.Compression;
using LingoCue.Transcription.Whisper;

namespace LingoCue.Transcription.Tests;

public sealed class CoreMlEncoderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-coreml-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("ggml-large-v3-q5_0.bin", "ggml-large-v3-encoder.mlmodelc")]
    [InlineData("ggml-large-v3.bin", "ggml-large-v3-encoder.mlmodelc")]
    [InlineData("ggml-large-v3-turbo-q5_0.bin", "ggml-large-v3-turbo-encoder.mlmodelc")]
    [InlineData("ggml-base.en.bin", "ggml-base.en-encoder.mlmodelc")]
    public void Names_the_folder_like_whisper_cpp(string model, string folder)
        => CoreMlEncoder.FolderName(model).ShouldBe(folder);

    [Fact]
    public void Q5_and_the_full_model_share_one_encoder()
    {
        CoreMlEncoder.ZipFor(WhisperModels.LargeV3Q5).ShouldBe(CoreMlEncoder.ZipFor(WhisperModels.LargeV3));
        CoreMlEncoder.ZipFor(WhisperModels.LargeV3TurboQ5).FileName.ShouldBe("ggml-large-v3-turbo-encoder.mlmodelc.zip");
    }

    [Fact]
    public void Unpacks_the_encoder_without_macos_metadata_and_switches_it_off_and_on()
    {
        var zip = Path.Combine(_dir, "enc.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("ggml-large-v3-encoder.mlmodelc/");
            Write(archive, "ggml-large-v3-encoder.mlmodelc/model.mil", "mil");
            Write(archive, "ggml-large-v3-encoder.mlmodelc/weights/weight.bin", "w");
            Write(archive, "__MACOSX/ggml-large-v3-encoder.mlmodelc/._model.mil", "junk");
        }

        CoreMlEncoder.Extract(zip, _dir, "ggml-large-v3-encoder.mlmodelc");

        var folder = CoreMlEncoder.FolderFor(_dir, WhisperModels.LargeV3Q5);
        File.ReadAllText(Path.Combine(folder, "weights", "weight.bin")).ShouldBe("w");
        Directory.Exists(Path.Combine(_dir, "__MACOSX")).ShouldBeFalse();
        CoreMlEncoder.IsInstalled(_dir, WhisperModels.LargeV3).ShouldBeTrue();

        CoreMlEncoder.SetEnabled(_dir, WhisperModels.LargeV3Q5, enabled: false);
        Directory.Exists(folder).ShouldBeFalse(); // whisper.cpp no longer finds it
        CoreMlEncoder.IsInstalled(_dir, WhisperModels.LargeV3Q5).ShouldBeTrue();

        CoreMlEncoder.SetEnabled(_dir, WhisperModels.LargeV3Q5, enabled: true);
        Directory.Exists(folder).ShouldBeTrue();
    }

    [Fact]
    public void Rejects_entries_that_leave_the_folder()
    {
        var zip = Path.Combine(_dir, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "ggml-large-v3-encoder.mlmodelc/../../escape.txt", "x");
        }

        Should.Throw<InvalidDataException>(() => CoreMlEncoder.Extract(zip, _dir, "ggml-large-v3-encoder.mlmodelc"));
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(text);
    }
}
