namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Represents a connection to a single remote peer in the P2P network.
/// </summary>
/// <remarks>
/// Manages the lifecycle of a peer connection including handshake, message exchange,
/// and connection state tracking.
/// </remarks>
public interface IPeerConnection : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this peer.
    /// </summary>
    string PeerId { get; }

    /// <summary>
    /// Gets a value indicating whether the connection is currently active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets a value indicating whether we are choking this peer (not sending pieces).
    /// </summary>
    bool AmChoking { get; }

    /// <summary>
    /// Gets a value indicating whether this peer is choking us (not sending pieces).
    /// </summary>
    bool PeerChoking { get; }

    /// <summary>
    /// Gets a value indicating whether we are interested in pieces from this peer.
    /// </summary>
    bool AmInterested { get; }

    /// <summary>
    /// Gets a value indicating whether this peer is interested in our pieces.
    /// </summary>
    bool PeerInterested { get; }

    /// <summary>
    /// Performs the BitTorrent handshake with the remote peer.
    /// </summary>
    /// <param name="infoHash">The 20-byte SHA1 hash of the torrent info dictionary.</param>
    /// <param name="peerId">Our 20-byte peer ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Task that completes when handshake is successful.</returns>
    Task HandshakeAsync(byte[] infoHash, byte[] peerId, CancellationToken ct);

    /// <summary>
    /// Sends a message to the remote peer.
    /// </summary>
    /// <param name="messageType">The type of message to send.</param>
    /// <param name="payload">The message payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendMessageAsync(byte messageType, byte[] payload, CancellationToken ct);

    /// <summary>
    /// Receives the next message from the remote peer.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple containing message type and payload.</returns>
    Task<(byte messageType, byte[] payload)> ReceiveMessageAsync(CancellationToken ct);

    /// <summary>
    /// Closes the connection to the peer.
    /// </summary>
    Task CloseAsync();
}
