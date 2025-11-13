using Dotp2pNet.Core.Models;

namespace Dotp2pNet.Networking.Framing;

/// <summary>
/// Handles serialization and deserialization of PeerMessage objects to/from byte arrays.
/// </summary>
/// <remarks>
/// This class converts between strongly-typed PeerMessage objects and their wire format
/// representations. Each message type has a specific binary format defined by the protocol.
/// 
/// Message Formats:
/// - Handshake: [20 bytes: InfoHash][20 bytes: PeerId]
/// - Bitfield: [N bytes: bitfield data]
/// - Request: [4 bytes: piece index][4 bytes: offset][4 bytes: length]
/// - Piece: [4 bytes: piece index][4 bytes: offset][N bytes: data]
/// - Have: [4 bytes: piece index]
/// - Interested, NotInterested, Choke, Unchoke, KeepAlive: [no payload]
/// </remarks>
public static class MessageSerializer
{
    /// <summary>
    /// Serializes a PeerMessage to its byte array representation.
    /// </summary>
    /// <param name="message">The message to serialize.</param>
    /// <returns>Byte array containing the serialized message payload.</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="ArgumentException">Thrown when message type is not supported or message is invalid.</exception>
    public static byte[] Serialize(PeerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!message.Validate())
        {
            throw new ArgumentException("Message validation failed", nameof(message));
        }

        return message.Type switch
        {
            MessageType.Handshake => SerializeHandshake((HandshakeMessage)message),
            MessageType.Bitfield => SerializeBitfield((BitfieldMessage)message),
            MessageType.Request => SerializeRequest((RequestMessage)message),
            MessageType.Piece => SerializePiece((PieceMessage)message),
            MessageType.Have => SerializeHave((HaveMessage)message),
            MessageType.Interested => Array.Empty<byte>(),
            MessageType.NotInterested => Array.Empty<byte>(),
            MessageType.Choke => Array.Empty<byte>(),
            MessageType.Unchoke => Array.Empty<byte>(),
            MessageType.KeepAlive => Array.Empty<byte>(),
            _ => throw new ArgumentException($"Unsupported message type: {message.Type}", nameof(message))
        };
    }

    /// <summary>
    /// Deserializes a byte array into a PeerMessage object.
    /// </summary>
    ///  /// <param="messageType">The type of message to deserialize.</param>
    /// <param name="payload">The message payload bytes.</param>
    /// <returns>Deserialized PeerMessage object.</returns>
    /// <exception cref="ArgumentNullException">Thrown when payload is null.</exception>
    /// <exception cref="ArgumentException">Thrown when message type is not supported or payload is invalid.</exception>
    public static PeerMessage Deserialize(byte messageType, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!Enum.IsDefined(typeof(MessageType), messageType))
        {
            throw new ArgumentException($"Invalid message type: {messageType}", nameof(messageType));
        }

        var type = (MessageType)messageType;

        PeerMessage message = type switch
        {
            MessageType.Handshake => DeserializeHandshake(payload),
            MessageType.Bitfield => DeserializeBitfield(payload),
            MessageType.Request => DeserializeRequest(payload),
            MessageType.Piece => DeserializePiece(payload),
            MessageType.Have => DeserializeHave(payload),
            MessageType.Interested => new InterestedMessage(),
            MessageType.NotInterested => new NotInterestedMessage(),
            MessageType.Choke => new ChokeMessage(),
            MessageType.Unchoke => new UnchokeMessage(),
            MessageType.KeepAlive => new KeepAliveMessage(),
            _ => throw new ArgumentException($"Unsupported message type: {type}", nameof(messageType))
        };

        if (!message.Validate())
        {
            throw new ArgumentException($"Deserialized message failed validation: {type}", nameof(payload));
        }

        return message;
    }

    #region Serialization Methods

    private static byte[] SerializeHandshake(HandshakeMessage message)
    {
        byte[] payload = new byte[40]; // 20 bytes InfoHash + 20 bytes PeerId
        Array.Copy(message.InfoHash, 0, payload, 0, 20);
        Array.Copy(message.PeerId, 0, payload, 20, 20);
        return payload;
    }

    private static byte[] SerializeBitfield(BitfieldMessage message)
    {
        return message.Bitfield;
    }

    private static byte[] SerializeRequest(RequestMessage message)
    {
        byte[] payload = new byte[12]; // 3 x 4-byte integers

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

    private static byte[] SerializePiece(PieceMessage message)
    {
        byte[] payload = new byte[8 + message.Data.Length]; // 2 x 4-byte integers + data

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

    private static byte[] SerializeHave(HaveMessage message)
    {
        byte[] payload = new byte[4];

        // Piece index (big-endian)
        payload[0] = (byte)(message.PieceIndex >> 24);
        payload[1] = (byte)(message.PieceIndex >> 16);
        payload[2] = (byte)(message.PieceIndex >> 8);
        payload[3] = (byte)message.PieceIndex;

        return payload;
    }

    #endregion

    #region Deserialization Methods

    private static HandshakeMessage DeserializeHandshake(byte[] payload)
    {
        if (payload.Length != 40)
        {
            throw new ArgumentException(
                $"Handshake payload must be 40 bytes, got {payload.Length}",
                nameof(payload));
        }

        var message = new HandshakeMessage
        {
            InfoHash = new byte[20],
            PeerId = new byte[20]
        };

        Array.Copy(payload, 0, message.InfoHash, 0, 20);
        Array.Copy(payload, 20, message.PeerId, 0, 20);

        return message;
    }

    private static BitfieldMessage DeserializeBitfield(byte[] payload)
    {
        if (payload.Length == 0)
        {
            throw new ArgumentException("Bitfield payload cannot be empty", nameof(payload));
        }

        return new BitfieldMessage
        {
            Bitfield = payload
        };
    }

    private static RequestMessage DeserializeRequest(byte[] payload)
    {
        if (payload.Length != 12)
        {
            throw new ArgumentException(
                $"Request payload must be 12 bytes, got {payload.Length}",
                nameof(payload));
        }

        var message = new RequestMessage
        {
            PieceIndex = (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3],
            Offset = (payload[4] << 24) | (payload[5] << 16) | (payload[6] << 8) | payload[7],
            Length = (payload[8] << 24) | (payload[9] << 16) | (payload[10] << 8) | payload[11]
        };

        return message;
    }

    private static PieceMessage DeserializePiece(byte[] payload)
    {
        if (payload.Length < 8)
        {
            throw new ArgumentException(
                $"Piece payload must be at least 8 bytes, got {payload.Length}",
                nameof(payload));
        }

        var message = new PieceMessage
        {
            PieceIndex = (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3],
            Offset = (payload[4] << 24) | (payload[5] << 16) | (payload[6] << 8) | payload[7],
            Data = new byte[payload.Length - 8]
        };

        Array.Copy(payload, 8, message.Data, 0, message.Data.Length);

        return message;
    }

    private static HaveMessage DeserializeHave(byte[] payload)
    {
        if (payload.Length != 4)
        {
            throw new ArgumentException(
                $"Have payload must be 4 bytes, got {payload.Length}",
                nameof(payload));
        }

        var message = new HaveMessage
        {
            PieceIndex = (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3]
        };

        return message;
    }

    #endregion
}
 