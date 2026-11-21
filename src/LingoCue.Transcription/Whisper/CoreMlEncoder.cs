using System.IO.Compression;
using LingoCue.Transcription.Models;

namespace LingoCue.Transcription.Whisper;

/// <summary>
/// The optional Core ML encoder that lets whisper.cpp run the encoder on the Apple Neural Engine.
/// whisper.cpp loads <c>&lt;model&gt;-encoder.mlmodelc</c> next to the ggml file when it exists (the
/// quantization suffix is dropped, so q5_0 and the full model share one encoder) and falls back to
/// Metal without it. Turning the option off renames the folder so whisper.cpp no longer finds it.
/// </summary>
public static class CoreMlEncoder
{
    private const string Repo = "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/";
    private const string DisabledSuffix = ".off";

    /// <summary>Folder name whisper.cpp looks for next to a model file (same rule as whisper.cpp itself).</summary>
    public static string FolderName(string modelFileName)
    {
        ArgumentNullException.ThrowIfNull(modelFileName);
        var stem = Path.GetFileNameWithoutExtension(modelFileName);
        var dash = stem.LastIndexOf('-');
        if (dash >= 0 && stem.Length - dash == 5 && stem[dash + 1] == 'q' && stem[dash + 3] == '_')
        {
            stem = stem[..dash];
        }

        return stem + "-encoder.mlmodelc";
    }

    /// <summary>The zipped encoder for a catalogue model (pinned with size and SHA256).</summary>
    public static ModelFile ZipFor(string modelId) => modelId switch
    {
        WhisperModels.LargeV3TurboQ5 => new("ggml-large-v3-turbo-encoder.mlmodelc.zip", new Uri(Repo + "ggml-large-v3-turbo-encoder.mlmodelc.zip"), 1_173_393_014,
            "84bedfe895bd7b5de6e8e89a0803dfc5addf8c0c5bc4c937451716bf7cf7988a"),
        _ => new("ggml-large-v3-encoder.mlmodelc.zip", new Uri(Repo + "ggml-large-v3-encoder.mlmodelc.zip"), 1_175_711_232,
            "47837be7594a29429ec08620043390c4d6d467f8bd362df09e9390ace76a55a4"),
    };

    public static string FolderFor(string modelDirectory, string modelId)
        => Path.Combine(modelDirectory, FolderName(WhisperModels.FileFor(modelId).FileName));

    public static bool IsInstalled(string modelDirectory, string modelId)
        => Directory.Exists(FolderFor(modelDirectory, modelId)) || Directory.Exists(FolderFor(modelDirectory, modelId) + DisabledSuffix);

    /// <summary>Downloads and unpacks the encoder once (the zip is deleted afterwards) and makes sure it is switched on.</summary>
    public static async Task EnsureAsync(string modelDirectory, string modelId, ModelDownloader downloader, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        SetEnabled(modelDirectory, modelId, enabled: true);
        var folder = FolderFor(modelDirectory, modelId);
        if (Directory.Exists(folder))
        {
            return;
        }

        var zip = await downloader.EnsureAsync(modelDirectory, ZipFor(modelId), progress, cancellationToken);
        Extract(zip, modelDirectory, Path.GetFileName(folder));
        File.Delete(zip);
    }

    /// <summary>Unpacks only <paramref name="folderName"/>/… entries (the zips also carry macOS <c>__MACOSX</c> metadata).</summary>
    internal static void Extract(string zipPath, string modelDirectory, string folderName)
    {
        var staging = Path.Combine(modelDirectory, folderName + ".partial");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(folderName + "/", StringComparison.Ordinal))
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(staging, entry.FullName[(folderName.Length + 1)..]));
                if (!target.StartsWith(root, StringComparison.Ordinal) && target != root.TrimEnd(Path.DirectorySeparatorChar))
                {
                    throw new InvalidDataException($"Unexpected path in the encoder archive: {entry.FullName}");
                }

                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        Directory.Move(staging, Path.Combine(modelDirectory, folderName));
    }

    /// <summary>Switches an installed encoder on or off by renaming its folder; nothing happens when it is not installed.</summary>
    public static void SetEnabled(string modelDirectory, string modelId, bool enabled)
    {
        var on = FolderFor(modelDirectory, modelId);
        var off = on + DisabledSuffix;
        var (from, to) = enabled ? (off, on) : (on, off);
        if (Directory.Exists(from) && !Directory.Exists(to))
        {
            Directory.Move(from, to);
        }
    }
}
