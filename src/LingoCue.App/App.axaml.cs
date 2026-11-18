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

                desktop.MainWindow = window;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
