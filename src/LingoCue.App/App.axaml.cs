using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LingoCue.App.ViewModels;
using LingoCue.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LingoCue.App;

public partial class App : Application
{
    private readonly IServiceProvider? _services;

    // Used by the XAML previewer.
    public App()
        : this(services: null)
    {
    }

    internal App(IServiceProvider? services)
    {
        _services = services;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_services is null)
            {
                desktop.MainWindow = new MainWindow { DataContext = MainViewModel.CreateDesignInstance() };
            }
            else
            {
                var window = _services.GetRequiredService<MainWindow>();
                var startup = StartupArguments.Parse(desktop.Args);
                if (startup.FilePath is not null && window.DataContext is MainViewModel vm)
                {
                    window.Opened += async (_, _) => await vm.OpenFileAsync(startup.FilePath);
                }

                if (window.DataContext is MainViewModel main)
                {
                    // Errors no handler caught: logged, shown with a diagnostics offer; the app keeps
                    // running so unsaved work can be saved. One dialog at a time.
                    var reporting = false;
                    Avalonia.Threading.Dispatcher.UIThread.UnhandledException += async (_, e) =>
                    {
                        e.Handled = true;
                        if (reporting)
                        {
                            return;
                        }

                        reporting = true;
                        try
                        {
                            await main.ReportUnexpectedErrorAsync(e.Exception);
                        }
                        finally
                        {
                            reporting = false;
                        }
                    };

                    // A few seconds after start-up, so the check never slows the first window down.
                    window.Opened += async (_, _) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(4));
                        await main.CheckForUpdatesOnStartupAsync();
                    };
                }

                desktop.MainWindow = window;

                // Raised before macOS terminates the process (see Program.LogStopped).
                desktop.Exit += (_, _) => Program.LogStopped();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
