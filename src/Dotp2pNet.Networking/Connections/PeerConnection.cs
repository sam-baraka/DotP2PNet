using System.Net.Sockets;
using System.Threading.Channels;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.ErrorHandling;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Networking.Connections;

/// <summary>
/// Represents a TCP connection to a single remote peer in the P2P network.
/// </summary>
/// <remarks>
/// <para>
/// PeerConnection manages the complete lifecycle of a peer-to-peer connection:
/// </para>
/// <list type="bullet">
/// <item><description><b>Connection Establishment:</b> Connects to remote peer via TCP socket</description></item>
/// <item><description><b>Handshake:</b> Exchanges BitTorrent handshake to verify torrent and peer identity</description></item>
/// <item><description><b>Message Exchange:</b> Sends and receives framed protocol messages</description></item>
/// <item><description><b>State Management:</b> Tracks connection state and peer choking/interest status</description></item>
/// <item><description><b>Graceful Shutdown:</b> Properly closes connection and cleans up resources</description></item>
/// </list>
/// 
/// <para><b>Thread Safety:</b></para>
/// <para>
/// - SendMessageAsync uses SemaphoreSlim to ensure only one thread sends at a time
/// - ReceiveMessagesAsync uses Channels for thread-safe message queuing
/// - State properties are volatile for visibility across threads
/// </para>
/// 
/// <para><b>Connection Lifecycle:</b></para>
/// <code>
/// Disconnected → Connecting → Connected → Handshaking → Active → Closing → Closed
/// </code>
/// </remarks>
public class PeerConnection : IPeerConnection
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream? _stream;
    private readonly IMessageFramer _messageFramer;
    private readonly ILogger<PeerConnection> _logger;
    private readonly SemaphoreSlim _sendLock;
    private readonly Channel<(byte messageType, byte[] payload)> _incomingMessages;
    private readonly CancellationTokenSource _disposalCts;
    private readonly Task _receiveTask;
    private readonly TimeoutTracker _inactivityTracker;

    private volatile bool _isConnected;
    private volatile bool _amChoking;
    private volatile bool _peerChoking;
    private volatile bool _amInterested;
    private volatile bool _peerInterested;
    private volatile bool _disposed;

    private string _peerId;

    /// <summary>
    /// /// Gets the unique identifier this peer.
    /// </summary>
    public string PeerId => _peerId;

    /// <summary>
    /// Gets a value indicating whether the connection is currently active.
    /// </summary>
    public bool IsConnected => _isConnected && !_disposed;

    /// <summary>
    /// Gets a value indicating whether we are choking this peer (not sending pieces).
    /// </summary>
    public bool AmChoking => _amChoking;

    /// <summary>
    /// Gets a value indicating whether this peer is choking us (not sending pieces).
    /// </summary>
    public bool PeerChoking => _peerChoking;

    /// <summary>
    /// Gets a value indicating whether we are interested in pieces from this peer.
    /// </summary>
    public bool AmInterested => _amInterested;

    /// <summary>
    /// Gets a value indicating whether this peer is interested in our pieces.
    /// </summary>
    public bool PeerInterested => _peerInterested;

    /// <summary>
    /// Checks if the peer has been inactive for too long (2 minutes).
    /// </summary>
    /// <returns>True if the peer has timed out, false otherwise.</returns>
    public bool HasTimedOut() => _inactivityTracker.HasTimedOut();

    /// <summary>
    /// Gets the time since the last activity from this peer.
    /// </summary>
    /// <returns>Time elapsed since last activity.</returns>
    public TimeSpan GetTimeSinceLastActivity() => _inactivityTracker.GetTimeSinceLastActivity();

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerConnection"/> class with an existing TCP client.
    /// </summary>
    /// <param name="tcpClient">The connected TCP client.</param>
    /// <param name="messageFramer">The message framer for protocol messages.</param>
    /// <param name="logger">Logger for connection events.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    ///     /// <excref="ArgumentException">Thrown when TCP client is not connected.</exception>
    public PeerConnection(
        TcpClient tcpClient,
        IMessageFramer messageFramer,
        ILogger<PeerConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(tcpClient);
        ArgumentNullException.ThrowIfNull(messageFramer);
        ArgumentNullException.ThrowIfNull(logger);

        if (!tcpClient.Connected)
        {
            throw new ArgumentException("TCP client must be connected", nameof(tcpClient));
        }

        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _messageFramer = messageFramer;
        _logger = logger;
        _sendLock = new SemaphoreSlim(1, 1);
        _incomingMessages = Channel.CreateUnbounded<(byte, byte[])>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });
        _disposalCts = new CancellationTokenSource();
        
        // Initialize inactivity tracker with 2-minute timeout
        _inactivityTracker = new TimeoutTracker(TimeSpan.FromMinutes(2));

        _peerId = "unknown";
        _isConnected = true;
        _amChoking = true;      // Start choking by default
        _peerChoking = true;    // Assume peer is choking us initially
        _amInterested = false;
        _peerInterested = false;

        // Start background task to receive messages
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_disposalCts.Token));

        _logger.LogInformation(
            "PeerConnection created for {RemoteEndpoint}",
            _tcpClient.Client.RemoteEndPoint);
    }

    /// <summary>
    /// Creates a new peer connection by connecting to a remote endpoint.
    /// </summary>
    /// <param name="host">The hostname or IP address of the remote peer.</param>
    /// <param name="port">The port number of the remote peer.</param>
    /// <param name="messageFramer">The message framer for protocol messages.</param>
    /// <param name="logger">Logger for connection events.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A connected PeerConnection instance.</returns>
    /// <exception cref="SocketException">Thrown when connection fails.</exception>
    /// //xception cref="OperationCanceledException">Thrown when operation is cancelled.</exception>
    public static async Task<PeerConnection> ConnectAsync(
        string host,
        int port,
        IMessageFramer messageFramer,
        ILogger<PeerConnection> logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(messageFramer);
        ArgumentNullException.ThrowIfNull(logger);

        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
        }

        logger.LogInformation("Connecting to peer at {Host}:{Port}", host, port);

        var tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
            logger.LogInformation("Successfully connected to {Host}:{Port}", host, port);

            return new PeerConnection(tcpClient, messageFramer, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to {Host}:{Port}", host, port);
            tcpClient.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Performs the BitTorrent handshake with the remote peer.
    /// </summary>
    /// <param name="infoHash">The 20-byte SHA1 hash of the torrent info dictionary.</param>
    /// <param name="peerId">Our 20-byte peer ID.</param>
    /// /// <param name="ct">Cancellation token.</param>
    /// <returns>Task that completes when handshake is successful.</returns>
    /// <exception cref="ArgumentException">Thrown when infoHash or peerId are not 20 bytes.</exception>
    /// <exception cref="InvalidOperationException">Thrown when handshake fails or connection is not active.</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled.</exception>
    /// <remarks>
    /// /   ///itTorrent Handshake Format:</para>
    /// <code>
    /// [1 byte: protocol length = 19]
    /// [19 bytes: "BitTorrent protocol"]
    /// [8 bytes: reserved/extension bits]
    /// [20 bytes: info_hash]
    /// [20 bytes: peer_id]
    ///    /de>
    /// <para>
    /// The handshake verifies both peers are sharing the same torrent (via info_hash)
    /// and establishes peer identities (via peer_id).
    ///    ara>
    /// </remarks>
    public async Task HandshakeAsync(byte[] infoHash, byte[] peerId, CancellationToken ct)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Connection is not active");
        }

        if (infoHash.Length != 20)
        {
            throw new ArgumentException("InfoHash must be exactly 20 bytes", nameof(infoHash));
        }

        if (peerId.Length != 20)
        {
            throw new ArgumentException("PeerId must be exactly 20 bytes", nameof(peerId));
        }

        _logger.LogDebug("Starting handshake with peer");

        try
        {
            // Build handshake message
            // Format: [1:pstrlen][19:pstr][8:reserved][20:info_hash][20:peer_id]
            byte[] handshake = new byte[68];
            handshake[0] = 19; // Protocol string length
            
            // Protocol string: "BitTorrent protocol"
            byte[] protocolString = System.Text.Encoding.ASCII.GetBytes("BitTorrent protocol");
            Array.Copy(protocolString, 0, handshake, 1, 19);
            
            // Reserved bytes (8 bytes of zeros)
            // bytes 20-27 are already zero
            
            // Info hash
            Array.Copy(infoHash, 0, handshake, 28, 20);
            
            // Peer ID
            Array.Copy(peerId, 0, handshake, 48, 20);

            // Send handshake
            await _stream!.WriteAsync(handshake, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);

            _logger.LogDebug("Sent handshake to peer");

            // Receive handshake response
            byte[] responseBuffer = new byte[68];
            int totalRead = 0;
            
            while (totalRead < 68)
            {
                int bytesRead = await _stream.ReadAsync(
                    responseBuffer.AsMemory(totalRead, 68 - totalRead),
                    ct).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    throw new InvalidOperationException("Peer closed connection during handshake");
                }

                totalRead += bytesRead;
            }

            _logger.LogDebug("Received handshake response from peer");

            // Validate handshake response
            if (responseBuffer[0] != 19)
            {
                throw new InvalidOperationException(
                    $"Invalid handshake: protocol length is {responseBuffer[0]}, expected 19");
            }

            string receivedProtocol = System.Text.Encoding.ASCII.GetString(responseBuffer, 1, 19);
            if (receivedProtocol != "BitTorrent protocol")
            {
                throw new InvalidOperationException(
                    $"Invalid handshake: protocol string is '{receivedProtocol}', expected 'BitTorrent protocol'");
            }

            // Verify info hash matches
            byte[] receivedInfoHash = new byte[20];
            Array.Copy(responseBuffer, 28, receivedInfoHash, 0, 20);
            
            if (!infoHash.SequenceEqual(receivedInfoHash))
            {
                throw new InvalidOperationException(
                    "Invalid handshake: info hash mismatch");
            }

            // Extract peer ID
            byte[] receivedPeerId = new byte[20];
            Array.Copy(responseBuffer, 48, receivedPeerId, 0, 20);
            _peerId = BitConverter.ToString(receivedPeerId[..8]).Replace("-", "").ToLower();

            _logger.LogInformation(
                "Handshake successful with peer {PeerId}",
                _peerId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Handshake failed");
            await CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Sends a message to the remote peer.
    /// </summary>
    ///    ram name="messageType">The type of message to send.</param>
    /// <param name="payload">The message payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Task that completes when the message is sent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when payload is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when connection is not active.</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled.</exception>
    /// <remarks>
    /// This method is thread-safe. Multiple threads can call SendMessageAsync concurrently,
    /// and messages will be sent sequentially using a semaphore lock.
    /// </remarks>
    public async Task SendMessageAsync(byte messageType, byte[] payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!IsConnected)
        {
            throw new InvalidOperationException("Connection is not active");
        }

        // Acquire send lock to ensure thread-safe sending
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Frame the message
            byte[] framedMessage = _messageFramer.FrameMessage(messageType, payload);

            // Send to network stream
            await _stream!.WriteAsync(framedMessage, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Sent message type {MessageType} ({PayloadSize} bytes) to peer {PeerId}",
                messageType,
                payload.Length,
                _peerId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to send message type {MessageType} to peer {PeerId}",
                messageType,
                _peerId);
            await CloseAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Receives the next message from the remote peer.
    /// </summary>
    /// // ram name="ct">Cancellation token.</param>
    /// <returns>Tuple containing message type and payload.</returns>
    /// <exception cref="InvalidOperationException">Thrown when connection is not active.</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled.</exception>
    /// <remarks>
    /// This method reads from an internal channel that is populated by a background receive loop.
    /// Multiple threads can call ReceiveMessageAsync concurrently.
    /// </remarks>
    public async Task<(byte messageType, byte[] payload)> ReceiveMessageAsync(CancellationToken ct)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Connection is not active");
        }

        try
        {
            return await _incomingMessages.Reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException("Connection has been closed");
        }
    }

    /// <summary>
    /// Background loop that continuously receives messages from the network stream.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// This method runs in a background task and:
    /// 1. Reads bytes from the network stream
    /// 2. Parses complete messages using the message framer
    /// 3. Writes messages to the incoming messages channel
    /// 4. Handles connection errors and cleanup
    /// </remarks>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[16384]; // 16 KB receive buffer

        try
        {
            while (!ct.IsCancellationRequested && _isConnected)
            {
                // Read from network stream
                int bytesRead = await _stream!.ReadAsync(buffer, ct).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    // Peer closed connection
                    _logger.LogInformation("Peer {PeerId} closed connection", _peerId);
                    break;
                }

                _logger.LogTrace(
                    "Received {BytesRead} bytes from peer {PeerId}",
                    bytesRead,
                    _peerId);

                // Parse messages from received bytes
                int consumed = _messageFramer.ParseMessages(
                    buffer,
                    bytesRead,
                    out var messages);

                // Write parsed messages to channel
                foreach (var (messageType, payload) in messages)
                {
                    // Record activity when message received
                    _inactivityTracker.RecordActivity();
                    
                    await _incomingMessages.Writer.WriteAsync((messageType, payload), ct)
                        .ConfigureAwait(false);

                    _logger.LogDebug(
                        "Received message type {MessageType} ({PayloadSize} bytes) from peer {PeerId}",
                        messageType,
                        payload.Length,
                        _peerId);
                }
                
                // Check for timeout after processing messages
                if (_inactivityTracker.HasTimedOut())
                {
                    _logger.LogWarning(
                        "Peer {PeerId} has been inactive for {Elapsed}, closing connection",
                        _peerId,
                        _inactivityTracker.GetTimeSinceLastActivity());
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Receive loop cancelled for peer {PeerId}", _peerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error in receive loop for peer {PeerId}",
                _peerId);
        }
        finally
        {
            // Close the incoming messages channel
            _incomingMessages.Writer.Complete();
            _isConnected = false;
        }
    }

    /// <summary>
    /// Closes the connection to the peer.
    /// </summary>
    /// <returns>Task that completes when the connection is closed.</returns>
    /// <remarks>
    /// This method performs a graceful shutdown:
    /// 1. Stops the receive loop
    /// 2. Closes the network stream
    /// 3. Closes the TCP client
    /// 4. Completes the incoming messages channel
    /// </remarks>
    public async Task CloseAsync()
    {
        if (!_isConnected)
        {
            return;
        }

        _logger.LogInformation("Closing connection to peer {PeerId}", _peerId);

        _isConnected = false;

        try
        {
            // Cancel the receive loop
            _disposalCts.Cancel();

            // Wait for receive task to complete (with timeout)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _receiveTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Receive task did not complete within timeout for peer {PeerId}", _peerId);
            }

            // Close network stream
            _stream?.Close();

            // Close TCP client
            _tcpClient.Close();

            _logger.LogInformation("Connection closed to peer {PeerId}", _peerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing connection to peer {PeerId}", _peerId);
        }
    }

    /// <summary>
    /// Disposes the peer connection and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Close connection if still open
        if (_isConnected)
        {
            CloseAsync().GetAwaiter().GetResult();
        }

        // Dispose resources
        _disposalCts.Dispose();
        _sendLock.Dispose();
        _stream?.Dispose();
        _tcpClient.Dispose();

        GC.SuppressFinalize(this);
    }
}