using Dotp2pNet.Core.Interfaces;
using Dotp2pNet.Core.Models;

namespace Dotp2pNet.Networking.Framing;

/// <summary>
/// High-level service that combines message framing and serialization.
/// </summary>
/// <remarks>
/// This service provides a complete solution for converting between PeerMessage objects
/// and wire format bytes. It handles:
/// 1. Serializing PeerMessage objects to framed byte arrays ready to send
/// 2. Parsing incoming byte streams into PeerMessage objects
/// 3. Managing partial reads and buffer state
/// 4. Validating messages for correctness
/// 
/// Usage:
/// - Call FrameMessageAsync to prepare a message for sending
/// - Call ReadMessageAsync to parse incoming data from a stream
/// </remarks>
public class MessageFramingService
{
    private readonly IMessageFramer _framer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageFramingService"/> class.
    /// </summary>
    /// <param name="framer">The message framer to use. If null, creates a default MessageFramer.</param>
    public MessageFramingService(IMessageFramer? framer = null)
    {
        _framer = framer ?? new MessageFramer();
    }

    /// <summary>
    /// Frames a PeerMessage for transmission over the network.
    /// </summary>
    /// <param name="message">The message to frame.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the framed message bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="ArgumentException">Thrown when message is invalid.</exception>
    public Task<byte[]> FrameMessageAsync(PeerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Serialize the message to payload bytes
        byte[] payload = MessageSerializer.Serialize(message);

        // Frame the message with length prefix and type
        byte[] framedMessage = _framer.FrameMessage((byte)message.Type, payload);

        return Task.FromResult(framedMessage);
    }

    /// <summary>
    /// Reads and parses messages from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the parsed message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a malformed message is detected.</exception>
    /// <exception cref="EndOfStreamException">Thrown when the stream ends unexpectedly.</exception>
    public async Task<PeerMessage> ReadMessageAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] buffer = new byte[16384]; // 16 KB read buffer

        while (true)
        {
            // Read data from stream
            int bytesRead = await stream.ReadAsync(buffer, cancellationToken);

            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Stream ended before a complete message was received");
            }

            // Parse messages from the buffer
            int consumed = _framer.ParseMessages(buffer, bytesRead, out var messages);

            if (messages.Count > 0)
            {
                // Deserialize the first message
                var (messageType, payload) = messages[0];
                return MessageSerializer.Deserialize(messageType, payload);
            }

            // No complete message yet, continue reading
        }
    }

    /// <summary>
    /// Reads multiple messages from a stream as they become available.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable of parsed messages.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a malformed message is detected.</exception>
    public async IAsyncEnumerable<PeerMessage> ReadMessagesAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] buffer = new byte[16384]; // 16 KB read buffer

        while (!cancellationToken.IsCancellationRequested)
        {
            // Read data from stream
            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (bytesRead == 0)
            {
                // Stream ended
                yield break;
            }

            // Parse messages from the buffer
            int consumed = _framer.ParseMessages(buffer, bytesRead, out var messages);

            // Yield each parsed message
            foreach (var (messageType, payload) in messages)
            {
                yield return MessageSerializer.Deserialize(messageType, payload);
            }
        }
    }
}
