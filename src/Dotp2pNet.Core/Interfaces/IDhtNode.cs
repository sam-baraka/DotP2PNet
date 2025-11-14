using System.Net;

namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Represents a node in the Distributed Hash Table (DHT) network.
/// </summary>
/// <remarks>
/// Implements the Kademlia DHT protocol (BEP 5) for decentralized peer discovery.
/// DHT allows peers to find each other without relying on centralized trackers.
/// Uses XOR distance metric and k-bucket routing table for efficient lookups.
/// </remarks>
public interface IDhtNode
{
    /// <summary>
    /// Gets the unique 160-bit identifier for this DHT node.
    /// </summary>
    byte[] NodeId { get; }

    /// <summary>
    /// Gets a value indicating whether the DHT node is currently running.
    /// /// </summary>
    bool IsRunning { get; }/// <summary>
    /// Gets the port this DHT node is listening on.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Starts the DHT node and begins listening for incoming requests.
    /// </summary>
    /// <param name="port">The UDP port to listen on.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StartAsync(int port, CancellationToken ct = default);

    /// <summary>
    /// Stops the DHT node and closes all connections.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Bootstraps the DHT node by connecting to known bootstrap nodes.
    /// </summary>
    /// <param name="bootstrapNodes">List of known DHT nodes to connect to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Bootstrapping is required to join the DHT network. The node will
    /// query bootstrap nodes to populate its routing table.
    /// </remarks>
    /// Task BootstrapAsynct<Models.PeerInfo> bootstrapNodes, CancellationToken ct = default);

    /// <summary>
    /// Finds peers that are sharing a specific file (info hash).
    /// </summary>
    /// <param name="infoHash">The 20-byte info hash of the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of peers sharing the file.</returns>
    /// <remarks>
    /// Performs an iterative lookup in the DHT to find peers.
    /// Uses get_peers RPC method to query nodes.
    /// </remarks>
    Task<List<Models.PeerInfo>> FindPeersAsync(byte[] infoHash, CancellationToken ct = default);

    /// <summary>
    /// Announces that this peer is sharing a file (info hash).
    /// </summary>
    /// <param name="infoHash">The 20-byte info hash of the file.</param>
    /// <param name="port">The port this peer is listening on for BitTorrent connections.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Stores the peer's information in the DHT so other peers can find it.
    /// Uses announce_peer RPC method.
    /// </remarks>
    Task AnnouncePeerAsync(byte[] infoHash, int port, CancellationToken ct = default);

    /// <summary>
    /// Pings a remote DHT node to check if it's alive.
    /// </summary>
    /// <param name="nodeId">The node ID of the remote node.</param>
    /// <param name="address">The IP address of the remote node.</param>
    /// <param name="port">The port of the remote node.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the node responded; otherwise, false.</returns>
    Task<bool> PingAsync(byte[] nodeId, IPAddress address, int port, CancellationToken ct = default);

    /// <summary>
    /// Finds the closest nodes to a target ID.
    /// </summary>
    /// <param name="targetId">The 160-bit target ID to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of closest nodes to the target.</returns>
    /// <remarks>
    /// Uses find_node RPC method to query nodes.
    /// Returns up to K (typically 8) closest nodes.
    /// </remarks>
    Task<List<Models.PeerInfo>> FindNodeAsync(byte[] targetId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current number of nodes in the routing table.
    /// </summary>
    /// <returns>The number of known nodes.</returns>
    int GetRoutingTableSize();
}
