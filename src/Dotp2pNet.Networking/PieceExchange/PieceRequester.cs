using System.Collections.Concurrent;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Networking.PieceExchange;

/// <summary>
/// Manages requesting pieces from remote peers with flow control.
/// </summary>
/// <remarks>
/// The PieceRequester implements flow control by limiting concurrent requests
/// to prevent overwhelming peers. It tracks outstanding requests and handles
/// piece verification after receipt.
/// 
/// Key concepts:
/// - Flow Control: Limits concurrent requests to 5 per peer
/// - Request Pipelining: Queues multiple requests for efficiency
/// - Hash Verification: Validates received pieces before acceptance
/// - Retry Logic: Re-requests pieces from different peers on failure
/// </remarks>
public class PieceRequester
{
    private readonly IPeerConnection _connection;
    private readonly IPieceManager _pieceManager;
    private readonly ILogger<PieceRequester> _logger;
    private readonly SemaphoreSlim _requestSemaphore;
    private readonly ConcurrentDictionary<int, DateTime> _outstandingRequests;
    private readonly TimeSpan _requestTimeout;
    private readonly int _maxConcurrentRequests;

    /// <summary>
    /// Gets the number of currently outstanding requests.
    /// </summary>
    public int OutstandingRequestCount => _outstandingRequests.Count;

