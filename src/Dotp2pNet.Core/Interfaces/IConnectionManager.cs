namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Manages connections to remote peers in the P2P network.
/// </summary>
/// <remarks>
/// Handles both outgoing connections (connecting to peers) and incoming connections
/// (accepting connections from peers). Maintains a pool of active connections and
/// enforces connection limits.
/// </remarks>
public interface IConnectionManager
{
    /// <summary>
    /// Gets the collection of currently active peer connections.
    /// </summary>
    IReadOnlyCollection<IPeerConnection> ActiveConnections { get; }

    /// <summary>
    /// Gets the maximum number of concurrent connections allowed.
    /// </summary>
    ///  int MaxConnections { get; }

    /// <summary>
    /// Establishes a connection to a remote peer.
    /// </summary>
    /// <param name="ipAddress">The IP address of the peer.</param>
    /// <param name="port">The port number of the peer.</param>
    /// <param name="ct">Cancellation token to cancel the connection attempt.</param>
    /// <returns>The established peer connection.</returns>
    Task<IPeerConnection> ConnectToPeerAsync(string ipAddress, int port, CancellationToken ct);

    /// <summary>
    /// Starts listening for incoming peer connections.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="ct">Cancellation token to stop listening.</param>
    Task StartListeningAsync(int port, CancellationToken ct);

    /// <summary>
    /// Stops listening for incoming connections.
    /// </summary>
    Task StopListeningAsync();

    /// <summary>
    /// Removes and closes a peer connection.
    /// </summary>
    /// <param name="connection">The connection to remove.</param>
    Task RemoveConnectionAsync(IPeerConnection connection);

    /// <summary>
    /// Closes all active connections.
    /// </summary>
    Task CloseAllConnectionsAsync();
}
