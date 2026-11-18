using System.Globalization;
using LingoCue.Core.Platform;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace LingoCue.App;

internal static class LoggingSetup
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static Serilog.Core.Logger CreateBootstrapLogger(IPlatformInfo platform)
        => Apply(new LoggerConfiguration().MinimumLevel.Information(), platform).CreateLogger();

    public static void Configure(LoggerConfiguration configuration, IConfiguration appConfiguration, IPlatformInfo platform)
        => Apply(configuration.ReadFrom.Configuration(appConfiguration), platform);

    private static LoggerConfiguration Apply(LoggerConfiguration configuration, IPlatformInfo platform)
    {
        var logFile = Path.Combine(platform.LogDirectory, "lingocue-.log");

        configuration
            .Enrich.FromLogContext()
            .WriteTo.File(
                logFile,
                outputTemplate: OutputTemplate,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true);
#if DEBUG
        configuration.WriteTo.Console(
            restrictedToMinimumLevel: LogEventLevel.Debug,
            outputTemplate: OutputTemplate,
            formatProvider: CultureInfo.InvariantCulture);
#endif
        return configuration;
    }
}