    /// <summary>
    /// Initializes a new instance of the PieceRequester class.
    /// </summary>
    /// <param name="connection">The peer connection to request pieces from.</param>
    /// <param name="pieceManager">The piece manager for storing received pieces.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="maxConcurrentRequests">Maximum number of concurrent requests (default 5).</param>
    /// <param name="requestTimeout">Timeout for piece requests (default 30 seconds).</param>
    public PieceRequester(
        IPeerConnection connection,
        IPieceManager pieceManager,
        ILogger<PieceRequester> logger,
        int maxConcurrentRequests = 5,
        TimeSpan? requestTimeout = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _pieceManager = pieceManager ?? throw new ArgumentNullException(nameof(pieceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (maxConcurrentRequests < 1 || maxConcurrentRequests > 20)
        {
            throw new ArgumentException(
                "Max concurrent requests must be between 1 and 20",
                nameof(maxConcurrentRequests));
        }

        _maxConcurrentRequests = maxConcurrentRequests;
        _requestSemaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _outstandingRequests = new ConcurrentDictionary<int, DateTime>();
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Requests a piece from the remote peer.
    /// </summary>
    /// <param name="pieceIndex">Index of the piece to request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the request was sent successfully; otherwise, false.</returns>
    /// <exception cref="InvalidOperationException">Thrown when peer is choked or not connected.</exception>
    public async Task<bool> RequestPieceAsync(int pieceIndex, CancellationToken ct)
    {
        if (!_connection.IsConnected)
        {
            throw new InvalidOperationException("Cannot request piece: peer is not connected");
        }

        if (_connection.PeerChoking)
        {
            _logger.LogDebug(
                "Cannot request piece {PieceIndex} from peer {PeerId}: peer is choking us",
                pieceIndex,
                _connection.PeerId);
            return false;
        }

        // Wait for available request slot (flow control)
        await _requestSemaphore.WaitAsync(ct);

        try
        {
            // Track this request
            _outstandingRequests[pieceIndex] = DateTime.UtcNow;

            // Create request message
            var requestMessage = new RequestMessage
            {
                PieceIndex = pieceIndex,
                Offset = 0,
                Length = _pieceManager.PieceLength
            };

            // Serialize request message
            var payload = SerializeRequestMessage(requestMessage);

            // Send request
            await _connection.SendMessageAsync((byte)MessageType.Request, payload, ct);

            _logger.LogDebug(
                "Requested piece {PieceIndex} from peer {PeerId} ({OutstandingRequests}/{MaxRequests} outstanding)",
                pieceIndex,
                _connection.PeerId,
                _outstandingRequests.Count,
                _maxConcurrentRequests);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to request piece {PieceIndex} from peer {PeerId}",
                pieceIndex,
                _connection.PeerId);

            // Remove from outstanding requests and release semaphore
            _outstandingRequests.TryRemove(pieceIndex, out _);
            _requestSemaphore.Release();

            throw;
        }
    }

    /// <summary>
    /// Handles a received piece message.
    /// </summary>
    /// <param name="pieceMessage">The received piece message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the piece was valid and stored; otherwise, false.</returns>
    public async Task<bool> HandleReceivedPieceAsync(PieceMessage pieceMessage, CancellationToken ct)
    {
        if (pieceMessage == null)
        {
            throw new ArgumentNullException(nameof(pieceMessage));
        }

        var pieceIndex = pieceMessage.PieceIndex;

        // Check if this was an outstanding request
        if (!_outstandingRequests.TryRemove(pieceIndex, out var requestTime))
        {
            _logger.LogWarning(
                "Received unrequested piece {PieceIndex} from peer {PeerId}",
                pieceIndex,
                _connection.PeerId);
            return false;
        }

        // Release semaphore slot
        _requestSemaphore.Release();

        // Calculate request latency
        var latency = DateTime.UtcNow - requestTime;
        _logger.LogDebug(
            "Received piece {PieceIndex} from peer {PeerId} (latency: {Latency}ms)",
            pieceIndex,
            _connection.PeerId,
            latency.TotalMilliseconds);

        // Verify and store the piece
        var isValid = await _pieceManager.StorePieceAsync(pieceIndex, pieceMessage.Data, ct);

        if (!isValid)
        {
            _logger.LogWarning(
                "Piece {PieceIndex} from peer {PeerId} failed hash verification",
                pieceIndex,
                _connection.PeerId);
            return false;
        }

        _logger.LogInformation(
            "Successfully received and verified piece {PieceIndex} from peer {PeerId}",
            pieceIndex,
            _connection.PeerId);

        return true;
    }

    /// <summary>
    /// Checks for timed-out requests and returns their piece indices.
    /// </summary>
    /// <returns>List of piece indices that have timed out.</returns>
    public List<int> GetTimedOutRequests()
    {
        var now = DateTime.UtcNow;
        var timedOut = new List<int>();

        foreach (var kvp in _outstandingRequests)
        {
            if (now - kvp.Value > _requestTimeout)
            {
                timedOut.Add(kvp.Key);
                _logger.LogWarning(
                    "Request for piece {PieceIndex} from peer {PeerId} timed out after {Timeout}s",
                    kvp.Key,
                    _connection.PeerId,
                    _requestTimeout.TotalSeconds);
            }
        }

        // Remove timed-out requests and release semaphore slots
        foreach (var pieceIndex in timedOut)
        {
            if (_outstandingRequests.TryRemove(pieceIndex, out _))
            {
                _requestSemaphore.Release();
            }
        }

        return timedOut;
    }

    /// <summary>
    /// Cancels all outstanding requests.
    /// </summary>
    public void CancelAllRequests()
    {
        var count = _outstandingRequests.Count;
        _outstandingRequests.Clear();

        // Release all semaphore slots
        for (int i = 0; i < count; i++)
        {
            _requestSemaphore.Release();
        }

        _logger.LogInformation(
            "Cancelled {Count} outstanding requests to peer {PeerId}",
            count,
            _connection.PeerId);
    }

    /// <summary>
    /// Serializes a request message to bytes.
    /// </summary>
    /// <param name="message">The request message to serialize.</param>
    /// <returns>Serialized message payload.</returns>
    private byte[] SerializeRequestMessage(RequestMessage message)
    {
        var payload = new byte[12]; // 4 bytes each for index, offset, length

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

        // Length (big-endian)
        payload[8] = (byte)(message.Length >> 24);
        payload[9] = (byte)(message.Length >> 16);
        payload[10] = (byte)(message.Length >> 8);
        payload[11] = (byte)message.Length;

        return payload;
    }
}
/// /// /// 