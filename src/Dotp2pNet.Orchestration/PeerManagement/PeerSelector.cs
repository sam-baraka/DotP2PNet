using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Dotp2pNet.Orchestration.PeerManagement;

/// <summary>
/// Implements peer selection strategies for the P2P network.
/// </summary>
/// <remarks>
/// This class implements the BitTorrent tit-for-tat strategy:
/// 
/// 1. **Regular Unchoking**: Unchoke the top N peers by upload rate
///    - Rewards peers that upload to us
///    - Re-evaluated every 10 seconds
///    - Typically N = 3 for most clients
/// 
/// 2. **Optimistic Unchoking**: Randomly unchoke one additional peer
///    - Gives new peers a chance to prove themselves
///    - Helps discover better peers
///    - Rotates every 30 seconds
/// 
/// 3. **Peer Selection**: Choose peers with best reputation
///    - Prioritize peers with high upload rates
///    - Avoid peers with high hash failure rates
///    - Balance exploration vs exploitation
/// 
/// This strategy ensures fairness (tit-for-tat) while still allowing
/// discovery of new good peers (optimistic unchoking).
/// </remarks>
public class PeerSelector : IPeerSelector
{
    private readonly ILogger<PeerSelector> _logger;
    private readonly Random _random;
    private byte[]? _currentOptimisticPeer;
    private DateTime _lastOptimisticRotation;
    private DateTime _lastChokingEvaluation;

    // Configuration constants
    private const int OptimisticRotationSeconds = 30;
    private const int ChokingEvaluationSeconds = 10;
    private const double MinimumReputationScore = 20.0; // Don't connect to peers below this score

    public PeerSelector(ILogger<PeerSelector> logger)
    {
        _logger = logger;
        _random = new Random();
        _lastOptimisticRotation = DateTime.UtcNow;
        _lastChokingEvaluation = DateTime.UtcNow;
    }

    /// <inheritdoc/>
    public List<PeerInfo> SelectPeersToConnect(
        List<PeerInfo> availablePeers,
        List<PeerInfo> currentConnections,
        int maxConnections)
    {
        if (availablePeers == null || availablePeers.Count == 0)
        {
            _logger.LogDebug("No available peers to select from");
            return new List<PeerInfo>();
        }

        // Calculate how many more connections we can make
        var slotsAvailable = maxConnections - currentConnections.Count;
        if (slotsAvailable <= 0)
        {
            _logger.LogDebug("Already at maximum connections ({MaxConnections})", maxConnections);
            return new List<PeerInfo>();
        }

        // Get set of currently connected peer IDs for quick lookup
        var connectedPeerIds = new HashSet<byte[]>(
            currentConnections.Select(p => p.PeerId),
            new ByteArrayComparer());

        // Filter out peers we're already connected to and stale peers
        var candidatePeers = availablePeers
            .Where(p => !connectedPeerIds.Contains(p.PeerId))
            .Where(p => !p.IsStale)
            .Where(p => p.Validate())
            .Where(p => p.Stats.ReputationScore >= MinimumReputationScore)
            .ToList();

        if (candidatePeers.Count == 0)
        {
            _logger.LogDebug("No suitable candidate peers after filtering");
            return new List<PeerInfo>();
        }

        // Sort by reputation score (descending) and then by download rate
        var selectedPeers = candidatePeers
            .OrderByDescending(p => p.Stats.ReputationScore)
            .ThenByDescending(p => p.Stats.DownloadRate)
            .ThenBy(p => p.Stats.HashFailureRate)
            .Take(slotsAvailable)
            .ToList();

        _logger.LogInformation(
            "Selected {Count} peers to connect from {Available} candidates (reputation scores: {Scores})",
            selectedPeers.Count,
            candidatePeers.Count,
            string.Join(", ", selectedPeers.Select(p => $"{p.Stats.ReputationScore:F1}")));

        return selectedPeers;
    }

