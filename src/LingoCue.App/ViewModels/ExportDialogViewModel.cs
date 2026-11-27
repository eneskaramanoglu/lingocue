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

    /// <summary>A transparent video of the subtitles for an editor's upper track (Premiere Pro, Resolve).</summary>
    Overlay,
}

public sealed partial class TrackChoice(SubtitleTrack track, bool selected) : ObservableObject
{
    public SubtitleTrack Track { get; } = track;

    public string Label => Track.IsMixedLanguage()
        ? $"{Track.Name} · karışık ({string.Join(", ", Track.SpokenLanguages())}) · {Track.Cues.Count} satır"
        : $"{Track.Name} · {Track.Language} · {Track.Cues.Count} satır";

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = selected;
}

/// <param name="Hevc10">The encoder can make the 10-bit HEVC format (x264 stands for x265 there).</param>
public sealed record EncoderChoice(VideoEncoder Encoder, string Label, bool Hevc10 = true);

public sealed record VideoFormatChoice(VideoFormat Format, string Label);

public sealed record SubtitleFormatChoice(ISubtitleFormat Format, string Label);

/// <summary>What the user chose in the export dialog.</summary>
public sealed record ExportChoice(
    ExportKind Kind,
    IReadOnlyList<SubtitleTrack> Tracks,
    ISubtitleFormat SubtitleFormat,
    bool Utf8Bom,
    VideoEncoder Encoder,
    int Quality,
    SoftSubContainer Container,
    bool SplitByLanguage = false,
    OverlayCodec OverlayCodec = OverlayCodec.Animation,
    VideoFormat VideoFormat = VideoFormat.H264,
    bool OverlayFades = true);

