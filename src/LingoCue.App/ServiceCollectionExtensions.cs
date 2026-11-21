using LingoCue.App.Playback;
using LingoCue.App.Services;
using LingoCue.App.ViewModels;
using LingoCue.App.Views;
using LingoCue.Core.Fonts;
using LingoCue.Core.Platform;
using LingoCue.Core.Projects;
using LingoCue.Core.Settings;
using LingoCue.Infrastructure.Platform;
using LingoCue.Infrastructure.Security;
using LingoCue.Infrastructure.Settings;
using LingoCue.Llm.Providers;
using LingoCue.Media.Audio;
using LingoCue.Media.Probing;
using LingoCue.Media.Processes;
using LingoCue.Media.Rendering;
using LingoCue.Media.Tools;
using Microsoft.Extensions.Configuration;
using LingoCue.Transcription;
using LingoCue.Transcription.Gemini;
using LingoCue.Transcription.Models;
using LingoCue.Transcription.Whisper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LingoCue.App;

/// <summary>Composition root: every service the app uses is registered here.</summary>
internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLingoCue(this IServiceCollection services, IPlatformInfo platform, IConfiguration configuration)
    {
        services.AddSingleton(platform);
        services.AddSingleton<IHardwareInfoProvider, LingoCue.Infrastructure.Platform.HardwareInfoProvider>();
        services.AddSingleton<IFileManager, FileManager>();
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
        services.AddSingleton<IAudioExtractor, AudioExtractor>();
        services.AddSingleton<IVideoRenderer, VideoRenderer>();
        services.AddSingleton<IWaveformService, WaveformService>();

        // Secrets
        services.AddSingleton(_ => SecretStoreFactory.Create());
        services.AddSingleton<ApiKeyProvider>();

        // Transcription
        var http = new HttpClient { Timeout = TimeSpan.FromHours(2) }; // model downloads and long uploads
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"LingoCue/{Core.AppInfo.Version}");
        services.AddSingleton(sp => new ModelDownloader(http, sp.GetRequiredService<ILogger<ModelDownloader>>()));
        services.AddSingleton(sp => new GeminiTranscribeClient(http, sp.GetRequiredService<ILogger<GeminiTranscribeClient>>()));
        services.AddSingleton<WhisperNetEngine>();
        services.AddSingleton<ITranscriptionEngine>(sp => sp.GetRequiredService<WhisperNetEngine>());
        services.AddSingleton<ITranscriptionEngine>(sp => new GeminiTranscribeEngine(
            sp.GetRequiredService<IAudioExtractor>(),
            sp.GetRequiredService<GeminiTranscribeClient>(),
            sp.GetRequiredService<ApiKeyProvider>().GetGeminiKey,
            sp.GetRequiredService<ILogger<GeminiTranscribeEngine>>()));

        // Settings and language models
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IStyleTemplateStore, JsonStyleTemplateStore>();
        services.AddSingleton<IFontCatalog, FontCatalog>();
        services.AddSingleton<ILlmProvider>(sp => new GeminiProvider(sp.GetRequiredService<ApiKeyProvider>().GetGeminiKey));
        // Local models: the per-request timeout of the pipeline applies; the client itself never gives up first.
        services.AddSingleton<ILlmProvider>(sp => new OllamaProvider(http, () => sp.GetRequiredService<ISettingsStore>().Current.Llm.OllamaUrl));
        services.AddSingleton(sp => new ModelResolver(sp.GetServices<ILlmProvider>(), () => sp.GetRequiredService<ISettingsStore>().Current.Llm));

        // Playback
        services.AddSingleton<MpvVideoPlayer>();
        services.AddSingleton<IVideoPlayer>(sp => sp.GetRequiredService<MpvVideoPlayer>());

        services.AddSingleton<SubtitlePreviewService>();

        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<SubtitleEditorViewModel>();
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<QualityViewModel>();
        services.AddSingleton<StyleEditorViewModel>();
        services.AddSingleton<RenderQueueViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
