using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Dotp2pNet.Storage.Bitfield;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Networking.PieceExchange;

/// <summary>
/// Coordinates the complete piece exchange protocol between peers.
/// </summary>
/// <remarks>
/// The PieceExchangeCoordinator manages the full lifecycle of piece exchange:
/// 
/// 1. Handshake: Exchange protocol version, info hash, and peer IDs
/// 2. Bitfield Exchange: Share which pieces each peer has
/// 3. Interest Management: Express interest when peer has needed pieces
/// 4. Piece Requests: Request pieces with flow control
/// 5. Piece Responses: Respond to peer requests when unchoked
/// 6. State Tracking: Monitor peer state transitions
/// 
/// This implements the core BitTorrent piece exchange protocol with proper
/// flow control, hash verification, and error handling.
/// </remarks>
public class PieceExchangeCoordinator : IDisposable
{
    private readonly IPeerConnection _connection;
    private readonly IPieceManager _pieceManager;
    private readonly ILogger<PieceExchangeCoordinator> _logger;
    private readonly PeerState _peerState;
    private readonly PieceRequester _requester;
    private readonly PieceResponder _responder;
    private readonly CancellationTokenSource _cts;
    private Task? _messageProcessingTask;
    private bool _disposed;

    /// <summary>
    /// Gets the peer state tracker.
    /// </summary>
    public PeerState State => _peerState;

    /// <summary>
    /// Gets the piece requester.
    /// </summary>
    public PieceRequester Requester => _requester;

    /// <summary>
    /// Gets the piece responder.
    /// </summary>
    public PieceResponder Responder => _responder;

