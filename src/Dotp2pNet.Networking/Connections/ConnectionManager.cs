using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Dotp2pNet.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dotp2pNet.Core.Configuration;

namespace Dotp2pNet.Networking.Connections;

/// <summary>
/// Manages multiple peer connections in the P2P network.
/// </summary>
/// <remarks>
/// <para>
/// ConnectionManager is responsible for:
/// </para>
/// <list type="bullet">
/// <item><description><b>Outgoing Connections:</b> Connecting to remote peers</description></item>
/// <item><description><b>Incoming Connections:</b> Accepting connections from remote peers via TcpListener</description></item>
/// <item><description><b>Connection Pool:</b> Maintaining a collection of active connections</description></item>
/// <item><description><b>Connection Limits:</b> Enforcing maximum connection count</description></item>
/// <item><description><b>Lifecycle Management:</b> Proper cleanup and disposal of connections</description></item>
/// </list>
/// /// 
/// <para><b>Thread Safety:</b></para>
/// <para>
/// - Uses ConcurrentDictionary for thread-safe connection storage
/// - All public methods are thread-safe and can be called concurrently
/// - Connection limit enforcement uses atomic operations
/// </para>
/// 
/// <para><b>Connection Pooling:</b></para>
/// <para>
/// Connections are stored in a dictionary keyed by a unique connection identifier.
/// When the maximum connection limit is reached, new connection attempts are rejected
/// until existing connections are closed.
/// </para>
/// </remarks>
public class ConnectionManager : IConnectionManager, IDisposable
{
    private readonly IMessageFramer _messageFramer;
    private readonly ILogger<ConnectionManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly NetworkConfiguration _config;
    private readonly ConcurrentDictionary<string, IPeerConnection> _connections;
    private readonly SemaphoreSlim _connectionLock;
    
    private TcpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private Task? _acceptTask;
    private bool _disposed;

    /// <summary>
    /// Gets the collection of currently active peer connections.
    /// </summary>
    public IReadOnlyCollection<IPeerConnection> ActiveConnections => 
        _connections.Values.ToList().AsReadOnly();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionManager"/> class.
    /// </summary>
    /// <param name="messageFramer">The message framer for protocol messages.</param>
    /// <param name="config">Network configuration options.</param>
    /// <param name="loggerFactory">Factory for creating loggers.</param>
    ///  /// m name="logger">Logger for connection manager events.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ConnectionManager(
        IMessageFramer messageFramer,
        IOptions<Dotp2pNetConfiguration> config,
        ILoggerFactory loggerFactory,
        ILogger<ConnectionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(messageFramer);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _messageFramer = messageFramer;
        _config = config.Value.Network;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _connections = new ConcurrentDictionary<string, IPeerConnection>();
        _connectionLock = new SemaphoreSlim(1, 1);

