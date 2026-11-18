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
            desktop.MainWindow = _services is null
                ? new MainWindow { DataContext = MainViewModel.CreateDesignInstance() }
                : _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
