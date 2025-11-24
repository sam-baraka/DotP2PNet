namespace Dotp2pNet.Core.ErrorHandling;

/// <summary>
/// Categorizes errors by their nature to enable appropriate recovery strategies.
/// </summary>
/// <remarks>
/// <para>
/// Error categories help the system determine how to handle failures:
/// </para>
/// <list type="bullet">
/// <item><description><b>Network:</b> Connection failures, timeouts, socket errors - typically retryable</description></item>
/// <item><description><b>Protocol:</b> Invalid messages, handshake failures - typically not retryable</description></item>
/// <item><description><b>DataIntegrity:</b> Hash mismatches, corrupted data - retry with different peer</description></item>
/// <item><description><b>Storage:</b> Disk full, I/O errors - may require user intervention</description></item>
/// <item><description><b>Resource:</b> Connection limits, memory pressure - wait and retry</description></item>
/// </list>
/// </remarks>
public enum ErrorCategory
{
    /// <summary>
    /// Network-related errors (connection failures, timeouts, socket errors).
    /// </summary>
    /// <remarks>
    /// Recovery strategy: Retry with exponential backoff, mark peer as temporarily unavailable.
    /// </remarks>
    Network,

    /// <summary>
    /// Protocol-related errors (malformed messages, invalid handshake, version mismatch).
    /// </summary>
    /// <remarks>
    /// Recovery strategy: Disconnect immediately, blacklist peer temporarily.
    /// </remarks>
    Protocol,

    /// <summary>
    /// Data integrity errors (hash mismatch, corrupted pieces).
    /// </summary>
    /// <remarks>
    /// Recovery strategy: Discard piece, increment peer failure count, request from different peer.
    /// </remarks>
    DataIntegrity,

    /// <summary>
    /// Storage-related errors (disk full, permission denied, I/O errors).
    /// </summary>
    /// <remarks>
    /// Recovery strategy: Pause torrent, notify user, attempt recovery if possible.
    /// </remarks>
    Storage,

    /// <summary>
    /// Resource exhaustion errors (too many connections, memory pressure).
    /// </summary>
    /// <remarks>
    /// Recovery strategy: Implement backpressure, close least useful connections, wait and retry.
    /// </remarks>
    Resource
}
