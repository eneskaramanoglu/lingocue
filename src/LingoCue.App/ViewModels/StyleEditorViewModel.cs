using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Services;
using LingoCue.Core.Fonts;
using LingoCue.Core.Projects;
using LingoCue.Core.Subtitles;
using LingoCue.Media.Rendering;

namespace LingoCue.App.ViewModels;

public sealed record KaraokeChoice(KaraokeMode Mode, string Label);

public sealed record BackgroundChoice(BackgroundKind Kind, string Label, string Hint);

/// <summary>
/// The "Stil" panel. Every edit is written to the project at once (style and presentation
/// options) and raises <see cref="Changed"/>, so the preview — the real libass output — follows.
/// </summary>
public sealed partial class StyleEditorViewModel : ViewModelBase
{
    // Properties that map to the project; other properties (lists, selections) never write back.
    private static readonly HashSet<string> s_editable =
    [
        nameof(FontName), nameof(FontSize), nameof(Bold), nameof(Italic), nameof(Underline), nameof(Spacing), nameof(UpperCase),
        nameof(PrimaryColor), nameof(OutlineColor), nameof(ShadowColor), nameof(Outline), nameof(Shadow),
        nameof(SelectedBackground), nameof(BoxColor), nameof(BoxPaddingX), nameof(BoxPaddingY), nameof(FrameWidth), nameof(FrameColor), nameof(CornerRadius),
        nameof(Alignment), nameof(MarginV), nameof(MarginL), nameof(MarginR), nameof(FadeIn), nameof(FadeOut),
        nameof(SelectedKaraoke), nameof(HighlightColor),
        nameof(BilingualEnabled), nameof(SecondaryTrack), nameof(SecondaryFontName), nameof(SecondaryFontSize), nameof(SecondaryColor), nameof(Gap),
    ];

    private readonly IStyleTemplateStore? _templates;
    private readonly IFontCatalog? _fonts;
    private readonly IFilePickerService? _files;
    private readonly IDialogService? _dialogs;
    private readonly StyleThumbnails? _thumbnails;
    private Project? _project;
    private SubtitleTrack? _activeTrack;
    private string? _projectPath;
    private bool _loading;

    public StyleEditorViewModel(IStyleTemplateStore templates, IFontCatalog fonts, IFilePickerService files, IDialogService dialogs, StyleThumbnails? thumbnails = null)
    {
        _thumbnails = thumbnails;
        _templates = templates;
        _fonts = fonts;
        _files = files;
        _dialogs = dialogs;
        RefreshTemplates();
    }

    // Designer constructor.
    private StyleEditorViewModel()
    {
    }

    public static StyleEditorViewModel CreateDesignInstance() => new();

    /// <summary>Raised after an edit was written to the project.</summary>
    public event EventHandler? Changed;

    [ObservableProperty]
    public partial bool HasProject { get; private set; }

