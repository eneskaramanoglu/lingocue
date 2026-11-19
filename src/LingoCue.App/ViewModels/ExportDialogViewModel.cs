using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LingoCue.Core.Fonts;
using LingoCue.Core.Formats;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Media.Probing;
using LingoCue.Media.Rendering;

namespace LingoCue.App.ViewModels;

public enum ExportKind
{
    SubtitleFiles,
    BurnIn,
    SoftSub,
}

public sealed partial class TrackChoice(SubtitleTrack track, bool selected) : ObservableObject
{
    public SubtitleTrack Track { get; } = track;

    public string Label => $"{Track.Name} · {Track.Language} · {Track.Cues.Count} satır";

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = selected;
}

public sealed record EncoderChoice(VideoEncoder Encoder, string Label);

public sealed record SubtitleFormatChoice(ISubtitleFormat Format, string Label);

/// <summary>What the user chose in the export dialog.</summary>
public sealed record ExportChoice(
    ExportKind Kind,
    IReadOnlyList<SubtitleTrack> Tracks,
    ISubtitleFormat SubtitleFormat,
    bool Utf8Bom,
    VideoEncoder Encoder,
    int Quality,
    SoftSubContainer Container);

/// <summary>
/// One dialog for every export: subtitle files (SRT for Premiere Pro, VTT, ASS), a video with the
/// subtitles burned in (exactly the preview), or a video with switchable subtitle tracks.
/// </summary>
public sealed partial class ExportDialogViewModel : ViewModelBase
{
    public ExportDialogViewModel(
        Project project, SubtitleTrack activeTrack, MediaInfo? media, IFontCatalog? fonts, string? projectPath,
        IReadOnlyList<HardwareEncoderStatus> hardware, string? burnInProblem = null)
    {
        HasVideo = media?.Video is not null;
        BurnInProblem = burnInProblem;
        Tracks = [.. project.Tracks.Where(t => t.Cues.Count > 0).Select(t => new TrackChoice(t, t == activeTrack))];
        BurnTrack = Tracks.FirstOrDefault(t => t.Track == activeTrack)?.Track ?? Tracks.FirstOrDefault()?.Track;
        foreach (var choice in Tracks)
        {
            choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanStart));
        }

        Formats =
        [
            new(SubtitleFormats.Srt, "SRT — Premiere Pro, YouTube, çoğu oynatıcı"),
            new(SubtitleFormats.Vtt, "WebVTT — web oynatıcılar"),
            new(SubtitleFormats.Ass, "ASS — stil, karaoke ve iki dilli düzen dahil"),
        ];
        SelectedFormat = Formats[0];

        Encoders = [new(VideoEncoder.X264, "İşlemci (x264) — en iyi kalite/boyut, her yerde çalışır")];
        foreach (var status in hardware.Where(h => h.Available))
        {
            Encoders.Add(new(status.Encoder, status.Encoder == VideoEncoder.Nvenc
                ? "NVIDIA ekran kartı (NVENC) — çok daha hızlı, dosya biraz büyük"
                : "Apple donanımı (VideoToolbox) — çok daha hızlı, dosya biraz büyük"));
        }

        EncoderNote = hardware.FirstOrDefault(h => h.Present && !h.Available)?.Problem;
        SelectedEncoder = Encoders[^1]; // hardware when it works: renders are long, speed matters most

        var warnings = new List<string>();
        if (media is not null)
        {
            warnings.AddRange(ExportJobs.Warnings(media));
        }

        if (fonts is not null && !string.IsNullOrWhiteSpace(project.Style.FontName))
        {
            var status = fonts.Check(project.Style.FontName, FontCatalog.ProjectFontDirectory(projectPath));
            if (status.Source == FontSource.Missing)
            {
                warnings.Add($"\"{project.Style.FontName}\" fontu bu bilgisayarda yok; videoda başka bir font kullanılır. Stil panelinden font dosyasını projeye ekleyin.");
            }
            else if (!status.HasTurkishLetters)
            {
                warnings.Add($"\"{project.Style.FontName}\" fontunda bazı Türkçe harfler yok; o harfler başka fontla çizilir.");
            }
        }

        StyleWarning = warnings.Count > 0 ? string.Join("\n", warnings) : null;
        BilingualNote = AssScriptBuilder.SecondaryTrackFor(project, activeTrack) is { } second && project.Presentation.Bilingual.Enabled
            ? $"İki dilli mod açık: \"{second.Name}\" alta eklenir."
            : null;
    }

    public bool HasVideo { get; }

    /// <summary>Why burn-in is unavailable (FFmpeg without libass), with the fix; null when it works.</summary>
    public string? BurnInProblem { get; }

    public bool CanBurnIn => HasVideo && BurnInProblem is null;

    public ObservableCollection<TrackChoice> Tracks { get; }

    public IReadOnlyList<SubtitleTrack> AllTracks => [.. Tracks.Select(t => t.Track)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubtitleFiles), nameof(IsBurnIn), nameof(IsSoftSub), nameof(CanStart), nameof(StartLabel))]
    public partial ExportKind Kind { get; set; }

    public bool IsSubtitleFiles
    {
        get => Kind == ExportKind.SubtitleFiles;
        set
        {
            if (value)
            {
                Kind = ExportKind.SubtitleFiles;
            }
        }
    }

    public bool IsBurnIn
    {
        get => Kind == ExportKind.BurnIn;
        set
        {
            if (value)
            {
                Kind = ExportKind.BurnIn;
            }
        }
    }

    public bool IsSoftSub
    {
        get => Kind == ExportKind.SoftSub;
        set
        {
            if (value)
            {
                Kind = ExportKind.SoftSub;
            }
        }
    }

    // ---- Subtitle files
    public IReadOnlyList<SubtitleFormatChoice> Formats { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSrt))]
    public partial SubtitleFormatChoice SelectedFormat { get; set; }

    public bool IsSrt => SelectedFormat.Format is SrtFormat;

    /// <summary>UTF-8 with BOM: Premiere Pro then always reads ğ, ş, ı, İ correctly.</summary>
    [ObservableProperty]
    public partial bool PremiereBom { get; set; } = true;

    // ---- Burn-in
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial SubtitleTrack? BurnTrack { get; set; }

    public ObservableCollection<EncoderChoice> Encoders { get; }

    [ObservableProperty]
    public partial EncoderChoice SelectedEncoder { get; set; }

    public string? EncoderNote { get; }

    /// <summary>x264 CRF: 16 = very high quality, 19 = visually lossless (default), 24 = small file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityText))]
    public partial double Quality { get; set; } = 19;

    public string QualityText => Quality switch
    {
        <= 17 => "Çok yüksek kalite (büyük dosya)",
        <= 20 => "Yüksek kalite (önerilen)",
        <= 23 => "Dengeli",
        _ => "Küçük dosya",
    };

    public string? StyleWarning { get; }

    public string? BilingualNote { get; }

    // ---- Soft subtitles
    [ObservableProperty]
    public partial bool UseMkv { get; set; }

    public string StartLabel => Kind == ExportKind.SubtitleFiles ? "Kaydet…" : "Kuyruğa ekle…";

    public bool CanStart => Kind switch
    {
        ExportKind.SubtitleFiles => Tracks.Any(t => t.IsSelected),
        ExportKind.BurnIn => CanBurnIn && BurnTrack is not null,
        _ => HasVideo && Tracks.Any(t => t.IsSelected),
    };

    public ExportChoice? BuildResult()
    {
        if (!CanStart)
        {
            return null;
        }

        IReadOnlyList<SubtitleTrack> tracks = Kind == ExportKind.BurnIn
            ? [BurnTrack!]
            : [.. Tracks.Where(t => t.IsSelected).Select(t => t.Track)];
        return new ExportChoice(Kind, tracks, SelectedFormat.Format, IsSrt && PremiereBom, SelectedEncoder.Encoder,
            (int)Math.Round(Quality), UseMkv ? SoftSubContainer.Mkv : SoftSubContainer.Mp4);
    }
}
