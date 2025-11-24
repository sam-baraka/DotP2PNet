using Dotp2pNet.Core.ErrorHandling;
using Dotp2pNet.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Networking.Connections;

/// <summary>
/// Provides retry logic for peer connections with exponential backoff.
/// </summary>
/// <remarks>
/// <para>
/// ConnectionRetryHelper wraps connection attempts with automatic retry logic:
/// </para>
/// <list type="bullet">
/// <item><description><b>Exponential Backoff:</b> Delays increase exponentially (1s, 2s, 4s)</description></item>
/// <item><description><b>Max 3 Attempts:</b> Gives up after 3 failed attempts</description></item>
/// <item><description><b>Error Categorization:</b> Converts exceptions to structured errors</description></item>
/// <item><description><b>Logging:</b> Logs each retry attempt for observability</description></item>
/// </list>
/// 
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// var helper = new ConnectionRetryHelper(connectionManager, logger);
/// 
/// var result = await helper.ConnectWithRetryAsync(
///     ipAddress: "192.168.1.50",
///     port: 6881,
///     ct: cancellationToken);
/// 
/// if (result.IsSuccess)
/// {
///     var connection = result.Value;
///     // Use connection
/// }
/// else
/// {
///     // Handle error
///     logger.LogError("Failed to connect: {Error}", result.Error);
/// }
/// </code>
/// </remarks>
public class ConnectionRetryHelper
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger _logger;
    private readonly RetryPolicy _retryPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionRetryHelper"/> class.
    /// </summary>
    /// <param name="connectionManager">The connection manager.</param>
    /// <param name="logger">Logger for retry events.</param>
    public ConnectionRetryHelper(
        IConnectionManager connectionManager,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(connectionManager);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionManager = connectionManager;
        _logger = logger;
        
        // Create retry policy: max 3 attempts, 1 second initial delay
        _retryPolicy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 1000);
    }

    /// <summary>
    /// Attempts to connect to a peer with automatic retry logic.
    /// </summary>
    /// <param name="ipAddress">The IP address of the peer.</param>
    /// <param name="port">The port number of the peer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the connection or an error.</returns>
    /// <remarks>
    /// This method will retry up to 3 times with exponential backoff if the connection fails
    /// with a retryable error (network errors). Non-retryable errors (protocol errors, resource
    /// exhaustion) are returned immediately without retry.
    /// </remarks>
    public async Task<Result<IPeerConnection>> ConnectWithRetryAsync(
        string ipAddress,
        int port,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        _logger.LogInformation(
            "Attempting to connect to {IpAddress}:{Port} with retry policy (max {MaxAttempts} attempts)",
            ipAddress,
            port,
            _retryPolicy.MaxAttempts);

        int attemptNumber = 0;

        var result = await _retryPolicy.ExecuteAsync(async () =>
        {
            attemptNumber++;
            
            try
            {
                _logger.LogDebug(
                    "Connection attempt {Attempt}/{MaxAttempts} to {IpAddress}:{Port}",
                    attemptNumber,
                    _retryPolicy.MaxAttempts,
                    ipAddress,
                    port);

                var connection = await _connectionManager.ConnectToPeerAsync(
                    ipAddress,
                    port,
                    ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Successfully connected to {IpAddress}:{Port} on attempt {Attempt}",
                    ipAddress,
                    port,
                    attemptNumber);

                return Result<IPeerConnection>.Success(connection);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("maximum connection limit"))
            {
                // Resource exhaustion - retryable but with different strategy
                _logger.LogWarning(
                    "Connection attempt {Attempt} to {IpAddress}:{Port} failed: {Error}",
                    attemptNumber,
                    ipAddress,
                    port,
                    ex.Message);

                return Result<IPeerConnection>.Failure(
                    Error.Resource(
                        $"Maximum connection limit reached when connecting to {ipAddress}:{port}",
                        ex));
            }
            catch (TimeoutException ex)
            {
                // Network timeout - retryable
                _logger.LogWarning(
                    "Connection attempt {Attempt} to {IpAddress}:{Port} timed out",
                    attemptNumber,
                    ipAddress,
                    port);

                return Result<IPeerConnection>.Failure(
                    Error.Network(
                        $"Connection to {ipAddress}:{port} timed out",
                        isRetryable: true,
                        ex));
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // Network error - retryable
                _logger.LogWarning(
                    ex,
                    "Connection attempt {Attempt} to {IpAddress}:{Port} failed with socket error",
                    attemptNumber,
                    ipAddress,
                    port);

                return Result<IPeerConnection>.Failure(
                    Error.Network(
                        $"Socket error connecting to {ipAddress}:{port}: {ex.Message}",
                        isRetryable: true,
                        ex));
            }
            catch (Exception ex)
            {
                // Unknown error - not retryable by default
                _logger.LogError(
                    ex,
                    "Connection attempt {Attempt} to {IpAddress}:{Port} failed with unexpected error",
                    attemptNumber,
                    ipAddress,
                    port);

                return Result<IPeerConnection>.Failure(
                    Error.Network(
                        $"Unexpected error connecting to {ipAddress}:{port}: {ex.Message}",
                        isRetryable: false,
                        ex));
            }
        }, ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogError(
                "Failed to connect to {IpAddress}:{Port} after {Attempts} attempts: {Error}",
                ipAddress,
                port,
                attemptNumber,
                result.Error);
        }

        return result;
    }
}
