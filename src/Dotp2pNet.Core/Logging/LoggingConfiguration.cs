using Serilog;
using Serilog.Events;
using Serilog.Context;

namespace Dotp2pNet.Core.Logging;

/// <summary>
/// Configures Serilog logging for the application with structured logging and correlation IDs.
/// </summary>
/// <remarks>
/// <para>
/// This configuration provides:
/// </para>
/// <list type="bullet">
/// <item><description><b>Structured Logging:</b> All log properties are captured as structured data</description></item>
/// <item><description><b>Correlation IDs:</b> Track related operations across components using CorrelationId</description></item>
/// <item><description><b>Log Enrichment:</b> Automatic enrichment with machine name, process ID, and thread ID</description></item>
/// <item><description><b>Multiple Sinks:</b> Console for development, file for production diagnostics</description></item>
/// <item><description><b>Log Levels:</b> ERROR, WARN, INFO, DEBUG for different verbosity needs</description></item>
/// </list>
/// 
/// <para><b>Correlation IDs:</b></para>
/// <para>
/// Correlation IDs allow tracking a single operation across multiple components and log entries.
/// For example, a download operation might involve tracker announce, DHT lookup, peer connections,
/// and piece transfers. By using the same correlation ID, all these log entries can be linked together.
/// </para>
/// 
/// <para><b>Usage Example:</b></para>
/// <code>
/// // Create a correlation ID for a download operation
/// using (LogContext.PushProperty("CorrelationId", Guid.NewGuid()))
/// using (LogContext.PushProperty("Operation", "Download"))
/// using (LogContext.PushProperty("InfoHash", infoHashHex))
/// {
///     _logger.LogInformation("Starting download");
///     // All logs within this scope will have the same CorrelationId
/// }
/// </code>
/// </remarks>
public static class LoggingConfiguration
{
    /// <summary>
    /// Configures the global Serilog logger with console and file sinks.
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
            // Enrich logs with contextual information
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            // Console sink for development and real-time monitoring
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} " +
                               "{Properties:j}{NewLine}{Exception}"
            )
            // File sink for production diagnostics with structured output
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] " +
                               "[{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                retainedFileCountLimit: 7
            )
            .CreateLogger();

        Log.Information("Logging configured. Log directory: {LogDirectory}, Minimum level: {MinimumLevel}", 
            logDirectory, minimumLevel);
    }

    /// <summary>
    /// Closes and flushes the Serilog logger.
    /// </summary>
    public static void CloseLogging()
    {
        Log.Information("Closing logging system");
        Log.CloseAndFlush();
    }
}

/// <summary>
/// Provides utilities for working with correlation IDs in logs.
/// </summary>
/// <remarks>
/// <para>
/// Correlation IDs are used to track related operations across multiple components.
/// This is essential in distributed systems where a single user action (like downloading a file)
/// triggers operations across multiple layers (tracker, DHT, networking, storage).
/// </para>
/// 
/// <para><b>Best Practices:</b></para>
/// <list type="bullet">
/// <item><description>Create a new correlation ID at the entry point of each major operation</description></item>
/// <item><description>Pass the correlation ID through all related operations</description></item>
/// <item><description>Use using statements to ensure proper scope management</description></item>
/// <item><description>Include operation type and key identifiers (like info hash) for better filtering</description></item>
/// </list>
/// </remarks>
public static class CorrelationContext
{
    /// <summary>
    /// Creates a new correlation context for tracking related operations.
    /// </summary>
    /// <param name="operation">The type of operation (e.g., "Download", "Upload", "TrackerAnnounce").</param>
    /// <param name="additionalProperties">Additional properties to include in the context.</param>
    /// <returns>An IDisposable that should be disposed when the operation completes.</returns>
    /// <example>
    /// <code>
    /// using (CorrelationContext.Create("Download", ("InfoHash", infoHashHex)))
    /// {
    ///     // All logs here will have the same CorrelationId and Operation
    ///     await DownloadFileAsync();
    /// }
    /// </code>
    /// </example>
    public static IDisposable Create(string operation, params (string Key, object Value)[] additionalProperties)
    {
        var correlationId = Guid.NewGuid().ToString();
        var disposables = new List<IDisposable>
        {
            LogContext.PushProperty("CorrelationId", correlationId),
            LogContext.PushProperty("Operation", operation)
        };

        foreach (var (key, value) in additionalProperties)
        {
            disposables.Add(LogContext.PushProperty(key, value));
        }

        return new CompositeDisposable(disposables);
    }

    /// <summary>
    /// Creates a correlation context with an existing correlation ID.
    /// </summary>
    /// <param name="correlationId">The correlation ID to use.</param>
    /// <param name="operation">The type of operation.</param>
    /// <param name="additionalProperties">Additional properties to include in the context.</param>
    /// <returns>An IDisposable that should be disposed when the operation completes.</returns>
    public static IDisposable CreateWithId(string correlationId, string operation, params (string Key, object Value)[] additionalProperties)
    {
        var disposables = new List<IDisposable>
        {
            LogContext.PushProperty("CorrelationId", correlationId),
            LogContext.PushProperty("Operation", operation)
        };

        foreach (var (key, value) in additionalProperties)
        {
            disposables.Add(LogContext.PushProperty(key, value));
        }

        return new CompositeDisposable(disposables);
    }

    private class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _disposables;

        public CompositeDisposable(List<IDisposable> disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }
    }
}
