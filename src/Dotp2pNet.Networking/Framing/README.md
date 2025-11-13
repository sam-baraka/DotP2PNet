# Message Framing Layer

## Overview

The message framing layer implements the core protocol for exchanging structured messages over TCP streams. Since TCP is stream-oriented (not message-oriented), we need a way to identify message boundaries.

## Wire Format

Each message on the wire follows this format:

```
[4 bytes: length][1 byte: type][N bytes: payload]
```

- **Length (4 bytes, big-endian)**: Total size of type + payload (minimum 1 byte)
- **Type (1 byte)**: MessageType enum value (0-9)
- **Payload (N bytes)**: Message-specific data

### Example: Request Message

A request for piece 42, offset 0, length 16384:

```
Wire format (hex):
00 00 00 0D    // Length: 13 bytes (1 byte type + 12 bytes payload)
02             // Type: Request (2)
00 00 00 2A    // Piece index: 42
00 00 00 00    // Offset: 0
00 00 40 00    // Length: 16384
```

## Components

### MessageFramer

Low-level framing and parsing:
- `FrameMessage()`: Adds length prefix and type to payload
- `ParseMessages()`: Extracts complete messages from byte stream
- Handles partial reads and buffer management
- Validates message length and type

### MessageSerializer

Converts between PeerMessage objects and byte arrays:
- `Serialize()`: Converts PeerMessage to payload bytes
- `Deserialize()`: Converts payload bytes to PeerMessage
- Handles all message types (Handshake, Bitfield, Request, Piece, etc.)

### MessageFramingService

High-level API combining framing and serialization:
- `FrameMessageAsync()`: Converts PeerMessage to framed bytes ready to send
- `ReadMessageAsync()`: Reads and parses a single message from a stream
- `ReadMessagesAsync()`: Async enumerable for continuous message reading

## Usage

### Sending a Message

```csharp
var service = new MessageFramingService();

// Create a message
var request = new RequestMessage
{
    PieceIndex = 42,
    Offset = 0,
    Length = 16384
};

// Frame it for transmission
byte[] framedMessage = await service.FrameMessageAsync(request);

// Send over network
await stream.WriteAsync(framedMessage);
```

### Receiving Messages

```csharp
var service = new MessageFramingService();

// Read a single message
PeerMessage message = await service.ReadMessageAsync(stream, cancellationToken);

// Or read multiple messages as they arrive
await foreach (var msg in service.ReadMessagesAsync(stream, cancellationToken))
{
    switch (msg.Type)
    {
        case MessageType.Request:
            var request = (RequestMessage)msg;
            // Handle request
            break;
        case MessageType.Piece:
            var piece = (PieceMessage)msg;
            // Handle piece
            break;
    }
}
```

## Message Formats

### Handshake (Type 0)
```
[20 bytes: InfoHash][20 bytes: PeerId]
```

### Bitfield (Type 1)
```
[N bytes: bitfield data]
```
Each bit represents whether the peer has that piece (1) or not (0).

### Request (Type 2)
```
[4 bytes: piece index][4 bytes: offset][4 bytes: length]
```

### Piece (Type 3)
```
[4 bytes: piece index][4 bytes: offset][N bytes: data]
```

### Have (Type 4)
```
[4 bytes: piece index]
```

### Interested, NotInterested, Choke, Unchoke, KeepAlive (Types 5-9)
```
[no payload]
```

## Key Concepts

### Why Length Prefix?

TCP delivers data as a continuous stream of bytes. Without framing, you can't tell where one message ends and another begins. Consider:

```
Without framing:
[message1 data][message2 data][message3 data]
↑ Where does message1 end?

With length prefix:
[4 bytes: len1][message1 data][4 bytes: len2][message2 data]
↑ Clear boundaries!
```

### Handling Partial Reads

TCP may deliver data in chunks smaller than a complete message. The MessageFramer maintains an internal buffer to accumulate data until a complete message arrives:

```
Read 1: [00 00 00 0D 02 00]           // Partial message
Read 2: [00 00 2A 00 00 00 00 00]     // More data
Read 3: [00 40 00]                    // Complete! Extract message
```

### Buffer Management

The MessageFramer automatically compacts its internal buffer when half the buffer has been consumed, preventing memory waste:

```
Before compaction:
[consumed data][unconsumed data][free space]

After compaction:
[unconsumed data][free space]
```

## Error Handling

### Invalid Length
- Length < 1: Rejected (must include at least type byte)
- Length > 16 MB: Rejected (prevents memory exhaustion)

### Invalid Type
- Type not in MessageType enum: Rejected

### Invalid Payload
- Payload doesn't match expected format: Rejected
- Message fails validation: Rejected

## Performance Considerations

### Buffer Pooling
Consider using `ArrayPool<byte>` for temporary buffers in high-throughput scenarios.

### Pipelining
The protocol supports request pipelining - multiple requests can be in-flight simultaneously (typically 5 per peer).

### Zero-Copy
Where possible, the implementation avoids unnecessary copying of data.

## Testing

To test the message framing layer:

1. **Unit tests**: Test individual message serialization/deserialization
2. **Integration tests**: Test complete round-trip (serialize → frame → parse → deserialize)
3. **Partial read tests**: Simulate TCP delivering data in small chunks
4. **Error tests**: Test handling of malformed messages

## Learning Experiments

### Experiment 1: Observe Framing
Use Wireshark to capture network traffic and observe the length prefix and message type bytes on the wire.

### Experiment 2: Partial Reads
Modify the read buffer size to be very small (e.g., 8 bytes) and observe how the framer handles partial messages.

### Experiment 3: Invalid Messages
Send malformed messages (wrong length, invalid type) and observe error handling.

## References

- BitTorrent Protocol Specification (BEP 3)
- TCP Stream Protocol (RFC 793)
- Message Framing Patterns
