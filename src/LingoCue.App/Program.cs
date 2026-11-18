using Avalonia;
using LingoCue.Core;
using LingoCue.Core.Platform;
using LingoCue.Infrastructure.Platform;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LingoCue.App;

internal static class Program
{
    // Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code
    // before AppMain is called: things aren't initialized yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        var platform = new PlatformInfo();
        Log.Logger = LoggingSetup.CreateBootstrapLogger(platform);

        try
        {
            Log.Information("{App} {Version} starting on {Rid}", AppInfo.Name, AppInfo.Version, platform.RuntimeIdentifier);

            using var host = CreateHost(args, platform);
            return BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception, shutting down");
            return 1;
        }
        finally
        {
            Log.Information("{App} stopped", AppInfo.Name);
            Log.CloseAndFlush();
        }
    }

    internal static IHost CreateHost(string[] args, IPlatformInfo platform)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddSerilog((_, configuration) =>
            LoggingSetup.Configure(configuration, builder.Configuration, platform));
        builder.Services.AddLingoCue(platform);

        return builder.Build();
    }

    // Avalonia configuration; also used by the visual designer (which runs without services).
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(services: null);

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? services)
        => AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
