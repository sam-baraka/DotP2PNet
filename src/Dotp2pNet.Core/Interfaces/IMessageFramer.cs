namespace Dotp2pNet.Core.Interfaces;

/// <summary>
/// Handles framing and parsing of P2P protocol messages over TCP streams.
/// </summary>
/// <remarks>
/// TCP is stream-oriented, so we need to frame messages with length prefixes
/// to identify message boundaries. Format: [4 bytes: length][1 byte: type][N bytes: payload]
/// </remarks>
public interface IMessageFramer
{
    /// <summary>
    /// Frames a message by adding length prefix and type identifier.
    ///    /// </summary>
    /// <param name="messageType">The type of message being sent.</param>
    /// <param name="payload">The message payload bytes.</param>
    /// <returns>Framed message ready to send over the wire.</returns>
    byte[] FrameMessage(byte messageType, byte[] payload);

    /// <summary>
    /// Parses incoming bytes from a stream and extracts complete messages.
    /// </summary>
    /// <param name="buffer">Buffer containing incoming stream data.</param>
    /// <param name="bytesRead">Number of bytes read into the buffer.</param>
    /// <param name="messages">Output list of complete messages extracted.</param>
    /// <returns>Number of bytes consumed from the buffer.</returns>
    int ParseMessages(byte[] buffer, int bytesRead, out List<(byte messageType, byte[] payload)> messages);
}
