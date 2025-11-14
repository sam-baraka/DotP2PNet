using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Networking.PieceExchange;

/// <summary>
/// Handles incoming piece requests from remote peers.
/// </summary>
/// <remarks>
/// The PieceResponder processes request messages from peers and sends
/// the requested piece data. It respects the choking state to implement
/// BitTorrent's tit-for-tat bandwidth management strategy.
/// 
/// Key concepts:
/// - Choking: Controls which peers can download from us
/// - Request Validation: Ensures requests are for valid pieces we have
/// - Idempotency: Handles duplicate requests gracefully
/// - Upload Tracking: Monitors data sent to peers
/// </remarks>
public class PieceResponder
{
    private readonly IPeerConnection _connection;
    private readonly IPieceManager _pieceManager;
    private readonly ILogger<PieceResponder> _logger;
    private long _bytesUploaded;
    private readonly object _statsLock = new();

    /// <summary>
    /// Gets the total number of bytes uploaded to this peer.
    /// </summary>
    public long BytesUploaded
    {
        get
        {
            lock (_statsLock)
            {
                return _bytesUploaded;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the PieceResponder class.
    /// </summary>
    /// <param name="connection">The peer connection to respond to.</param>
    /// <param name="pieceManager">The piece manager for reading pieces.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public PieceResponder(
        IPeerConnection connection,
        IPieceManager pieceManager,
        ILogger<PieceResponder> logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _pieceManager = pieceManager ?? throw new ArgumentNullException(nameof(pieceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles an incoming request message from a peer.
    /// </summary>
    /// <param name="requestMessage">The request message to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the piece was sent successfully; otherwise, false.</returns>
    public async Task<bool> HandleRequestAsync(RequestMessage requestMessage, CancellationToken ct)
    {
        if (requestMessage == null)
        {
            throw new ArgumentNullException(nameof(requestMessage));
        }

        if (!_connection.IsConnected)
        {
            _logger.LogWarning(
                "Cannot handle request from peer {PeerId}: not connected",
                _connection.PeerId);
            return false;
        }

        // Check if we are choking this peer
        if (_connection.AmChoking)
        {
            _logger.LogDebug(
                "Ignoring request for piece {PieceIndex} from peer {PeerId}: we are choking them",
                requestMessage.PieceIndex,
                _connection.PeerId);
            return false;
        }

        // Validate request
        if (!ValidateRequest(requestMessage))
        {
            _logger.LogWarning(
                "Invalid request from peer {PeerId}: piece={PieceIndex}, offset={Offset}, length={Length}",
                _connection.PeerId,
                requestMessage.PieceIndex,
                requestMessage.Offset,
                requestMessage.Length);
            return false;
        }

        // Check if we have the requested piece
        if (!_pieceManager.HasPiece(requestMessage.PieceIndex))
        {
            _logger.LogWarning(
                "Peer {PeerId} requested piece {PieceIndex} that we don't have",
                _connection.PeerId,
                requestMessage.PieceIndex);
            return false;
        }

        try
        {
            // Read the piece from storage
            var pieceData = await _pieceManager.GetPieceAsync(requestMessage.PieceIndex, ct);

            // Extract the requested portion (handle offset and length)
            var requestedData = ExtractRequestedData(pieceData, requestMessage.Offset, requestMessage.Length);

            // Create piece message
            var pieceMessage = new PieceMessage
            {
                PieceIndex = requestMessage.PieceIndex,
                Offset = requestMessage.Offset,
                Data = requestedData
            };

            // Serialize and send piece message
            var payload = SerializePieceMessage(pieceMessage);
            await _connection.SendMessageAsync((byte)MessageType.Piece, payload, ct);

            // Update upload statistics
            lock (_statsLock)
            {
                _bytesUploaded += requestedData.Length;
            }

            _logger.LogDebug(
                "Sent piece {PieceIndex} (offset={Offset}, length={Length}) to peer {PeerId} (total uploaded: {TotalUploaded} bytes)",
                requestMessage.PieceIndex,
                requestMessage.Offset,
                requestedData.Length,
                _connection.PeerId,
                BytesUploaded);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send piece {PieceIndex} to peer {PeerId}",
                requestMessage.PieceIndex,
                _connection.PeerId);
            return false;
        }
    }

    /// <summary>
    /// Validates a request message.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>True if the request is valid; otherwise, false.</returns>
    private bool ValidateRequest(RequestMessage request)
    {
        // Check piece index is in valid range
        if (request.PieceIndex < 0 || request.PieceIndex >= _pieceManager.TotalPieces)
        {
            return false;
        }

        // Check offset is non-negative
        if (request.Offset < 0)
        {
            return false;
        }

        // Check length is within acceptable range (1 byte to 128 KB)
        if (request.Length < 1 || request.Length > 131072)
        {
            return false;
        }

        // Check that offset + length doesn't exceed piece size
        if (request.Offset + request.Length > _pieceManager.PieceLength)
        {
            // Allow for last piece being smaller
            var isLastPiece = request.PieceIndex == _pieceManager.TotalPieces - 1;
            if (!isLastPiece)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the requested portion of piece data.
    /// </summary>
    /// <param name="pieceData">The full piece data.</param>
    /// <param name="offset">The offset within the piece.</param>
    /// <param name="length">The number of bytes to extract.</param>
    /// <returns>The requested portion of the piece.</returns>
    private byte[] ExtractRequestedData(byte[] pieceData, int offset, int length)
    {
        // Ensure we don't read beyond the piece data
        var actualLength = Math.Min(length, pieceData.Length - offset);

        if (actualLength <= 0)
        {
            return Array.Empty<byte>();
        }

        var requestedData = new byte[actualLength];
        Array.Copy(pieceData, offset, requestedData, 0, actualLength);

        return requestedData;
    }

    /// <summary>
    /// Serializes a piece message to bytes.
    /// </summary>
    /// <param name="message">The piece message to serialize.</param>
    /// <returns>Serialized message payload.</returns>
    private byte[] SerializePieceMessage(PieceMessage message)
    {
        var payload = new byte[8 + message.Data.Length]; // 4 bytes index + 4 bytes offset + data

        // Piece index (big-endian)
        payload[0] = (byte)(message.PieceIndex >> 24);
        payload[1] = (byte)(message.PieceIndex >> 16);
        payload[2] = (byte)(message.PieceIndex >> 8);
        payload[3] = (byte)message.PieceIndex;

        // Offset (big-endian)
        payload[4] = (byte)(message.Offset >> 24);
        payload[5] = (byte)(message.Offset >> 16);
        payload[6] = (byte)(message.Offset >> 8);
        payload[7] = (byte)message.Offset;

        // Data
        Array.Copy(message.Data, 0, payload, 8, message.Data.Length);

        return payload;
    }

    /// <summary>
    /// Resets upload statistics.
    /// </summary>
    public void ResetStatistics()
    {
        lock (_statsLock)
        {
            _bytesUploaded = 0;
        }

        _logger.LogDebug("Reset upload statistics for peer {PeerId}", _connection.PeerId);
    }
}
 /// 