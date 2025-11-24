using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Core.ErrorHandling;

/// <summary>
/// Coordinates error recovery strategies based on error categories.
/// </summary>
/// <remarks>
/// <para>
/// ErrorRecoveryCoordinator implements category-specific recovery strategies:
/// </para>
/// <list type="bullet">
/// <item><description><b>Network Errors:</b> Retry with exponential backoff, mark peer temporarily unavailable</description></item>
/// <item><description><b>Protocol Errors:</b> Disconnect immediately, blacklist peer temporarily</description></item>
/// <item><description><b>Data Integrity:</b> Discard piece, increment failure count, request from different peer</description></item>
/// <item><description><b>Storage Errors:</b> Pause torrent, notify user, attempt recovery</description></item>
/// <item><description><b>Resource Errors:</b> Implement backpressure, close least useful connections</description></item>
/// </list>
/// 
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// var coordinator = new ErrorRecoveryCoordinator(logger);
/// 
/// var result = await SomeOperation();
/// if (result.IsFailure)
/// {
///     var action = coordinator.DetermineRecoveryAction(result.Error);
///     
///     switch (action)
///     {
///         case RecoveryAction.Retry:
///             // Retry the operation
///             break;
///         case RecoveryAction.Disconnect:
///             // Disconnect from peer
///             break;
///         case RecoveryAction.RequestFromDifferentPeer:
///             // Try different peer
///             break;
///         // ... handle other actions
///     }
/// }
/// </code>
/// </remarks>
public class ErrorRecoveryCoordinator
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorRecoveryCoordinator"/> class.
    /// </summary>
    /// <param name="logger">Logger for recovery events.</param>
    public ErrorRecoveryCoordinator(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Determines the appropriate recovery action for an error.
    /// </summary>
    /// <param name="error">The error to handle.</param>
    /// <returns>The recommended recovery action.</returns>
    public RecoveryAction DetermineRecoveryAction(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _logger.LogDebug(
            "Determining recovery action for error: category={Category}, retryable={IsRetryable}, message={Message}",
            error.Category,
            error.IsRetryable,
            error.Message);

        var action = error.Category switch
        {
            ErrorCategory.Network => HandleNetworkError(error),
            ErrorCategory.Protocol => HandleProtocolError(error),
            ErrorCategory.DataIntegrity => HandleDataIntegrityError(error),
            ErrorCategory.Storage => HandleStorageError(error),
            ErrorCategory.Resource => HandleResourceError(error),
            _ => RecoveryAction.Fail
        };

        _logger.LogInformation(
            "Recovery action determined: {Action} for error category {Category}",
            action,
            error.Category);

        return action;
    }

    /// <summary>
    /// Handles network errors.
    /// </summary>
    private RecoveryAction HandleNetworkError(Error error)
    {
        // Network errors are typically retryable
        if (error.IsRetryable)
        {
            // Check if it's a timeout
            if (error.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                error.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Network timeout detected, will retry with backoff");
                return RecoveryAction.RetryWithBackoff;
            }

            // Connection refused or reset
            if (error.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
                error.Message.Contains("reset", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Connection refused/reset, will mark peer unavailable and retry");
                return RecoveryAction.MarkPeerUnavailableAndRetry;
            }

            // Generic network error
            return RecoveryAction.RetryWithBackoff;
        }

        // Non-retryable network error
        return RecoveryAction.Fail;
    }

    /// <summary>
    /// Handles protocol errors.
    /// </summary>
    private RecoveryAction HandleProtocolError(Error error)
    {
        // Protocol errors indicate a misbehaving peer
        _logger.LogWarning("Protocol error detected, will disconnect and blacklist peer");
        return RecoveryAction.DisconnectAndBlacklist;
    }

    /// <summary>
    /// Handles data integrity errors.
    /// </summary>
    private RecoveryAction HandleDataIntegrityError(Error error)
    {
        // Data integrity errors mean we got corrupted data
        _logger.LogWarning("Data integrity error detected, will request from different peer");
        return RecoveryAction.RequestFromDifferentPeer;
    }

    /// <summary>
    /// Handles storage errors.
    /// </summary>
    private RecoveryAction HandleStorageError(Error error)
    {
        // Check if it's a disk full error
        if (error.Message.Contains("disk", StringComparison.OrdinalIgnoreCase) &&
            error.Message.Contains("full", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Disk full error detected, will pause torrent");
            return RecoveryAction.PauseTorrent;
        }

        // Check if it's a permission error
        if (error.Message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Permission error detected, will pause torrent");
            return RecoveryAction.PauseTorrent;
        }

        // Generic I/O error - might be transient
        if (error.IsRetryable)
        {
            _logger.LogWarning("Transient storage error detected, will retry");
            return RecoveryAction.RetryWithBackoff;
        }

        // Non-retryable storage error
        return RecoveryAction.PauseTorrent;
    }

    /// <summary>
    /// Handles resource exhaustion errors.
    /// </summary>
    private RecoveryAction HandleResourceError(Error error)
    {
        // Check if it's a connection limit error
        if (error.Message.Contains("connection limit", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("maximum connection", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Connection limit reached, will close least useful connections");
            return RecoveryAction.CloseLeastUsefulConnections;
        }

        // Check if it's a memory error
        if (error.Message.Contains("memory", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Memory pressure detected, will implement backpressure");
            return RecoveryAction.ImplementBackpressure;
        }

        // Generic resource error
        return RecoveryAction.WaitAndRetry;
    }

    /// <summary>
    /// Logs an error with appropriate context.
    /// </summary>
    /// <param name="error">The error to log.</param>
    /// <param name="context">Additional context information.</param>
    public void LogError(Error error, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var logLevel = error.Category switch
        {
            ErrorCategory.Protocol => LogLevel.Warning,
            ErrorCategory.Storage => LogLevel.Error,
            ErrorCategory.Resource => LogLevel.Warning,
            _ => LogLevel.Information
        };

        var message = context != null
            ? $"{context}: [{error.Category}] {error.Message}"
            : $"[{error.Category}] {error.Message}";

        _logger.Log(logLevel, error.InnerException, message);
    }
}

/// <summary>
/// Defines recovery actions that can be taken in response to errors.
/// </summary>
public enum RecoveryAction
{
    /// <summary>
    /// Retry the operation immediately.
    /// </summary>
    Retry,

    /// <summary>
    /// Retry the operation with exponential backoff.
    /// </summary>
    RetryWithBackoff,

    /// <summary>
    /// Mark the peer as temporarily unavailable and retry with a different peer.
    /// </summary>
    MarkPeerUnavailableAndRetry,

    /// <summary>
    /// Disconnect from the peer immediately.
    /// </summary>
    Disconnect,

    /// <summary>
    /// Disconnect from the peer and add to blacklist temporarily.
    /// </summary>
    DisconnectAndBlacklist,

    /// <summary>
    /// Request the piece from a different peer.
    /// </summary>
    RequestFromDifferentPeer,

    /// <summary>
    /// Pause the torrent and notify the user.
    /// </summary>
    PauseTorrent,

    /// <summary>
    /// Close the least useful connections to free resources.
    /// </summary>
    CloseLeastUsefulConnections,

    /// <summary>
    /// Implement backpressure to reduce resource usage.
    /// </summary>
    ImplementBackpressure,

    /// <summary>
    /// Wait for a period and then retry.
    /// </summary>
    WaitAndRetry,

    /// <summary>
    /// Fail the operation without recovery.
    /// </summary>
    Fail
}