    /// <summary>
    /// Initializes a new instance of the PieceExchangeCoordinator class.
    /// </summary>
    /// <param name="connection">The peer connection.</param>
    /// <param name="pieceManager">The piece manager.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public PieceExchangeCoordinator(
        IPeerConnection connection,
        IPieceManager pieceManager,
        ILoggerFactory loggerFactory)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _pieceManager = pieceManager ?? throw new ArgumentNullException(nameof(pieceManager));
        
        if (loggerFactory == null)
        {
            throw new ArgumentNullException(nameof(loggerFactory));
        }

        _logger = loggerFactory.CreateLogger<PieceExchangeCoordinator>();
        
        var peerStateLogger = loggerFactory.CreateLogger<PeerState>();
        var requesterLogger = loggerFactory.CreateLogger<PieceRequester>();
        var responderLogger = loggerFactory.CreateLogger<PieceResponder>();

        _peerState = new PeerState(connection.PeerId, peerStateLogger);
        _requester = new PieceRequester(connection, pieceManager, requesterLogger);
        _responder = new PieceResponder(connection, pieceManager, responderLogger);
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Performs the complete handshake and bitfield exchange with the peer.
    /// </summary>
    /// <param name="infoHash">The torrent info hash.</param>
    /// <param name="ourPeerId">Our peer ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Task that completes when handshake and bitfield exchange are done.</returns>
    public async Task PerformHandshakeAsync(byte[] infoHash, byte[] ourPeerId, CancellationToken ct)
    {
        _logger.LogInformation("Starting handshake with peer {PeerId}", _connection.PeerId);

        // Step 1: Perform handshake
        await _connection.HandshakeAsync(infoHash, ourPeerId, ct);

        _logger.LogInformation("Handshake completed with peer {PeerId}", _connection.PeerId);

        // Step 2: Send our bitfield
        await SendBitfieldAsync(ct);

        // Step 3: Receive peer's bitfield (or wait for it in message processing)
        _logger.LogInformation("Waiting for bitfield from peer {PeerId}", _connection.PeerId);
    }

    /// <summary>
    /// Starts processing messages from the peer.
    /// </summary>
    public void StartMessageProcessing()
    {
        if (_messageProcessingTask != null)
        {
            throw new InvalidOperationException("Message processing already started");
        }

        _messageProcessingTask = Task.Run(async () => await ProcessMessagesAsync(_cts.Token));
        _logger.LogInformation("Started message processing for peer {PeerId}", _connection.PeerId);
    }

    /// <summary>
    /// Sends our bitfield to the peer.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    private async Task SendBitfieldAsync(CancellationToken ct)
    {
        var ourBitfield = _pieceManager.GetBitfield();
        var bitfieldBytes = ourBitfield.ToBytes();

        _logger.LogDebug(
            "Sending bitfield to peer {PeerId}: {Available}/{Total} pieces",
            _connection.PeerId,
            ourBitfield.CountSetBits(),
            ourBitfield.Length);

        await _connection.SendMessageAsync((byte)MessageType.Bitfield, bitfieldBytes, ct);
    }

    /// <summary>
    /// Processes incoming messages from the peer.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    private async Task ProcessMessagesAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _connection.IsConnected)
            {
                // Receive next message
                var (messageType, payload) = await _connection.ReceiveMessageAsync(ct);

                // Handle message based on type
                await HandleMessageAsync((MessageType)messageType, payload, ct);

                // Check for timed-out requests periodically
                var timedOutPieces = _requester.GetTimedOutRequests();
                if (timedOutPieces.Count > 0)
                {
                    _logger.LogWarning(
                        "{Count} piece requests timed out for peer {PeerId}",
                        timedOutPieces.Count,
                        _connection.PeerId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message processing cancelled for peer {PeerId}", _connection.PeerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing messages from peer {PeerId}", _connection.PeerId);
        }
    }

    /// <summary>
    /// Handles a received message based on its type.
    /// </summary>
    /// <param name="messageType">The type of message received.</param>
    /// <param name="payload">The message payload.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task HandleMessageAsync(MessageType messageType, byte[] payload, CancellationToken ct)
    {
        _logger.LogDebug(
            "Received {MessageType} message from peer {PeerId} ({Size} bytes)",
            messageType,
            _connection.PeerId,
            payload.Length);

        switch (messageType)
        {
            case MessageType.Bitfield:
                await HandleBitfieldMessageAsync(payload, ct);
                break;

            case MessageType.Have:
                HandleHaveMessage(payload);
                break;

            case MessageType.Interested:
                HandleInterestedMessage();
                break;

            case MessageType.NotInterested:
                HandleNotInterestedMessage();
                break;

            case MessageType.Choke:
                HandleChokeMessage();
                break;

            case MessageType.Unchoke:
                HandleUnchokeMessage();
                break;

            case MessageType.Request:
                await HandleRequestMessageAsync(payload, ct);
                break;

            case MessageType.Piece:
                await HandlePieceMessageAsync(payload, ct);
                break;

            case MessageType.KeepAlive:
                _logger.LogDebug("Received keep-alive from peer {PeerId}", _connection.PeerId);
                break;

            default:
                _logger.LogWarning(
                    "Received unknown message type {MessageType} from peer {PeerId}",
                    messageType,
                    _connection.PeerId);
                break;
        }
    }

    /// <summary>
    /// Handles a bitfield message from the peer.
    /// </summary>
    private async Task HandleBitfieldMessageAsync(byte[] payload, CancellationToken ct)
    {
        var peerBitfield = new Bitfield(payload, _pieceManager.TotalPieces);
        _peerState.SetPeerBitfield(peerBitfield);

        // Determine if we're interested in this peer
        await UpdateInterestAsync(ct);
    }

    /// <summary>
    /// Handles a have message from the peer.
    /// </summary>
    private void HandleHaveMessage(byte[] payload)
    {
        if (payload.Length < 4)
        {
            _logger.LogWarning("Invalid have message from peer {PeerId}: too short", _connection.PeerId);
            return;
        }

        // Parse piece index (big-endian)
        var pieceIndex = (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3];

        _peerState.SetPeerHasPiece(pieceIndex);
    }

    /// <summary>
    /// Handles an interested message from the peer.
    /// </summary>
    private void HandleInterestedMessage()
    {
        _peerState.SetPeerInterested(true);
    }

    /// <summary>
    /// Handles a not interested message from the peer.
    /// </summary>
    private void HandleNotInterestedMessage()
    {
        _peerState.SetPeerInterested(false);
    }

    /// <summary>
    /// Handles a choke message from the peer.
    /// </summary>
    private void HandleChokeMessage()
    {
        _peerState.SetPeerChoking(true);
        _requester.CancelAllRequests();
    }

    /// <summary>
    /// Handles an unchoke message from the peer.
    /// </summary>
    private void HandleUnchokeMessage()
    {
        _peerState.SetPeerChoking(false);
    }

    /// <summary>
    /// Handles a request message from the peer.
    /// </summary>
    private async Task HandleRequestMessageAsync(byte[] payload, CancellationToken ct)
    {
        var requestMessage = DeserializeRequestMessage(payload);
        if (requestMessage != null)
        {
            await _responder.HandleRequestAsync(requestMessage, ct);
        }
    }

    /// <summary>
    /// Handles a piece message from the peer.
    /// </summary>
    private async Task HandlePieceMessageAsync(byte[] payload, CancellationToken ct)
    {
        var pieceMessage = DeserializePieceMessage(payload);
        if (pieceMessage != null)
        {
            var success = await _requester.HandleReceivedPieceAsync(pieceMessage, ct);
            
            if (success)
            {
                // Broadcast have message to all peers (would be done by orchestration layer)
                _logger.LogDebug("Successfully received piece {PieceIndex}", pieceMessage.PieceIndex);
            }
        }
    }

    /// <summary>
    /// Updates our interest state based on what the peer has.
    /// </summary>
    private async Task UpdateInterestAsync(CancellationToken ct)
    {
        var peerBitfield = _peerState.PeerBitfield;
        if (peerBitfield == null)
        {
            return;
        }

        // Check if peer has any pieces we need
        var neededPiece = _pieceManager.SelectPiece(peerBitfield);
        var interested = neededPiece >= 0;

        if (interested != _peerState.AmInterested)
        {
            _peerState.SetAmInterested(interested);

            // Send interested/not interested message
            var messageType = interested ? MessageType.Interested : MessageType.NotInterested;
            await _connection.SendMessageAsync((byte)messageType, Array.Empty<byte>(), ct);

            _logger.LogInformation(
                "Sent {MessageType} to peer {PeerId}",
                messageType,
                _connection.PeerId);
        }
    }

    /// <summary>
    /// Deserializes a request message from bytes.
    /// </summary>
    private RequestMessage? DeserializeRequestMessage(byte[] payload)
    {
        if (payload.Length < 12)
        {
            _logger.LogWarning("Invalid request message: too short");
            return null;
        }

        var pieceIndex = (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3];
        var offset = (payload[4] << 24) | (payload[5] << 16) | (payload[6] << 8) | payload[7];
        var length = (payload[8] << 24) | (payload[9] << 16) | (payload[10] << 8) | payload[11];

        return new RequestMessage
        {
            PieceIndex = pieceIndex,
            Offset = offset,
            Length = length
        };
    }

    /// <summary>
    /// Deserializes a piece message from bytes.
    /// </summary>
    private PieceMessage? DeserializePieceMessage(byte[] payload)
    {
        if (payload.Length < 8)
        {
            _logger.LogWarning("Invalid piece message: too short");
            return null;
        }

        var pieceIndex = (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3];
        var offset = (payload[4] << 24) | (payload[5] << 16) | (payload[6] << 8) | payload[7];
        var data = new byte[payload.Length - 8];
        Array.Copy(payload, 8, data, 0, data.Length);

        return new PieceMessage
        {
            PieceIndex = pieceIndex,
            Offset = offset,
            Data = data
        };
    }

    /// <summary>
    /// Disposes resources used by the coordinator.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cts.Cancel();
        _cts.Dispose();

        _messageProcessingTask?.Wait(TimeSpan.FromSeconds(5));

        _disposed = true;
        _logger.LogInformation("Disposed piece exchange coordinator for peer {PeerId}", _connection.PeerId);
    }
}
