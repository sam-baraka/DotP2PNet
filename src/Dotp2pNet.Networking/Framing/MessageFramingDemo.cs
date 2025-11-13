using Dotp2pNet.Core.Models;
using System.Text;

namespace Dotp2pNet.Networking.Framing;

/// <summary>
/// Demonstration and validation of the message framing layer.
/// </summary>
/// <remarks>
/// This class provides examples and tests to verify the message framing implementation.
/// It demonstrates:
/// 1. Serializing and framing messages
/// 2. Parsing framed messages
/// 3. Handling partial reads
/// 4. Error handling for malformed messages
/// </remarks>
public static class MessageFramingDemo
{
    /// <summary>
    /// Demonstrates basic message framing and parsing.
    /// </summary>
    public static void DemoBasicFraming()
    {
        Console.WriteLine("=== Message Framing Demo ===\n");

        var framer = new MessageFramer();

        // Example 1: Frame a simple message
        Console.WriteLine("1. Framing a Request message:");
        var requestPayload = new byte[] { 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00 };
        var framedRequest = framer.FrameMessage((byte)MessageType.Request, requestPayload);
        
        Console.WriteLine($"   Payload length: {requestPayload.Length} bytes");
        Console.WriteLine($"   Framed length: {framedRequest.Length} bytes");
        Console.WriteLine($"   Wire format: {BitConverter.ToString(framedRequest)}");
        Console.WriteLine($"   Breakdown:");
        Console.WriteLine($"     - Length prefix: {BitConverter.ToString(framedRequest, 0, 4)}");
        Console.WriteLine($"     - Message type: {framedRequest[4]} (Request)");
        Console.WriteLine($"     - Payload: {BitConverter.ToString(framedRequest, 5)}");
        Console.WriteLine();

        // Example 2: Parse the framed message
        Console.WriteLine("2. Parsing the framed message:");
        int consumed = framer.ParseMessages(framedRequest, framedRequest.Length, out var messages);
        
        Console.WriteLine($"   Bytes consumed: {consumed}");
        Console.WriteLine($"   Messages extracted: {messages.Count}");
        if (messages.Count > 0)
        {
            var (msgType, payload) = messages[0];
            Console.WriteLine($"   Message type: {msgType} ({(MessageType)msgType})");
            Console.WriteLine($"   Payload: {BitConverter.ToString(payload)}");
        }
        Console.WriteLine();

        // Example 3: Demonstrate partial reads
        Console.WriteLine("3. Handling partial reads:");
        framer.Reset();
        
        // Split the message into two parts
        int splitPoint = 8;
        byte[] part1 = framedRequest[..splitPoint];
        byte[] part2 = framedRequest[splitPoint..];
        
        Console.WriteLine($"   Part 1 ({part1.Length} bytes): {BitConverter.ToString(part1)}");
        int consumed1 = framer.ParseMessages(part1, part1.Length, out var messages1);
        Console.WriteLine($"   Messages after part 1: {messages1.Count} (incomplete)");
        
        Console.WriteLine($"   Part 2 ({part2.Length} bytes): {BitConverter.ToString(part2)}");
        int consumed2 = framer.ParseMessages(part2, part2.Length, out var messages2);
        Console.WriteLine($"   Messages after part 2: {messages2.Count} (complete!)");
        Console.WriteLine();
    }

    /// <summary>
    /// Demonstrates serialization and deserialization of all message types.
    /// </summary>
    public static void DemoMessageSerialization()
    {
        Console.WriteLine("=== Message Serialization Demo ===\n");

        // Handshake
        Console.WriteLine("1. Handshake Message:");
        var handshake = new HandshakeMessage
        {
            InfoHash = new byte[20] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
            PeerId = new byte[20] { 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40 }
        };
        DemoMessage(handshake);

        // Request
        Console.WriteLine("2. Request Message:");
        var request = new RequestMessage
        {
            PieceIndex = 42,
            Offset = 0,
            Length = 16384
        };
        DemoMessage(request);

        // Piece
        Console.WriteLine("3. Piece Message:");
        var piece = new PieceMessage
        {
            PieceIndex = 42,
            Offset = 0,
            Data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }
        };
        DemoMessage(piece);

