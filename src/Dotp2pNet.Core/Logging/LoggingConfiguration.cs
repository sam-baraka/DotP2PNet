namespace Dotp2pNet.Core.Logging;

/// <summary>
/// Configures Serilog logging for the application.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    ///   ///es the global Serilog logger with console and file sinks.
    /// </summary>
    /// <param name="logDirectory">Directory where log files will be stored. Defaults to ~/.dotp2pnet/logs/</param>
    /// <param name="minimumLevel">Minimum log level. Defaults to Information.</param>
    public static void ConfigureLogging(string? logDirectory = null, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        logDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotp2pnet",
            "logs"
        );

        // Ensure log directory exists
        Directory.CreateDirectory(logDirectory);

        var logFilePath = Path.Combine(logDirectory, "dotp2pnet-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 7
            )
            .CreateLogger();

        Log.Information("Logging configured. Log directory: {LogDirectory}", logDirectory);
    }

    /// <summary>
    /// Closes and flushes the Serilog logger.
    /// </summary>
    public static void CloseLogging()
    {
        Log.CloseAndFlush();
    }
}
