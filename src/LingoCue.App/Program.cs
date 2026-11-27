using System.Runtime.InteropServices;
using Avalonia;
using HanumanInstitute.LibMpv.Core;
using LingoCue.Core;
using LingoCue.Core.Platform;
using LingoCue.Infrastructure.Platform;
using Microsoft.Extensions.Hosting;
using Serilog;
using Velopack;

namespace LingoCue.App;

internal static class Program
{
    // Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code
    // before AppMain is called: things aren't initialized yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: during install, update and uninstall Velopack starts the app with hook
        // arguments and exits inside Run(). A copy that was not installed returns at once.
        VelopackApp.Build().SetArgs(args).Run();

        var platform = new PlatformInfo();
        Log.Logger = LoggingSetup.CreateBootstrapLogger(platform);

        // On macOS, Quit (⌘Q, Dock) ends the process from native code: StartWithClassicDesktopLifetime
        // never returns and the finally block below does not run. Errors thrown from native callbacks
        // (e.g. libmpv failing to load while the first window is built) also skip the catch.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception, shutting down");
            LogStopped();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => LogStopped();

        try
        {
            Log.Information("{App} {Version} starting on {Rid}", AppInfo.Name, AppInfo.Version, platform.RuntimeIdentifier);

            using var host = CreateHost(args, platform);
            return BuildAvaloniaApp(host.Services, platform).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception, shutting down");
            return 1;
        }
        finally
        {
            LogStopped();
        }
    }

    /// <summary>
    /// Loads the bundled libmpv once before Avalonia starts, only to log why it fails: the video view
    /// loads it later from a native render callback, where an error ends the process without a log line.
    /// </summary>
    private static void ProbeBundledLibMpv(PlatformInfo platform)
    {
        var path = Path.Combine(platform.NativeLibraryDirectory, platform.Platform == OsPlatform.Windows ? "libmpv-2.dll" : "libmpv.2.dylib");
        if (!File.Exists(path))
        {
            return; // Development build: the library comes from the system (e.g. Homebrew).
        }

        try
        {
            NativeLibrary.Free(NativeLibrary.Load(path));
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            Log.Fatal(ex, "The bundled libmpv at {Path} does not load; video playback will fail", path);
        }
    }

    private static int s_stopped;

    /// <summary>Writes the "stopped" line and flushes the log, once, from whichever exit path runs first.</summary>
    internal static void LogStopped()
    {
        if (Interlocked.Exchange(ref s_stopped, 1) != 0)
        {
            return;
        }

        Log.Information("{App} stopped", AppInfo.Name);
        Log.CloseAndFlush();
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
        builder.Services.AddLingoCue(platform, builder.Configuration);

        return builder.Build();
    }

    // Avalonia configuration; also used by the visual designer (which runs without services).
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(services: null, new PlatformInfo());

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? services, PlatformInfo platform)
    {
        // libmpv is loaded from here (next to the exe on Windows, Contents/Frameworks in the .app);
        // on macOS the library also searches /opt/homebrew/lib for development builds.
        MpvApi.RootPath = platform.NativeLibraryDirectory;
        ProbeBundledLibMpv(platform);

        var builder = AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

        // The mpv video view renders through OpenGL: ANGLE/EGL on Windows, OpenGL (not Metal) on macOS.
        // Software rendering is the fallback when the GPU path is unavailable (e.g. remote desktop).
        return platform.Platform switch
        {
            OsPlatform.Windows => builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            }),
            OsPlatform.MacOS => builder.With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software],
            }),
            _ => builder,
        };
    }
}