        _logger.LogInformation(
            "ConnectionManager initialized with max connections: {MaxConnections}",
            _config.MaxConnections);
    }

    /// <summary>
    /// Establishes a connection to a remote peer.
    /// </summary>
    /// <param name="ipAddress">The IP address of the peer.</param>
    /// <param name="port">The port number of the peer.</param>
    /// <param name="ct">Cancellation token to cancel the connection attempt.</param>
    /// <returns>The established peer connection.</returns>
    /// <exception cref="InvalidOperationException">Thrown when maximum connection limit is reached.</exception>
    /// <exception cref="SocketException">Thrown when connection fails.</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled.</exception>
    /// <remarks>
    /// This method checks the connection limit before attempting to connect.
    /// If the limit is reached, an exception is thrown. Otherwise, it creates
    /// a new PeerConnection and adds it to the active connections pool.
    ///   ///arks>
    public async Task<IPeerConnection> ConnectToPeerAsync(
        string ipAddress,
        int port,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
        }

        // Check connection limit
        if (_connections.Count >= _config.MaxConnections)
        {
            _logger.LogWarning(
                "Cannot connect to {IpAddress}:{Port} - maximum connection limit ({MaxConnections}) reached",
                ipAddress,
                port,
                _config.MaxConnections);
            
            throw new InvalidOperationException(
                $"Maximum connection limit ({_config.MaxConnections}) reached");
        }

        _logger.LogInformation(
            "Connecting to peer at {IpAddress}:{Port} ({CurrentConnections}/{MaxConnections})",
            ipAddress,
            port,
            _connections.Count,
            _config.MaxConnections);

        try
        {
            // Create connection timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.ConnectionTimeoutSeconds));

            // Create peer connection
            var peerLogger = _loggerFactory.CreateLogger<PeerConnection>();
            var connection = await PeerConnection.ConnectAsync(
                ipAddress,
                port,
                _messageFramer,
                peerLogger,
                timeoutCts.Token).ConfigureAwait(false);

            // Generate unique connection ID
            string connectionId = Guid.NewGuid().ToString();

            // Add to connections dictionary
            if (!_connections.TryAdd(connectionId, connection))
            {
                // This should never happen with GUID, but handle it anyway
                await connection.CloseAsync().ConfigureAwait(false);
                connection.Dispose();
                throw new InvalidOperationException("Failed to add connection to pool");
            }

            _logger.LogInformation(
                "Successfully connected to peer at {IpAddress}:{Port}, connection ID: {ConnectionId} ({CurrentConnections}/{MaxConnections})",
                ipAddress,
                port,
                connectionId,
                _connections.Count,
                _config.MaxConnections);

            return connection;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Connection to {IpAddress}:{Port} was cancelled",
                ipAddress,
                port);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Connection to {IpAddress}:{Port} timed out after {Timeout} seconds",
                ipAddress,
                port,
                _config.ConnectionTimeoutSeconds);
            throw new TimeoutException(
                $"Connection to {ipAddress}:{port} timed out after {_config.ConnectionTimeoutSeconds} seconds");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to connect to peer at {IpAddress}:{Port}",
                ipAddress,
                port);
            throw;
        }
    }

    /// <summary>
    /// Starts listening for incoming peer connections.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="ct">Cancellation token to stop listening.</param>
    /// <returns>Task that completes when listening stops.</returns>
    /// <exception cref="InvalidOperationException">Thrown when already listening.</exception>
    /// <exception cref="SocketException">Thrown when unable to bind to port.</exception>
    /// <remarks>
    /// This method starts a TcpListener on the specified port and begins accepting
    /// incoming connections in a background task. Each accepted connection is added
    /// to the connection pool (subject to the maximum connection limit).
    /// </remarks>
    public async Task StartListeningAsync(int port, CancellationToken ct)
    {
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
        }

        await _connectionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_listener != null)
            {
                throw new InvalidOperationException("Already listening for connections");
            }

            _logger.LogInformation("Starting to listen for incoming connections on port {Port}", port);

            // Create and start TCP listener
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            // Create cancellation token source for the accept loop
            _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Start accept loop in background task
            _acceptTask = Task.Run(() => AcceptLoopAsync(_listenerCts.Token), _listenerCts.Token);

            _logger.LogInformation("Now listening for incoming connections on port {Port}", port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start listening on port {Port}", port);
            
            // Cleanup on failure
            _listener?.Stop();
            _listener = null;
            _listenerCts?.Dispose();
            _listenerCts = null;
            
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Stops listening for incoming connections.
    /// </summary>
    /// <returns>Task that completes when listening has stopped.</returns>
    /// <remarks>
    /// This method stops the TcpListener and waits for the accept loop to complete.
    /// Existing connections are not affected.
    /// </remarks>
    public async Task StopListeningAsync()
    {
        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_listener == null)
            {
                return;
            }

            _logger.LogInformation("Stopping listener");

            // Cancel the accept loop
            _listenerCts?.Cancel();

            // Stop the listener
            _listener.Stop();

            // Wait for accept task to complete (with timeout)
            if (_acceptTask != null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _acceptTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Accept task did not complete within timeout");
                }
            }

            // Cleanup
            _listenerCts?.Dispose();
            _listenerCts = null;
            _listener = null;
            _acceptTask = null;

            _logger.LogInformation("Listener stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping listener");
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Background loop that continuously accepts incoming connections.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// This method runs in a background task and:
    /// 1. Accepts incoming TCP connections
    /// 2. Checks connection limit
    /// 3. Creates PeerConnection instances
    /// 4. Adds connections to the pool
    /// </remarks>
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Accept loop started");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient? tcpClient = null;
                
                try
                {
                    // Accept incoming connection
                    tcpClient = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);

                    var remoteEndpoint = tcpClient.Client.RemoteEndPoint;
                    _logger.LogInformation(
                        "Accepted incoming connection from {RemoteEndpoint} ({CurrentConnections}/{MaxConnections})",
                        remoteEndpoint,
                        _connections.Count,
                        _config.MaxConnections);

                    // Check connection limit
                    if (_connections.Count >= _config.MaxConnections)
                    {
                        _logger.LogWarning(
                            "Rejecting connection from {RemoteEndpoint} - maximum connection limit ({MaxConnections}) reached",
                            remoteEndpoint,
                            _config.MaxConnections);
                        
                        tcpClient.Close();
                        tcpClient.Dispose();
                        continue;
                    }

                    // Create peer connection
                    var peerLogger = _loggerFactory.CreateLogger<PeerConnection>();
                    var connection = new PeerConnection(tcpClient, _messageFramer, peerLogger);

                    // Generate unique connection ID
                    string connectionId = Guid.NewGuid().ToString();

                    // Add to connections dictionary
                    if (!_connections.TryAdd(connectionId, connection))
                    {
                        _logger.LogWarning(
                            "Failed to add connection from {RemoteEndpoint} to pool",
                            remoteEndpoint);
                        
                        await connection.CloseAsync().ConfigureAwait(false);
                        connection.Dispose();
                        continue;
                    }

                    _logger.LogInformation(
                        "Added incoming connection from {RemoteEndpoint}, connection ID: {ConnectionId} ({CurrentConnections}/{MaxConnections})",
                        remoteEndpoint,
                        connectionId,
                        _connections.Count,
                        _config.MaxConnections);
                }
                catch (OperationCanceledException)
                {
                    // Expected when stopping
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting connection");
                    
                    // Cleanup on error
                    tcpClient?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in accept loop");
        }
        finally
        {
            _logger.LogDebug("Accept loop stopped");
        }
    }

    /// <summary>
    /// Removes and closes a peer connection.
    /// </summary>
    /// <param name="connection">The connection to remove.</param>
    /// <returns>Task that completes when the connection is removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when connection is null.</exception>
    /// <remarks>
    /// This method finds the connection in the pool, removes it, closes it,
    /// and disposes of it. If the connection is not found in the pool, the
    /// method completes without error.
    /// </remarks>
    public async Task RemoveConnectionAsync(IPeerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Find the connection in the dictionary
        var entry = _connections.FirstOrDefault(kvp => kvp.Value == connection);
        
        if (entry.Key == null)
        {
            _logger.LogWarning(
                "Attempted to remove connection for peer {PeerId} that is not in the pool",
                connection.PeerId);
            return;
        }

        // Remove from dictionary
        if (_connections.TryRemove(entry.Key, out var removedConnection))
        {
            _logger.LogInformation(
                "Removing connection to peer {PeerId}, connection ID: {ConnectionId} ({CurrentConnections}/{MaxConnections})",
                connection.PeerId,
                entry.Key,
                _connections.Count,
                _config.MaxConnections);

            try
            {
                // Close the connection
                await removedConnection.CloseAsync().ConfigureAwait(false);
                
                // Dispose the connection
                removedConnection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error closing connection to peer {PeerId}",
                    connection.PeerId);
            }
        }
    }

    /// <summary>
    ///  loses all active connections.
    /// </summary>
    /// <returns>Task that completes when all connections are closed.</returns>
    /// <remarks>
    /// This method closes and disposes all connections in the pool.
    /// It's typically called during shutdown.
    /// </remarks>
    public async Task CloseAllConnectionsAsync()
    {
        _logger.LogInformation(
            "Closing all connections ({Count} active)",
            _connections.Count);

        // Get all connections
        var connections = _connections.ToArray();

        // Clear the dictionary first
        _connections.Clear();

        // Close all connections in parallel
        var closeTasks = connections.Select(async kvp =>
        {
            try
            {
                await kvp.Value.CloseAsync().ConfigureAwait(false);
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error closing connection to peer {PeerId}",
                    kvp.Value.PeerId);
            }
        });

        await Task.WhenAll(closeTasks).ConfigureAwait(false);

        _logger.LogInformation("All connections closed");
    }

    /// <summary>
    /// Disposes the connection manager and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop listening
        StopListeningAsync().GetAwaiter().GetResult();

        // Close all connections
        CloseAllConnectionsAsync().GetAwaiter().GetResult();

        // Dispose resources
        _connectionLock.Dispose();
        _listenerCts?.Dispose();

        GC.SuppressFinalize(this);
    }
}