public sealed record OverlayCodecChoice(OverlayCodec Codec, string Label);

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
            choice.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(HasMixedTrack));
            };
        }

        Formats =
        [
            new(SubtitleFormats.Srt, "SRT — Premiere Pro, YouTube, çoğu oynatıcı"),
            new(SubtitleFormats.Vtt, "WebVTT — web oynatıcılar"),
            new(SubtitleFormats.Ass, "ASS — stil, karaoke ve iki dilli düzen dahil"),
        ];
        SelectedFormat = Formats[0];
        SelectedOverlayCodec = OverlayCodecs[0];
        HasFades = project.Presentation.FadeInMs > 0 || project.Presentation.FadeOutMs > 0;
        SelectedVideoFormat = VideoFormats[0];

        Encoders = [new(VideoEncoder.X264, "İşlemci (x264) — en iyi kalite/boyut, her yerde çalışır")];
        foreach (var status in hardware.Where(h => h.Available))
        {
            Encoders.Add(new(status.Encoder, status.Encoder switch
            {
                VideoEncoder.Nvenc => "NVIDIA ekran kartı (NVENC) — çok daha hızlı, dosya biraz büyük",
                VideoEncoder.Amf => "AMD ekran kartı (AMF) — çok daha hızlı, dosya biraz büyük",
                VideoEncoder.Qsv => "Intel grafik (Quick Sync) — hızlı, dosya biraz büyük",
                _ => "Apple donanımı (VideoToolbox) — çok daha hızlı, dosya biraz büyük",
            }, status.Hevc10));
        }

        EncoderNote = hardware.FirstOrDefault(h => h.Present && !h.Available)?.Problem;
        // Hardware when it works (renders are long, speed matters most); the first one found is the
        // fastest (a laptop's NVIDIA card before its Intel graphics).
        SelectedEncoder = Encoders.Count > 1 ? Encoders[1] : Encoders[0];

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
    [NotifyPropertyChangedFor(nameof(IsSubtitleFiles), nameof(IsBurnIn), nameof(IsSoftSub), nameof(IsOverlay), nameof(ShowsTrackList), nameof(CanStart), nameof(StartLabel))]
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

    public bool IsOverlay
    {
        get => Kind == ExportKind.Overlay;
        set
        {
            if (value)
            {
                Kind = ExportKind.Overlay;
            }
        }
    }

    /// <summary>Burn-in and the overlay draw one track (with its plan or bilingual partner); the other kinds pick tracks.</summary>
    public bool ShowsTrackList => Kind is ExportKind.SubtitleFiles or ExportKind.SoftSub;

    // ---- Overlay
    public IReadOnlyList<OverlayCodecChoice> OverlayCodecs { get; } =
    [
        new(OverlayCodec.Animation, "QuickTime Animation — kayıpsız, çok küçük dosya"),
        new(OverlayCodec.ProRes4444, "Apple ProRes 4444 — en geniş uyum, büyük dosya"),
    ];

    [ObservableProperty]
    public partial OverlayCodecChoice SelectedOverlayCodec { get; set; }

    /// <summary>The style fades subtitles in or out (Stil → Efektler).</summary>
    public bool HasFades { get; }

    /// <summary>
    /// Off by default: during a fade every overlay frame is stored whole, which made a 4K overlay about ten
    /// times larger. The editor can add its own transitions.
    /// </summary>
    [ObservableProperty]
    public partial bool OverlayFades { get; set; }

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

    /// <summary>H.264 for sharing; HEVC 10 bit or ProRes 422 HQ as a high-quality master.</summary>
    public IReadOnlyList<VideoFormatChoice> VideoFormats { get; } =
    [
        new(VideoFormat.H264, "H.264 · MP4 — paylaşım (YouTube, sosyal medya), 8 bit"),
        new(VideoFormat.Hevc10, "HEVC 10 bit · MP4 — yüksek kalite, 4K ve 10 bit kaynaklar için"),
        new(VideoFormat.ProRes422Hq, "ProRes 422 HQ · MOV — kurgu için ana kopya (4K'da dakikası ~2,6 GB)"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesQuality), nameof(UsesEncoder), nameof(FormatNote))]
    public partial VideoFormatChoice SelectedVideoFormat { get; set; }

    /// <summary>ProRes has a fixed quality and is encoded on the CPU.</summary>
    public bool UsesQuality => SelectedVideoFormat.Format != VideoFormat.ProRes422Hq;

    public bool UsesEncoder => SelectedVideoFormat.Format != VideoFormat.ProRes422Hq;

    /// <summary>The encoder that will really run: HEVC on a device without 10-bit HEVC falls back to x265.</summary>
    private VideoEncoder EffectiveEncoder => SelectedVideoFormat.Format == VideoFormat.Hevc10 && !SelectedEncoder.Hevc10
        ? VideoEncoder.X264
        : SelectedEncoder.Encoder;

    public string? FormatNote => SelectedVideoFormat.Format switch
    {
        VideoFormat.Hevc10 when !SelectedEncoder.Hevc10 => "Seçili donanım HEVC 10 bit kodlayamıyor; işlemci (x265) kullanılacak. Daha yavaştır (4K'da gerçek sürenin ~2 katı).",
        VideoFormat.Hevc10 when SelectedEncoder.Encoder == VideoEncoder.X264 => "İşlemciyle x265 kullanılır: en iyi kalite/boyut ama yavaş (4K'da gerçek sürenin ~2 katı).",
        VideoFormat.ProRes422Hq => "ProRes işlemciyle kodlanır ve kalite ayarı yoktur. Görüntü yine de bir kez yeniden kodlanır; hiç kayıp istemiyorsanız \"Altyazı katmanı\"nı kullanın.",
        _ => null,
    };

    partial void OnSelectedEncoderChanged(EncoderChoice value) => OnPropertyChanged(nameof(FormatNote));

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

    /// <summary>A chosen track mixes languages: offer one file (or stream) per language.</summary>
    public bool HasMixedTrack => Tracks.Any(t => t.IsSelected && t.Track.IsMixedLanguage());

    [ObservableProperty]
    public partial bool SplitByLanguage { get; set; }

    public string StartLabel => Kind == ExportKind.SubtitleFiles ? "Kaydet…" : "Kuyruğa ekle…";

    public bool CanStart => Kind switch
    {
        ExportKind.SubtitleFiles => Tracks.Any(t => t.IsSelected),
        ExportKind.BurnIn or ExportKind.Overlay => CanBurnIn && BurnTrack is not null,
        _ => HasVideo && Tracks.Any(t => t.IsSelected),
    };

    public ExportChoice? BuildResult()
    {
        if (!CanStart)
        {
            return null;
        }

        IReadOnlyList<SubtitleTrack> tracks = Kind is ExportKind.BurnIn or ExportKind.Overlay
            ? [BurnTrack!]
            : [.. Tracks.Where(t => t.IsSelected).SelectMany(t => SplitByLanguage && HasMixedTrack ? TrackOperations.SplitByLanguage(t.Track) : [t.Track])];
        return new ExportChoice(Kind, tracks, SelectedFormat.Format, IsSrt && PremiereBom, EffectiveEncoder,
            (int)Math.Round(Quality), UseMkv ? SoftSubContainer.Mkv : SoftSubContainer.Mp4, SplitByLanguage && HasMixedTrack, SelectedOverlayCodec.Codec, SelectedVideoFormat.Format, OverlayFades);
    }
}
