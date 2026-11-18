using LingoCue.App.Playback;
using LingoCue.App.Services;
using LingoCue.App.ViewModels;
using LingoCue.App.Views;
using LingoCue.Core.Platform;
using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LingoCue.App;

/// <summary>Composition root: every service the app uses is registered here.</summary>
internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLingoCue(this IServiceCollection services, IPlatformInfo platform, IConfiguration configuration)
    {
        services.AddSingleton(platform);
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IDialogService, DialogService>();

        // Media
        var mediaOptions = new MediaToolOptions();
        configuration.GetSection("Media").Bind(mediaOptions);
        services.AddSingleton(mediaOptions);
        services.AddSingleton<IMediaToolLocator, MediaToolLocator>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IMediaProbe, MediaProbe>();

        // Playback
        services.AddSingleton<MpvVideoPlayer>();
        services.AddSingleton<IVideoPlayer>(sp => sp.GetRequiredService<MpvVideoPlayer>());

        services.AddSingleton<SubtitlePreviewService>();

        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<SubtitleEditorViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
