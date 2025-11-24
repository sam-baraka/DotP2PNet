using System.Collections.Concurrent;
using Dotp2pNet.Core.ErrorHandling;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Networking.PieceExchange;

/// <summary>
/// Tracks outstanding piece requests and detects timeouts.
/// </summary>
/// <remarks>
/// <para>
/// PieceRequestTracker manages piece requests to ensure they complete within 30 seconds:
/// </para>
/// <list type="bullet">
/// <item><description><b>Request Tracking:</b> Records when each piece request is sent</description></item>
/// <item><description><b>Timeout Detection:</b> Identifies requests that haven't completed in 30 seconds</description></item>
/// <item><description><b>Automatic Cleanup:</b> Removes completed or timed-out requests</description></item>
/// <item><description><b>Thread-Safe:</b> Uses ConcurrentDictionary for safe concurrent access</description></item>
/// </list>
/// 
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// var tracker = new PieceRequestTracker(logger);
/// 
/// // Track a new request
/// tracker.TrackRequest(peerId, pieceIndex);
/// 
/// // Complete a request when piece received
/// tracker.CompleteRequest(peerId, pieceIndex);
/// 
/// // Check for timeouts periodically
/// var timedOut = tracker.GetTimedOutRequests();
/// foreach (var (peerId, pieceIndex) in timedOut)
/// {
///     // Re-request from different peer
/// }
/// </code>
/// </remarks>
public class PieceRequestTracker
{
    private readonly ConcurrentDictionary<(string peerId, int pieceIndex), TimeoutTracker> _requests;
    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="PieceRequestTracker"/> class.
    /// </summary>
    /// <param name="logger">Logger for tracking events.</param>
    /// <param name="requestTimeout">Timeout duration for piece requests. Defaults to 30 seconds.</param>
    public PieceRequestTracker(
        ILogger logger,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _requests = new ConcurrentDictionary<(string, int), TimeoutTracker>();
    }

    /// <summary>
    /// Tracks a new piece request.
    /// </summary>
    /// <param name="peerId">The peer ID the request was sent to.</param>
    /// <param name="pieceIndex">The index of the requested piece.</param>
    /// <remarks>
    /// If a request for the same piece from the same peer already exists, it will be replaced.
    /// </remarks>
    public void TrackRequest(string peerId, int pieceIndex)
    {
        ArgumentNullException.ThrowIfNull(peerId);

        if (pieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), "Piece index must be non-negative");
        }

        var key = (peerId, pieceIndex);
        var tracker = new TimeoutTracker(_requestTimeout);

        _requests[key] = tracker;

        _logger.LogDebug(
            "Tracking piece request: peer={PeerId}, piece={PieceIndex}, timeout={Timeout}",
            peerId,
            pieceIndex,
            _requestTimeout);
    }

    /// <summary>
    /// Marks a piece request as completed.
    /// </summary>
    /// <param name="peerId">The peer ID the request was sent to.</param>
    /// <param name="pieceIndex">The index of the completed piece.</param>
    /// <returns>True if the request was found and removed, false otherwise.</returns>
    public bool CompleteRequest(string peerId, int pieceIndex)
    {
        ArgumentNullException.ThrowIfNull(peerId);

        var key = (peerId, pieceIndex);
        
        if (_requests.TryRemove(key, out var tracker))
        {
            var elapsed = tracker.GetTimeSinceLastActivity();
            
            _logger.LogDebug(
                "Completed piece request: peer={PeerId}, piece={PieceIndex}, elapsed={Elapsed}",
                peerId,
                pieceIndex,
                elapsed);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all requests that have timed out.
    /// </summary>
    /// <returns>List of timed-out requests (peerId, pieceIndex).</returns>
    /// <remarks>
    /// This method does not remove the timed-out requests. Call CompleteRequest or RemoveRequest
    /// to remove them after handling the timeout.
    /// </remarks>
    public List<(string peerId, int pieceIndex)> GetTimedOutRequests()
    {
        var timedOut = new List<(string, int)>();

        foreach (var kvp in _requests)
        {
            if (kvp.Value.HasTimedOut())
            {
                timedOut.Add(kvp.Key);

                _logger.LogWarning(
                    "Piece request timed out: peer={PeerId}, piece={PieceIndex}, elapsed={Elapsed}",
                    kvp.Key.peerId,
                    kvp.Key.pieceIndex,
                    kvp.Value.GetTimeSinceLastActivity());
            }
        }

        return timedOut;
    }

    /// <summary>
    /// Removes a specific request without marking it as completed.
    /// </summary>
    /// <param name="peerId">The peer ID.</param>
    /// <param name="pieceIndex">The piece index.</param>
    /// <returns>True if the request was found and removed, false otherwise.</returns>
    public bool RemoveRequest(string peerId, int pieceIndex)
    {
        ArgumentNullException.ThrowIfNull(peerId);

        var key = (peerId, pieceIndex);
        return _requests.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes all requests for a specific peer.
    /// </summary>
    /// <param name="peerId">The peer ID.</param>
    /// <returns>The number of requests removed.</returns>
    /// <remarks>
    /// This is typically called when a peer disconnects.
    /// </remarks>
    public int RemoveAllRequestsForPeer(string peerId)
    {
        ArgumentNullException.ThrowIfNull(peerId);

        var keysToRemove = _requests.Keys
            .Where(k => k.peerId == peerId)
            .ToList();

        int removed = 0;
        foreach (var key in keysToRemove)
        {
            if (_requests.TryRemove(key, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation(
                "Removed {Count} pending requests for peer {PeerId}",
                removed,
                peerId);
        }

        return removed;
    }

    /// <summary>
    /// Gets the number of outstanding requests.
    /// </summary>
    public int OutstandingRequestCount => _requests.Count;

    /// <summary>
    /// Gets the number of outstanding requests for a specific peer.
    /// </summary>
    /// <param name="peerId">The peer ID.</param>
    /// <returns>The number of outstanding requests for the peer.</returns>
    public int GetOutstandingRequestCountForPeer(string peerId)
    {
        ArgumentNullException.ThrowIfNull(peerId);

        return _requests.Keys.Count(k => k.peerId == peerId);
    }

    /// <summary>
    /// Clears all tracked requests.
    /// </summary>
    public void Clear()
    {
        var count = _requests.Count;
        _requests.Clear();

        if (count > 0)
        {
            _logger.LogInformation("Cleared {Count} tracked piece requests", count);
        }
    }
}