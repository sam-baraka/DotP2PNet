using System.Buffers;
using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;

namespace Dotp2pNet.Networking.Framing;

/// <summary>
/// Implements message framing for the P2P protocol using length-prefix framing.
/// </summary>
/// <remarks>
/// TCP is a stream-oriented protocol, meaning data arrives as a continuous stream of bytes
/// without inherent message boundaries. To solve this, we use length-prefix framing:
/// 
/// Wire Format: [4 bytes: length][1 byte: type][N bytes: payload]
/// 
/// - Length (4 bytes, big-endian): Total size of type + payload
/// - Type (1 byte): MessageType enum value
/// - Payload (N bytes): Message-specific data
/// 
/// This class handles:
/// 1. Serialization: Converting PeerMessage objects to framed byte arrays
/// 2. Deserialization: Parsing byte streams into PeerMessage objects
/// 3. Partial reads: Buffering incomplete messages until full message arrives
/// 4. Validation: Rejecting malformed messages with invalid length or type
/// </remarks>
public class MessageFramer : IMessageFramer
{
    private const int LengthPrefixSize = 4;
    private const int MessageTypeSize = 1;
    private const int HeaderSize = LengthPrefixSize + MessageTypeSize;
    private const int MaxMessageSize = 16 * 1024 * 1024; // 16 MB max message size

    private readonly byte[] _buffer;
    private int _bufferPosition;
    private int _bufferLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageFramer"/> class.
    /// </summary>
    /// <param name="bufferSize">Size of the internal buffer for handling partial reads. Default is 64 KB.</param>
    public MessageFramer(int bufferSize = 65536)
    {
        if (bufferSize < HeaderSize)
        {
            throw new ArgumentException($"Buffer size must be at least {HeaderSize} bytes", nameof(bufferSize));
        }

        _buffer = new byte[bufferSize];
        _bufferPosition = 0;
        _bufferLength = 0;
    }

    /// <summary>
    /// /// Frames a message by adding length prefix and type identifier.
    /// </s
    /// <param name="messageType">The type of message being sent.</param>
    /// <param name="payload">The message payload bytes.</param>
    /// <returns>Framed message ready to send over the wire.</returns>
    /// <exception cref="ArgumentNullException">Thrown when payload is null.</exception>
    /// <exception cref="ArgumentException">Thrown when payload exceeds maximum size.</exception>
    public byte[] FrameMessage(byte messageType, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Calculate total message length (type + payload)
        int messageLength = MessageTypeSize + payload.Length;

        if (messageLength > MaxMessageSize)
        {
            throw new ArgumentException(
                $"Message size {messageLength} exceeds maximum allowed size {MaxMessageSize}",
                nameof(payload));
        }

        // Allocate buffer for framed message
        byte[] framedMessage = new byte[LengthPrefixSize + messageLength];

        // Write length prefix (big-endian)
        framedMessage[0] = (byte)(messageLength >> 24);
        framedMessage[1] = (byte)(messageLength >> 16);
        framedMessage[2] = (byte)(messageLength >> 8);
        framedMessage[3] = (byte)messageLength;

        // Write message type
        framedMessage[4] = messageType;

        // Write payload
        Array.Copy(payload, 0, framedMessage, 5, payload.Length);

        return framedMessage;
    }

    /// <summary>
    /// Parses incoming bytes from a stream and extracts complete messages.
    /// </summary>
    /// <param name="buffer">Buffer containing incoming stream data.</param>
    /// <param name="bytesRead">Number of bytes read into the buffer.</param>
    /// <param name="messages">Output list of complete messages extracted.</param>
    /// <returns>Number of bytes consumed from the buffer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when buffer is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when bytesRead is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a malformed message is detected.</exception>
    public int ParseMessages(
        byte[] buffer,
        int bytesRead,
        out List<(byte messageType, byte[] payload)> messages)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (bytesRead < 0 || bytesRead > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytesRead),
                $"bytesRead must be between 0 and {buffer.Length}");
        }

        messages = new List<(byte, byte[])>();

        // Copy new data into internal buffer
        int bytesToCopy = Math.Min(bytesRead, _buffer.Length - _bufferLength);
        if (bytesToCopy < bytesRead)
        {
            throw new InvalidOperationException(
                $"Internal buffer overflow: cannot fit {bytesRead} bytes");
        }

        Array.Copy(buffer, 0, _buffer, _bufferLength, bytesToCopy);
        _bufferLength += bytesToCopy;

        // Process complete messages from buffer
        while (TryExtractMessage(out var messageType, out var payload))
        {
            messages.Add((messageType, payload));
        }

        return bytesToCopy;
    }

    /// <summary>
    /// Attempts to extract a complete message from the internal buffer.
    /// </summary>
    /// <param name="messageType">The extracted message type.</param>
    /// <param name="payload">The extracted message payload.</param>
    /// <returns>True if a complete message was extracted; otherwise, false.</returns>
    private bool TryExtractMessage(out byte messageType, out byte[] payload)
    {
        messageType = 0;
        payload = Array.Empty<byte>();

        // Need at least the header to proceed
        if (_bufferLength - _bufferPosition < HeaderSize)
        {
            return false;
        }

        // Read length prefix (big-endian)
        int messageLength = (_buffer[_bufferPosition] << 24)
                          | (_buffer[_bufferPosition + 1] << 16)
                          | (_buffer[_bufferPosition + 2] << 8)
                          | _buffer[_bufferPosition + 3];

        // Validate message length
        if (messageLength < MessageTypeSize)
        {
            throw new InvalidOperationException(
                $"Invalid message length {messageLength}: must be at least {MessageTypeSize}");
        }

        if (messageLength > MaxMessageSize)
        {
            throw new InvalidOperationException(
                $"Message length {messageLength} exceeds maximum allowed size {MaxMessageSize}");
        }

        // Check if we have the complete message
        int totalMessageSize = LengthPrefixSize + messageLength;
        if (_bufferLength - _bufferPosition < totalMessageSize)
        {
            // Incomplete message, wait for more data
            return false;
        }

        // Extract message type
        messageType = _buffer[_bufferPosition + LengthPrefixSize];

        // Validate message type
        if (!Enum.IsDefined(typeof(MessageType), messageType))
        {
            throw new InvalidOperationException(
                $"Invalid message type {messageType}: not a recognized MessageType value");
        }

        // Extract payload
        int payloadLength = messageLength - MessageTypeSize;
        if (payloadLength > 0)
        {
            payload = new byte[payloadLength];
            Array.Copy(
                _buffer,
                _bufferPosition + HeaderSize,
                payload,
                0,
                payloadLength);
        }

        // Move position forward
        _bufferPosition += totalMessageSize;

        // Compact buffer if we've consumed enough data
        if (_bufferPosition > _buffer.Length / 2)
        {
            CompactBuffer();
        }

        return true;
    }

    /// <summary>
    /// Compacts the internal buffer by removing consumed data.
    /// </summary>
    private void CompactBuffer()
    {
        if (_bufferPosition == 0)
        {
            return;
        }

        int remainingBytes = _bufferLength - _bufferPosition;
        if (remainingBytes > 0)
        {
            Array.Copy(_buffer, _bufferPosition, _buffer, 0, remainingBytes);
        }

        _bufferLength = remainingBytes;
        _bufferPosition = 0;
    }

    /// <summary>
    /// Resets the internal buffer state.
    /// </summary>
    /// <remarks>
    /// Useful when starting a new connection or recovering from an error.
    /// </remarks>
    public void Reset()
    {
        _bufferPosition = 0;
        _bufferLength = 0;
        Array.Clear(_buffer, 0, _buffer.Length);
    }
}
