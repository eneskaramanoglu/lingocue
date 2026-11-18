using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.App.Services;
using LingoCue.Core;
using LingoCue.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LingoCue.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IThemeService? _themeService;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(IThemeService themeService, IPlatformInfo platform, ILogger<MainViewModel> logger)
    {
        _themeService = themeService;
        _logger = logger;
        PlatformText = platform.RuntimeIdentifier;
        SelectedTheme = themeService.Current;
    }

    // Designer / previewer constructor.
    private MainViewModel()
    {
        _logger = NullLogger<MainViewModel>.Instance;
        PlatformText = "design";
    }

    public static MainViewModel CreateDesignInstance() => new();

    public string Title { get; } = AppInfo.Name;

    public string VersionText { get; } = $"v{AppInfo.Version}";

    public string PlatformText { get; }

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    public partial AppTheme SelectedTheme { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Hazır";

    // Long-running job state (wired up from M3 on).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelJobCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    public bool IsIdle => !IsBusy;

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        _themeService?.Apply(value);
        _logger.LogDebug("Theme changed to {Theme}", value);
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelJob()
    {
        // Each job will own a CancellationTokenSource; nothing runs yet in M0.
        StatusText = "İptal edildi";
        IsBusy = false;
    }
}
