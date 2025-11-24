using Dotp2pNet.Core.Models;

namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Defines a strategy for selecting which peers to connect to from a list of available peers.
/// </summary>
/// <remarks>
/// Peer selection is crucial for P2P performance. Good peer selection:
/// - Prioritizes peers with good reputation (high upload rates, low hash failures)
/// - Balances between exploiting known good peers and exploring new ones
/// - Implements the BitTorrent tit-for-tat strategy for fairness
/// - Uses optimistic unchoking to give new peers a chance
/// </remarks>
public interface IPeerSelector
{
    /// <summary>
    /// Selects the best peers to connect to from a list of available peers.
    /// </summary>
    /// <param name="availablePeers">The list of peers that could be connected to.</param>
    /// <param name="currentConnections">The list of peers we're currently connected to.</param>
    /// <param name="maxConnections">The maximum number of connections allowed.</param>
    /// <returns>
    /// A list of peers to connect to, ordered by preference.
    /// The list will contain at most (maxConnections - currentConnections.Count) peers.
    /// </returns>
    ///   /// <remarks>
    /// Selection criteria should include:
    /// - Peer reputation score (based on statistics)
    /// - Avoiding already connected peers
    /// - Avoiding stale peers (not seen recently)
    /// - Preferring peers with good upload rates
    /// - Avoiding peers with high hash failure rates
    /// </remarks>
    List<PeerInfo> SelectPeersToConnect(
        List<PeerInfo> availablePeers,
        List<PeerInfo> currentConnections,
        int maxConnections);

    /// <summary>
    /// Determines which peers should be choked (not sent pieces).
    /// </summary>
    /// <param name="connectedPeers">All currently connected peers with their statistics.</param>
    /// <param name="uploadSlots">The number of upload slots available (typically 4).</param>
    /// <returns>
    /// A list of peer IDs that should be unchoked (allowed to download).
    /// All other peers should be choked.
    /// </returns>
    /// <remarks>
    /// Implements the BitTorrent choking algorithm:
    /// - Unchoke the top N peers by upload rate (tit-for-tat)
    /// - Reserve one slot for optimistic unchoking (random peer)
    /// - Re-evaluate every 10 seconds for regular slots
    /// - Re-evaluate every 30 seconds for optimistic slot
    /// </remarks>
    List<byte[]> SelectPeersToUnchoke(
        List<PeerInfo> connectedPeers,
        int uploadSlots);

    /// <summary>
    /// Selects a peer for optimistic unchoking.
    /// </summary>
    /// <param name="connectedPeers">All currently connected peers.</param>
    /// <param name="currentlyUnchoked">Peers that are currently unchoked.</param>
    /// <returns>
    /// The peer ID to optimistically unchoke, or null if no suitable peer is found.
    /// </returns>
    /// <remarks>
    /// Optimistic unchoking gives new or choked peers a chance to prove themselves.
    /// This helps discover better peers and prevents starvation.
    /// Typically rotates every 30 seconds.
    /// </remarks>
    byte[]? SelectOptimisticUnchoke(
        List<PeerInfo> connectedPeers,
        List<byte[]> currentlyUnchoked);
}
