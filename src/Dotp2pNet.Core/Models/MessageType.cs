namespace Dotp2pNet.Core.Models;

/// <summary>
/// Defines the types of messages that can be exchanged between peers in the P2P protocol.
/// </summary>
/// <remarks>
/// Each message type serves a specific purpose in the peer-to-peer communication protocol:
/// - Handshake: Initial connection establishment and protocol negotiation
/// - Bitfield: Communicates which pieces a peer has available
/// - Request: Requests a specific piece from a peer
/// - Piece: Delivers piece data in response to a request
/// - Have: Notifies peers when a new piece is acquired
/// - Interested/NotInterested: Indicates interest in peer's pieces
/// - Choke/Unchoke: Controls upload bandwidth allocation
/// - KeepAlive: Maintains connection during idle periods
/// </remarks>
public enum MessageType : byte
{
    /// <summary>
    /// Initial handshake message exchanged when establishing a connection.
    /// Contains protocol version, info hash, and peer ID.
    /// </summary>
    Handshake = 0,

    /// <summary>
    /// Bitfield message indicating which pieces the peer has.
    /// Sent immediately after handshake.
    /// </summary>
    Bitfield = 1,

    /// <summary>
    /// Request message asking for a specific piece or piece block.
    /// Contains piece index, offset, and length.
    /// </summary>
    Request = 2,

    /// <summary>
    /// Piece message containing actual file data.
    /// Sent in response to a Request message.
    /// </summary>
    Piece = 3,

    /// <summary>
    /// Have message announcing that the peer has acquired a new piece.
    /// Broadcast to all connected peers.
    /// </summary>
    Have = 4,

    /// <summary>
    /// Interested message indicating the peer wants to download from the remote peer.
    /// </summary>
    Interested = 5,

    /// <summary>
    /// NotInterested message indicating the peer doesn't need anything from the remote peer.
    /// </summary>
    NotInterested = 6,

    /// <summary>
    /// Choke message indicating the peer will not fulfill requests.
    /// Used for bandwidth management and tit-for-tat strategy.
    /// </summary>
    Choke = 7,

    /// <summary>
    /// Unchoke message indicating the peer will fulfill requests.
    /// Allows the remote peer to start requesting pieces.
    /// </summary>
    Unchoke = 8,

    /// <summary>
    /// KeepAlive message to prevent connection timeout during idle periods.
    /// Contains no payload.
    /// </summary>
    KeepAlive = 9
}