        // Have
        Console.WriteLine("4. Have Message:");
        var have = new HaveMessage
        {
            PieceIndex = 42
        };
        DemoMessage(have);

        // Interested
        Console.WriteLine("5. Interested Message:");
        var interested = new InterestedMessage();
        DemoMessage(interested);

        Console.WriteLine();
    }

    private static void DemoMessage(PeerMessage message)
    {
        try
        {
            // Serialize
            byte[] payload = MessageSerializer.Serialize(message);
            Console.WriteLine($"   Type: {message.Type}");
            Console.WriteLine($"   Payload size: {payload.Length} bytes");
            if (payload.Length > 0 && payload.Length <= 40)
            {
                Console.WriteLine($"   Payload: {BitConverter.ToString(payload)}");
            }

            // Frame
            var framer = new MessageFramer();
            byte[] framed = framer.FrameMessage((byte)message.Type, payload);
            Console.WriteLine($"   Framed size: {framed.Length} bytes");

            // Parse
            framer.Reset();
            framer.ParseMessages(framed, framed.Length, out var messages);
            
            // Deserialize
            if (messages.Count > 0)
            {
                var (msgType, parsedPayload) = messages[0];
                var deserialized = MessageSerializer.Deserialize(msgType, parsedPayload);
                Console.WriteLine($"   Round-trip successful: {deserialized.Type == message.Type}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Error: {ex.Message}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Demonstrates error handling for malformed messages.
    /// </summary>
    public static void DemoErrorHandling()
    {
        Console.WriteLine("=== Error Handling Demo ===\n");

        var framer = new MessageFramer();

        // Test 1: Invalid length (too small)
        Console.WriteLine("1. Invalid length (0 bytes):");
        try
        {
            byte[] invalidLength = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x02 };
            framer.ParseMessages(invalidLength, invalidLength.Length, out var messages);
            Console.WriteLine("   ERROR: Should have thrown exception!");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"   ✓ Caught expected exception: {ex.Message}");
        }
        Console.WriteLine();

        // Test 2: Invalid message type
        Console.WriteLine("2. Invalid message type (99):");
        try
        {
            framer.Reset();
            byte[] invalidType = new byte[] { 0x00, 0x00, 0x00, 0x01, 99 };
            framer.ParseMessages(invalidType, invalidType.Length, out var messages);
            Console.WriteLine("   ERROR: Should have thrown exception!");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"   ✓ Caught expected exception: {ex.Message}");
        }
        Console.WriteLine();

        // Test 3: Message too large
        Console.WriteLine("3. Message too large (> 16 MB):");
        try
        {
            byte[] payload = new byte[17 * 1024 * 1024]; // 17 MB
            framer.FrameMessage((byte)MessageType.Piece, payload);
            Console.WriteLine("   ERROR: Should have thrown exception!");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"   ✓ Caught expected exception: {ex.Message}");
        }
        Console.WriteLine();

        // Test 4: Invalid payload format
        Console.WriteLine("4. Invalid payload format (Request with wrong size):");
        try
        {
            byte[] invalidPayload = new byte[] { 0x00, 0x00 }; // Request needs 12 bytes
            var message = MessageSerializer.Deserialize((byte)MessageType.Request, invalidPayload);
            Console.WriteLine("   ERROR: Should have thrown exception!");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"   ✓ Caught expected exception: {ex.Message}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Runs all demonstrations.
    /// </summary>
    public static void RunAll()
    {
        DemoBasicFraming();
        DemoMessageSerialization();
        DemoErrorHandling();
        
        Console.WriteLine("=== All Demos Complete ===");
    }
}