    public ObservableCollection<StyleTemplate> Templates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyTemplateCommand), nameof(DeleteTemplateCommand), nameof(ExportTemplateCommand))]
    public partial StyleTemplate? SelectedTemplate { get; set; }

    /// <summary>Installed fonts plus the project's own font files (names as libass knows them).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Fonts { get; private set; } = [];

    private string? ProjectFontDirectory => FontCatalog.ProjectFontDirectory(_projectPath);

    public IReadOnlyList<KaraokeChoice> KaraokeChoices { get; } =
    [
        new(KaraokeMode.Off, "Kapalı"),
        new(KaraokeMode.Fill, "Dolan renk (söylendikçe dolar)"),
        new(KaraokeMode.ActiveWord, "Aktif kelime (Shorts tarzı)"),
    ];

    // ---- Text
    [ObservableProperty]
    public partial string FontName { get; set; } = "Arial";

    [ObservableProperty]
    public partial string? FontWarning { get; private set; }

    [ObservableProperty]
    public partial decimal FontSize { get; set; } = 48;

    [ObservableProperty]
    public partial bool Bold { get; set; }

    [ObservableProperty]
    public partial bool Italic { get; set; }

    [ObservableProperty]
    public partial bool Underline { get; set; }

    [ObservableProperty]
    public partial decimal Spacing { get; set; }

    [ObservableProperty]
    public partial bool UpperCase { get; set; }

    // ---- Colors and border
    [ObservableProperty]
    public partial Color PrimaryColor { get; set; } = Colors.White;

    [ObservableProperty]
    public partial Color OutlineColor { get; set; } = Colors.Black;

    [ObservableProperty]
    public partial Color ShadowColor { get; set; } = Color.FromArgb(128, 0, 0, 0);

    [ObservableProperty]
    public partial decimal Outline { get; set; } = 2.5m;

    [ObservableProperty]
    public partial decimal Shadow { get; set; } = 1;

    // ---- Background box
    public IReadOnlyList<BackgroundChoice> BackgroundChoices { get; } =
    [
        new(BackgroundKind.None, "Yok", "Yalnızca kontur ve gölge."),
        new(BackgroundKind.BlockBox, "Blok kutusu", "Tüm satırların arkasında tek dikdörtgen; yazının konturu kalır."),
        new(BackgroundKind.LineBox, "Satır kutusu", "Her satırın arkasında ayrı kutu; kontur görünmez."),
        new(BackgroundKind.Band, "Tam genişlik şerit", "Videonun bir ucundan öbür ucuna, yazı yüksekliğinde şerit."),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackground), nameof(HasSideBox), nameof(HasFrame), nameof(BackgroundInfo), nameof(OutlineInfo), nameof(HasBlockBox))]
    public partial BackgroundChoice? SelectedBackground { get; set; }

    public bool HasBackground => SelectedBackground is { Kind: not BackgroundKind.None };

    /// <summary>The band spans the full width, so it has no horizontal padding.</summary>
    public bool HasSideBox => SelectedBackground is { Kind: BackgroundKind.BlockBox or BackgroundKind.LineBox };

    public bool HasFrame => HasBackground && FrameWidth > 0;

    /// <summary>Box fill; the alpha channel sets the opacity.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundInfo))]
    public partial Color BoxColor { get; set; } = Color.FromArgb(160, 0, 0, 0);

    [ObservableProperty]
    public partial decimal BoxPaddingX { get; set; } = 18;

    [ObservableProperty]
    public partial decimal BoxPaddingY { get; set; } = 8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFrame), nameof(BackgroundInfo))]
    public partial decimal FrameWidth { get; set; }

    /// <summary>Rounded corners of the block box (0 = square).</summary>
    [ObservableProperty]
    public partial decimal CornerRadius { get; set; }

    public bool HasBlockBox => SelectedBackground is { Kind: BackgroundKind.BlockBox };

    [ObservableProperty]
    public partial Color FrameColor { get; set; } = Colors.White;

    public string? OutlineInfo => SelectedBackground?.Kind == BackgroundKind.LineBox
        ? "Satır kutusunda yazının konturu çizilmez; kutu konturun yerini alır."
        : null;

    public string? BackgroundInfo
    {
        get
        {
            var notes = new List<string>();
            if (HasFrame && BoxColor.A < 250)
            {
                notes.Add("Kutu saydamken çerçeve rengi kutunun içinden de görünür; net bir çerçeve için kutuyu opak yapın.");
            }

            if (SelectedBackground?.Kind is BackgroundKind.BlockBox or BackgroundKind.Band)
            {
                notes.Add("Önizleme ve videoya gömme birebir aynıdır. Ayrı altyazı dosyası (ASS/MKV) olarak dışa aktarıldığında libass kullanmayan oynatıcılar bu kutuyu göstermeyebilir.");
            }

            return notes.Count == 0 ? null : string.Join(" ", notes);
        }
    }

    // ---- Position
    [ObservableProperty]
    public partial int Alignment { get; set; } = 2;

    [ObservableProperty]
    public partial decimal MarginV { get; set; } = 40;

    [ObservableProperty]
    public partial decimal MarginL { get; set; } = 60;

    [ObservableProperty]
    public partial decimal MarginR { get; set; } = 60;

    /// <summary>Show safe-area guides over the video (preview aid only, not saved).</summary>
    [ObservableProperty]
    public partial bool ShowSafeArea { get; set; }

    // ---- Effects
    [ObservableProperty]
    public partial decimal FadeIn { get; set; }

    [ObservableProperty]
    public partial decimal FadeOut { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKaraokeOn))]
    public partial KaraokeChoice? SelectedKaraoke { get; set; }

    public bool IsKaraokeOn => SelectedKaraoke is { Mode: not KaraokeMode.Off };

    [ObservableProperty]
    public partial Color HighlightColor { get; set; } = Colors.Yellow;

    [ObservableProperty]
    public partial string? KaraokeInfo { get; private set; }

    // ---- Bilingual
    [ObservableProperty]
    public partial bool BilingualEnabled { get; set; }

    public ObservableCollection<SubtitleTrack> SecondaryTracks { get; } = [];

    [ObservableProperty]
    public partial SubtitleTrack? SecondaryTrack { get; set; }

    /// <summary>Font of the second (bilingual) track; a Korean track needs one with Hangul.</summary>
    [ObservableProperty]
    public partial string SecondaryFontName { get; set; } = "Arial";

    [ObservableProperty]
    public partial string? SecondaryFontWarning { get; private set; }

    [ObservableProperty]
    public partial decimal SecondaryFontSize { get; set; } = 38;

    [ObservableProperty]
    public partial Color SecondaryColor { get; set; } = Color.FromRgb(255, 230, 140);

    [ObservableProperty]
    public partial decimal Gap { get; set; } = 12;

    [ObservableProperty]
    public partial string? BilingualInfo { get; private set; }

    /// <summary>Shows the project's current look (call when the project or the active track changes).</summary>
    public void Load(Project? project, SubtitleTrack? activeTrack, string? projectPath)
    {
        _loading = true;
        try
        {
            _project = project;
            _activeTrack = activeTrack;
            _projectPath = projectPath;
            RefreshFonts();
            HasProject = project is not null;
            var style = project?.Style ?? new SubtitleStyle();
            var presentation = project?.Presentation ?? new PresentationOptions();

            FontName = style.FontName;
            FontSize = (decimal)style.FontSize;
            Bold = style.Bold;
            Italic = style.Italic;
            Underline = style.Underline;
            Spacing = (decimal)style.Spacing;
            UpperCase = presentation.UpperCase;
            PrimaryColor = ToColor(style.PrimaryColor);
            OutlineColor = ToColor(style.OutlineColor);
            ShadowColor = ToColor(style.BackColor);
            Outline = (decimal)style.Outline;
            Shadow = (decimal)style.Shadow;
            var background = SubtitleBackground.Of(style);
            if (background.Kind != style.Background.Kind)
            {
                // A box from an older project or an imported ASS: its padding was the outline/shadow size.
                Outline = 2.5m;
                Shadow = 0;
            }

            SelectedBackground = BackgroundChoices.First(b => b.Kind == background.Kind);
            BoxColor = ToColor(background.Color);
            BoxPaddingX = (decimal)background.PaddingX;
            BoxPaddingY = (decimal)background.PaddingY;
            FrameWidth = (decimal)background.FrameWidth;
            CornerRadius = (decimal)background.CornerRadius;
            FrameColor = ToColor(background.FrameColor);
            Alignment = style.Alignment;
            MarginV = style.MarginV;
            MarginL = style.MarginL;
            MarginR = style.MarginR;
            FadeIn = presentation.FadeInMs;
            FadeOut = presentation.FadeOutMs;
            SelectedKaraoke = KaraokeChoices.First(k => k.Mode == presentation.Karaoke.Mode);
            HighlightColor = ToColor(presentation.Karaoke.HighlightColor);

            var bilingual = presentation.Bilingual;
            SecondaryTracks.Clear();
            foreach (var track in project?.Tracks.Where(t => t != activeTrack) ?? [])
            {
                SecondaryTracks.Add(track);
            }

            BilingualEnabled = bilingual.Enabled;
            SecondaryTrack = SecondaryTracks.FirstOrDefault(t => t.Id == bilingual.SecondaryTrackId)
                             ?? SecondaryTracks.FirstOrDefault(t => t.Kind == TrackKind.Translation)
                             ?? SecondaryTracks.FirstOrDefault();
            SecondaryFontName = bilingual.SecondaryStyle.FontName;
            SecondaryFontSize = (decimal)bilingual.SecondaryStyle.FontSize;
            SecondaryColor = ToColor(bilingual.SecondaryStyle.PrimaryColor);
            Gap = bilingual.Gap;
        }
        finally
        {
            _loading = false;
        }

        MarkAppliedCard();
        UpdateInfo();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(FontName))
        {
            UpdateFontWarning();
            UpdateSecondaryFontWarning();
        }

        if (e.PropertyName is nameof(SecondaryFontName) or nameof(SecondaryTrack))
        {
            UpdateSecondaryFontWarning();
        }

        if (!_loading && e.PropertyName is { } name && s_editable.Contains(name))
        {
            WriteToProject();
            UpdateInfo();
        }
    }

    private void WriteToProject()
    {
        if (_project is not { } project)
        {
            return;
        }

        project.Style = project.Style with
        {
            FontName = string.IsNullOrWhiteSpace(FontName) ? "Arial" : FontName.Trim(),
            FontSize = (double)Math.Clamp(FontSize, 8, 300),
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Spacing = (double)Spacing,
            PrimaryColor = ToAss(PrimaryColor),
            OutlineColor = ToAss(OutlineColor),
            BackColor = ToAss(ShadowColor),
            // The box comes from Background; the ASS border style follows it when the script is built.
            BorderStyle = AssBorderStyle.Outline,
            Background = new SubtitleBackground
            {
                Kind = SelectedBackground?.Kind ?? BackgroundKind.None,
                Color = ToAss(BoxColor),
                PaddingX = (double)Math.Clamp(BoxPaddingX, 0, 400),
                PaddingY = (double)Math.Clamp(BoxPaddingY, 0, 400),
                FrameWidth = (double)Math.Clamp(FrameWidth, 0, 40),
                CornerRadius = (double)Math.Clamp(CornerRadius, 0, 200),
                FrameColor = ToAss(FrameColor),
            },
            Outline = (double)Math.Clamp(Outline, 0, 50),
            Shadow = (double)Math.Clamp(Shadow, 0, 50),
            Alignment = Math.Clamp(Alignment, 1, 9),
            MarginV = (int)Math.Max(0, MarginV),
            MarginL = (int)Math.Max(0, MarginL),
            MarginR = (int)Math.Max(0, MarginR),
        };

        var presentation = project.Presentation;
        project.Presentation = presentation with
        {
            UpperCase = UpperCase,
            FadeInMs = (int)Math.Clamp(FadeIn, 0, 5000),
            FadeOutMs = (int)Math.Clamp(FadeOut, 0, 5000),
            Karaoke = new KaraokeOptions { Mode = SelectedKaraoke?.Mode ?? KaraokeMode.Off, HighlightColor = ToAss(HighlightColor) },
            Bilingual = presentation.Bilingual with
            {
                Enabled = BilingualEnabled && SecondaryTrack is not null,
                SecondaryTrackId = SecondaryTrack?.Id,
                Gap = (int)Math.Clamp(Gap, 0, 300),
                SecondaryStyle = presentation.Bilingual.SecondaryStyle with
                {
                    FontName = string.IsNullOrWhiteSpace(SecondaryFontName) ? FontName.Trim() : SecondaryFontName.Trim(),
                    FontSize = (double)Math.Clamp(SecondaryFontSize, 8, 300),
                    PrimaryColor = ToAss(SecondaryColor),
                },
            },
        };

        MarkAppliedCard();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after a font file was copied into the project's fonts folder.</summary>
    public event EventHandler? FontAdded;

    private void RefreshFonts() => Fonts = _fonts?.Families(ProjectFontDirectory) ?? [];

    private void UpdateFontWarning()
    {
        if (_fonts is null || string.IsNullOrWhiteSpace(FontName))
        {
            FontWarning = null;
            return;
        }

        var status = _fonts.Check(FontName, ProjectFontDirectory);
        var korean = IsKorean(_activeTrack?.Language);
        FontWarning = status switch
        {
            { Source: FontSource.Missing } => "Bu font bu bilgisayarda yok; önizleme ve video başka bir font kullanır. \"Font dosyası ekle\" ile projeye ekleyebilirsiniz.",
            { HasHangul: false } when korean => HangulWarning(),
            { HasTurkishLetters: false } => "Bu fontta bazı Türkçe harfler (ğ, ş, ı, İ…) yok; o harfler başka bir fontla çizilir.",
            { Source: FontSource.System } when !FontCatalog.CrossPlatformFamilies.Contains(FontName.Trim())
                => "Bu font her bilgisayarda (ör. Mac'te) olmayabilir; projeyi taşıyacaksanız font dosyasını projeye ekleyin.",
            _ => null,
        };
        IsFontWarningSevere = status.Source == FontSource.Missing || (korean ? !status.HasHangul : !status.HasTurkishLetters);
    }

    private void UpdateSecondaryFontWarning()
    {
        if (_fonts is null || !IsKorean(SecondaryTrack?.Language))
        {
            SecondaryFontWarning = null;
            return;
        }

        var name = string.IsNullOrWhiteSpace(SecondaryFontName) ? FontName : SecondaryFontName;
        SecondaryFontWarning = _fonts.Check(name, ProjectFontDirectory).HasHangul ? null : HangulWarning();
    }

    private string HangulWarning()
        => _fonts?.SuggestHangulFamily(ProjectFontDirectory) is { } suggestion
            ? $"Bu fontta Korece (Hangul) harfleri yok; Korece satırlar kutu (□) olarak görünebilir. Öneri: {suggestion}."
            : "Bu fontta Korece (Hangul) harfleri yok ve bilgisayarda Korece font bulunamadı. Noto Sans KR'yi indirip \"Font dosyası ekle\" ile projeye ekleyin.";

    private static bool IsKorean(string? language) => language is not null && language.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial bool IsFontWarningSevere { get; private set; }

    /// <summary>Copies a font file into the project's <c>fonts</c> folder and selects it.</summary>
    [RelayCommand]
    private async Task AddFont()
    {
        if (_files is null || _dialogs is null || _fonts is null || _project is null)
        {
            return;
        }

        if (ProjectFontDirectory is not { } directory)
        {
            await _dialogs.ShowMessageAsync("Önce projeyi kaydedin", "Font dosyaları projenin yanındaki \"fonts\" klasörüne kopyalanır; bunun için proje bir kez kaydedilmiş olmalı.");
            return;
        }

        if (await _files.PickFontAsync() is not { } path)
        {
            return;
        }

        var families = _fonts.FamiliesIn(path);
        if (families.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Font eklenemedi", "Bu dosya okunabilir bir TrueType/OpenType font değil.");
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, Path.GetFileName(path));
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(path, target, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Font eklenemedi", ex.Message);
            return;
        }

        RefreshFonts();
        FontAdded?.Invoke(this, EventArgs.Empty);
        FontName = families[0];
    }

    private void UpdateInfo()
    {
        var cues = _activeTrack?.Cues ?? [];
        var timed = cues.Count(c => c.Words.Count > 0);
        KaraokeInfo = cues.Count == 0
            ? null
            : timed == 0
                ? "Bu izde kelime zamanı yok (ör. çeviri ya da içe aktarılan altyazı); karaoke uygulanmaz."
                : $"{cues.Count} satırın {timed} tanesinde kelime zamanı var; diğerleri düz gösterilir.";
        BilingualInfo = SecondaryTracks.Count == 0
            ? "İki dilli mod için projede ikinci bir iz gerekir (ör. çeviri ya da içe aktarılan İngilizce altyazı)."
            : null;
    }

    // ---------------------------------------------------------------- gallery

    /// <summary>Raised when templates were added, imported or deleted (the gallery needs new thumbnails).</summary>
    public event EventHandler? GalleryChanged;

    /// <summary>One card per template, with a thumbnail of the real look (rendered by libass through FFmpeg).</summary>
    public ObservableCollection<TemplateCard> Gallery { get; } = [];

    private CancellationTokenSource? _galleryCts;

    /// <summary>
    /// Renders the thumbnails one after another over <paramref name="scene"/> (cached, so a second call
    /// with the same frame and text is instant). A new call cancels the one still running.
    /// </summary>
    public async Task RefreshGalleryAsync(ThumbnailScene scene)
    {
        if (_thumbnails is null)
        {
            return;
        }

        if (_galleryCts is { } previous)
        {
            await previous.CancelAsync();
        }

        using var cts = new CancellationTokenSource();
        _galleryCts = cts;
        try
        {
            foreach (var card in Gallery.ToList())
            {
                var png = await Task.Run(() => _thumbnails.RenderAsync(card.Template, scene, cts.Token), cts.Token);
                if (png is not null)
                {
                    card.SetThumbnail(png);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // a newer refresh took over
        }
        finally
        {
            if (_galleryCts == cts)
            {
                _galleryCts = null;
            }
        }
    }

    /// <summary>A click on a card applies that template (Ctrl+Z brings the previous look back).</summary>
    [RelayCommand]
    private void ApplyCard(TemplateCard card)
    {
        SelectedTemplate = card.Template;
        ApplyTemplate();
    }

    /// <summary>A card is marked when the project looks exactly like its template (style and presentation).</summary>
    private void MarkAppliedCard()
    {
        foreach (var card in Gallery)
        {
            card.IsSelected = _project is { } project && StyleTemplates.LooksLike(project, card.Template);
        }
    }

    // ---------------------------------------------------------------- commands

    private bool HasTemplate => SelectedTemplate is not null && HasProject;

    [RelayCommand(CanExecute = nameof(HasTemplate))]
    private void ApplyTemplate()
    {
        if (_project is null || SelectedTemplate is not { } template)
        {
            return;
        }

        StyleTemplates.Apply(template, _project);
        Load(_project, _activeTrack, _projectPath);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task SaveTemplate()
    {
        if (_project is null || _dialogs is null || _templates is null)
        {
            return;
        }

        var name = await _dialogs.AskTextAsync("Şablon olarak kaydet", "Şablon adı (tüm projelerde kullanılabilir):", SelectedTemplate is { BuiltIn: false } t ? t.Name : "Benim stilim", "Kaydet");
        if (name is null)
        {
            return;
        }

        if (StyleTemplates.BuiltIn.Any(b => string.Equals(b.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            await _dialogs.ShowMessageAsync("Şablon kaydedilemedi", $"\"{name}\" hazır bir şablonun adı; başka bir ad seçin.");
            return;
        }

        var template = StyleTemplates.FromProject(_project, name);
        _templates.Save(template);
        RefreshTemplates(template.Name);
    }

    private bool CanDeleteTemplate => SelectedTemplate is { BuiltIn: false };

    [RelayCommand(CanExecute = nameof(CanDeleteTemplate))]
    private async Task DeleteTemplate()
    {
        if (_templates is null || _dialogs is null || SelectedTemplate is not { BuiltIn: false } template)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync("Şablonu sil", $"\"{template.Name}\" şablonu silinecek.", "Sil"))
        {
            _templates.Delete(template.Name);
            RefreshTemplates();
        }
    }

    [RelayCommand]
    private async Task ImportTemplate()
    {
        if (_files is null || _templates is null || _dialogs is null || await _files.PickStyleTemplateAsync() is not { } path)
        {
            return;
        }

        try
        {
            var template = StyleTemplates.Deserialize(await File.ReadAllTextAsync(path));
            if (StyleTemplates.BuiltIn.Any(b => string.Equals(b.Name, template.Name, StringComparison.CurrentCultureIgnoreCase)))
            {
                template = template with { Name = template.Name + " (içe aktarılan)" };
            }

            _templates.Save(template);
            RefreshTemplates(template.Name);
        }
        catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Şablon içe aktarılamadı", ex.Message);
        }
    }

    private bool CanExportTemplate => SelectedTemplate is not null;

    [RelayCommand(CanExecute = nameof(CanExportTemplate))]
    private async Task ExportTemplate()
    {
        if (_files is null || _dialogs is null || SelectedTemplate is not { } template || await _files.SaveStyleTemplateAsync(template.Name) is not { } path)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, StyleTemplates.Serialize(template));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Şablon dışa aktarılamadı", ex.Message);
        }
    }

    private void RefreshTemplates(string? select = null)
    {
        var previous = select ?? SelectedTemplate?.Name;
        Templates.Clear();
        foreach (var template in StyleTemplates.BuiltIn.Concat(_templates?.UserTemplates ?? []))
        {
            Templates.Add(template);
        }

        // Cards keep their thumbnail when the template itself did not change.
        var old = Gallery.ToDictionary(c => c.Template.Name, StringComparer.Ordinal);
        Gallery.Clear();
        foreach (var template in Templates)
        {
            Gallery.Add(old.TryGetValue(template.Name, out var card) && card.Template == template ? card : new TemplateCard(template));
        }

        GalleryChanged?.Invoke(this, EventArgs.Empty);

        SelectedTemplate = Templates.FirstOrDefault(t => t.Name == previous) ?? Templates.FirstOrDefault();
        MarkAppliedCard();
    }

    partial void OnHasProjectChanged(bool value) => ApplyTemplateCommand.NotifyCanExecuteChanged();

    internal static Color ToColor(AssColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    internal static AssColor ToAss(Color c) => new(c.R, c.G, c.B, c.A);
}