    /// <inheritdoc/>
    public List<byte[]> SelectPeersToUnchoke(
        List<PeerInfo> connectedPeers,
        int uploadSlots)
    {
        if (connectedPeers == null || connectedPeers.Count == 0)
        {
            return new List<byte[]>();
        }

        // Check if it's time to re-evaluate choking
        var now = DateTime.UtcNow;
        var timeSinceLastEvaluation = (now - _lastChokingEvaluation).TotalSeconds;
        
        if (timeSinceLastEvaluation < ChokingEvaluationSeconds)
        {
            // Not time to re-evaluate yet, return empty list to indicate no change
            return new List<byte[]>();
        }

        _lastChokingEvaluation = now;

        // Reserve one slot for optimistic unchoking
        var regularSlots = Math.Max(1, uploadSlots - 1);

        // Select top peers by upload rate (tit-for-tat)
        // Peers who upload to us get priority
        var regularUnchoked = connectedPeers
            .Where(p => p.Stats.ConnectedAt != null) // Only consider connected peers
            .OrderByDescending(p => p.Stats.UploadRate)
            .ThenByDescending(p => p.Stats.BytesUploaded)
            .Take(regularSlots)
            .Select(p => p.PeerId)
            .ToList();

        // Add optimistic unchoke peer
        var optimisticPeer = SelectOptimisticUnchoke(connectedPeers, regularUnchoked);
        if (optimisticPeer != null)
        {
            regularUnchoked.Add(optimisticPeer);
        }

        _logger.LogDebug(
            "Selected {Count} peers to unchoke ({Regular} regular + {Optimistic} optimistic)",
            regularUnchoked.Count,
            regularSlots,
            optimisticPeer != null ? 1 : 0);

        return regularUnchoked;
    }

    /// <inheritdoc/>
    public byte[]? SelectOptimisticUnchoke(
        List<PeerInfo> connectedPeers,
        List<byte[]> currentlyUnchoked)
    {
        if (connectedPeers == null || connectedPeers.Count == 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var timeSinceLastRotation = (now - _lastOptimisticRotation).TotalSeconds;

        // Check if it's time to rotate the optimistic unchoke
        if (_currentOptimisticPeer != null && timeSinceLastRotation < OptimisticRotationSeconds)
        {
            // Check if the current optimistic peer is still connected
            var stillConnected = connectedPeers.Any(p => p.PeerId.SequenceEqual(_currentOptimisticPeer));
            if (stillConnected)
            {
                return _currentOptimisticPeer;
            }
        }

        // Time to select a new optimistic unchoke peer
        _lastOptimisticRotation = now;

        // Get set of currently unchoked peers for quick lookup
        var unchokedSet = new HashSet<byte[]>(currentlyUnchoked, new ByteArrayComparer());

        // Find peers that are currently choked and interested
        var chokedPeers = connectedPeers
            .Where(p => !unchokedSet.Contains(p.PeerId))
            .Where(p => p.Stats.ConnectedAt != null)
            .ToList();

        if (chokedPeers.Count == 0)
        {
            _logger.LogDebug("No choked peers available for optimistic unchoking");
            _currentOptimisticPeer = null;
            return null;
        }

        // Prefer peers we haven't uploaded to yet (new peers)
        var newPeers = chokedPeers.Where(p => p.Stats.BytesUploaded == 0).ToList();
        var candidatePool = newPeers.Count > 0 ? newPeers : chokedPeers;

        // Randomly select one peer for optimistic unchoking
        var selectedPeer = candidatePool[_random.Next(candidatePool.Count)];
        _currentOptimisticPeer = selectedPeer.PeerId;

        _logger.LogInformation(
            "Selected peer {PeerId} for optimistic unchoking (from {Count} candidates)",
            selectedPeer.ToString(),
            candidatePool.Count);

        return _currentOptimisticPeer;
    }

    /// <summary>
    /// Helper class to compare byte arrays for equality.
    /// Used in HashSet operations for peer ID comparisons.
    /// </summary>
    private class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y)
        {
            if (x == null || y == null)
                return x == y;
            return x.SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            if (obj == null || obj.Length < 4)
                return 0;
            return BitConverter.ToInt32(obj, 0);
        }
    }
}
