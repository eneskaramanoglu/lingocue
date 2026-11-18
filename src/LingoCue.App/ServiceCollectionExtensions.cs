using LingoCue.App.Services;
using LingoCue.App.ViewModels;
using LingoCue.App.Views;
using LingoCue.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace LingoCue.App;

/// <summary>Composition root: every service the app uses is registered here.</summary>
internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLingoCue(this IServiceCollection services, IPlatformInfo platform)
    {
        services.AddSingleton(platform);
        services.AddSingleton<IThemeService, ThemeService>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
