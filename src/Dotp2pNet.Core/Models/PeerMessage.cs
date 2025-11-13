using System.ComponentModel.DataAnnotations;

namespace Dotp2pNet.Core.Models;

/// <summary>
/// Base class for all peer-to-peer protocol messages.
/// </summary>
/// <remarks>
/// All messages in the P2P protocol inherit from this base class.
/// The Type property identifies the specific message type for parsing and routing.
/// Derived classes add specific payload data for their message type.
/// </remarks>
public abstract class PeerMessage
{
    /// <summary>
    /// Gets or sets the type of this message.
    /// </summary>
    public MessageType Type { get; set; }

    /// <summary>
    /// Validates the message data to ensure integrity.
    /// </summary>
    /// <returns>True if the message is valid; otherwise, false.</returns>
    public abstract bool Validate();
}

/// <summary>
/// Handshake message exchanged when establishing a peer connection.
/// </summary>
/// <remarks>
/// The handshake is the first message sent when connecting to a peer.
/// It verifies that both peers are participating in the same torrent (via InfoHash)
/// and establishes peer identities (via PeerId).
/// </remarks>
public class HandshakeMessage : PeerMessage
{
    /// <summary>
    /// Gets or sets the SHA-1 hash of the torrent's info dictionary.
    /// Used to verify both peers are sharing the same torrent.
    /// </summary>
    [Required]
    public byte[] InfoHash { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the unique identifier for this peer.
    /// Typically 20 bytes, often starting with client identifier.
    /// </summary>
    [Required]
    public byte[] PeerId { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Initializes a new instance of the <see cref="HandshakeMessage"/> class.
    /// </summary>
    public HandshakeMessage()
    {
        Type = MessageType.Handshake;
    }

    /// <summary>
    /// Validates the handshake message.
    /// </summary>
    /// <returns>True if InfoHash is 20 bytes and PeerId is 20 bytes; otherwise, false.</returns>
    public override bool Validate()
    {
        return InfoHash.Length == 20 && PeerId.Length == 20;
    }
}

/// <summary>
/// Bitfield message indicating which pieces a peer has available.
/// </summary>
/// <remarks>
/// Sent immediately after the handshake to communicate piece availability.
/// Each bit represents whether the peer has that piece (1) or not (0).
/// The bitfield is packed into bytes, with the high bit of the first byte
/// representing piece 0.
/// </remarks>
public class BitfieldMessage : PeerMessage
{
    /// <summary>
    /// Gets or sets the bitfield data representing piece availability.
    /// Each bit indicates whether the peer has that piece.
    /// </summary>
    [Required]
    public byte[] Bitfield { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Initializes a new instance of the <see cref="BitfieldMessage"/> class.
    /// </summary>
    public BitfieldMessage()
    {
        Type = MessageType.Bitfield;
    }

    /// <summary>
    /// Validates the bitfield message.
    /// </summary>
    /// <returns>True if the bitfield is not empty; otherwise, false.</returns>
    public override bool Validate()
    {
        return Bitfield.Length > 0;
    }
}

/// <summary>
/// Request message asking for a specific piece or piece block.
/// </summary>
/// <remarks>
/// Peers send request messages to download pieces from other peers.
/// To avoid overwhelming peers, typically only 5 requests are outstanding at once.
/// Requests are for blocks within pieces (typically 16 KB blocks).
/// </remarks>
public class RequestMessage : PeerMessage
{
    /// <summary>
    /// Gets or sets the zero-based index of the requested piece.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int PieceIndex { get; set; }

    /// <summary>
    /// Gets or sets the byte offset within the piece.
    /// Used for requesting sub-piece blocks.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes requested.
    /// Typically 16,384 bytes (16 KB) for standard block size.
    /// </summary>
    [Range(1, 131072)] // Max 128 KB per request
    public int Length { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMessage"/> class.
    /// </summary>
    public RequestMessage()
    {
        Type = MessageType.Request;
    }

    /// <summary>
    /// Validates the request message.
    /// </summary>
    /// <returns>True if all fields are within valid ranges; otherwise, false.</returns>
    public override bool Validate()
    {
        return PieceIndex >= 0 
            && Offset >= 0 
            && Length > 0 
            && Length <= 131072; // Max 128 KB
    }
}

/// <summary>
/// Piece message containing actual file data.
/// </summary>
/// <remarks>
/// Sent in response to a Request message. Contains the actual piece data
/// that was requested. The receiver must verify the data against the expected
/// hash before accepting it.
/// </remarks>
public class PieceMessage : PeerMessage
{
    /// <summary>
    /// Gets or sets the zero-based index of the piece.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int PieceIndex { get; set; }

    /// <summary>
    /// Gets or sets the byte offset within the piece.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    /// <summary>
    /// Gets or sets the actual piece data.
    /// </summary>
    [Required]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Initializes a new instance of the <see cref="PieceMessage"/> class.
    /// </summary>
    public PieceMessage()
    {
        Type = MessageType.Piece;
    }

    /// <summary>
    /// Validates the piece message.
    /// </summary>
    /// <returns>True if all fields are valid and data is not empty; otherwise, false.</returns>
    public override bool Validate()
    {
        return PieceIndex >= 0 
            && Offset >= 0 
            && Data.Length > 0 
            && Data.Length <= 131072; // Max 128 KB
    }
}

/// <summary>
/// Have message announcing that a peer has acquired a new piece.
/// </summary>
/// <remarks>
/// Broadcast to all connected peers when a piece is successfully downloaded
/// and verified. Allows peers to update their view of piece availability.
/// </remarks>
public class HaveMessage : PeerMessage
{
    /// <summary>
    /// Gets or sets the zero-based index of the piece the peer now has.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int PieceIndex { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HaveMessage"/> class.
    /// </summary>
    public HaveMessage()
    {
        Type = MessageType.Have;
    }

    /// <summary>
    /// Validates the have message.
    /// </summary>
    /// <returns>True if the piece index is non-negative; otherwise, false.</returns>
    public override bool Validate()
    {
        return PieceIndex >= 0;
    }
}

/// <summary>
/// Interested message indicating the peer wants to download from the remote peer.
/// </summary>
/// <remarks>
/// Sent when a peer has pieces that the local peer needs.
/// The remote peer may then unchoke this peer to allow downloads.
/// </remarks>
public class InterestedMessage : PeerMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InterestedMessage"/> class.
    /// </summary>
    public InterestedMessage()
    {
        Type = MessageType.Interested;
    }

    /// <summary>
    /// Validates the interested message.
    /// </summary>
    /// <returns>Always returns true as this message has no payload.</returns>
    public override bool Validate()
    {
        return true;
    }
}

/// <summary>
/// NotInterested message indicating the peer doesn't need anything from the remote peer.
/// </summary>
/// <remarks>
/// Sent when the remote peer has no pieces that the local peer needs.
/// The remote peer may then choke this peer to save bandwidth.
/// </remarks>
public class NotInterestedMessage : PeerMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotInterestedMessage"/> class.
    /// </summary>
    public NotInterestedMessage()
    {
        Type = MessageType.NotInterested;
    }

    /// <summary>
    /// Validates the not interested message.
    /// </summary>
    /// <returns>Always returns true as this message has no payload.</returns>
    public override bool Validate()
    {
        return true;
    }
}

/// <summary>
/// Choke message indicating the peer will not fulfill requests.
/// </summary>
/// <remarks>
/// Used for bandwidth management. When choked, the remote peer should not
/// send any request messages. Part of BitTorrent's tit-for-tat strategy.
/// </remarks>
public class ChokeMessage : PeerMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChokeMessage"/> class.
    /// </summary>
    public ChokeMessage()
    {
        Type = MessageType.Choke;
    }

    /// <summary>
    /// Validates the choke message.
    /// </summary>
    /// <returns>Always returns true as this message has no payload.</returns>
    public override bool Validate()
    {
        return true;
    }
}

/// <summary>
/// Unchoke message indicating the peer will fulfill requests.
/// </summary>
/// <remarks>
/// Allows the remote peer to start sending request messages.
/// Typically sent to peers that are providing good upload rates.
/// </remarks>
public class UnchokeMessage : PeerMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnchokeMessage"/> class.
    /// </summary>
    public UnchokeMessage()
    {
        Type = MessageType.Unchoke;
    }

    /// <summary>
    /// Validates the unchoke message.
    /// </summary>
    /// <returns>Always returns true as this message has no payload.</returns>
    public override bool Validate()
    {
        return true;
    }
}

/// <summary>
/// KeepAlive message to prevent connection timeout during idle periods.
/// </summary>
/// <remarks>
/// Sent periodically (typically every 2 minutes) when no other messages
/// are being exchanged. Prevents the connection from being closed due to inactivity.
/// </remarks>
public class KeepAliveMessage : PeerMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeepAliveMessage"/> class.
    /// </summary>
    public KeepAliveMessage()
    {
        Type = MessageType.KeepAlive;
    }

    /// <summary>
    /// Validates the keep alive message.
    /// </summary>
    /// <returns>Always returns true as this message has no payload.</returns>
    public override bool Validate()
    {
        return true;
    }
}
