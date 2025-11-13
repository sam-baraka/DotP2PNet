# Peer Connection

This directory contains the implementation of peer-to-peer TCP connections for the Dotp2pNet system.

## Overview

The `PeerConnection` class manages the complete lifecycle of a connection to a remote peer, including:

- **TCP Socket Management**: Establishes and maintains TCP connections
- **BitTorrent Handshake**: Performs protocol handshake to verify torrent and peer identity
- **Message Exchange**: Sends and receives framed protocol messages
- **Thread Safety**: Uses SemaphoreSlim for send operations and Channels for receive operations
- **Graceful Shutdown**: Properly closes connections and cleans up resources

## Connection Lifecycle

```
Disconnected → Connecting → Connected → Handshaking → Active → Closing → Closed
```

1. **Connecting**: TCP socket connection is being established
2. **Connected**: TCP connection established, ready for handshake
3. **Handshaking**: BitTorrent handshake in progress
4. **Active**: Handshake complete, ready for message exchange
5. **Closing**: Connection is being gracefully closed
6. **Closed**: Connection fully closed, resources released

## Key Concepts

### Thread Safety

The `PeerConnection` class is designed to be thread-safe:

- **Send Operations**: Uses `SemaphoreSlim` to ensure only one thread sends at a time
- **Receive Operations**: Uses `Channel<T>` for thread-safe message queuing
- **State Properties**: Uses volatile fields for visibility across threads

### Async Message Receiving

Messages are received in a background loop that:
1. Continuously reads bytes from the network stream
2. Parses complete messages using the `MessageFramer`
3. Writes messages to an internal channel
4. Allows multiple consumers to read messages concurrently

### BitTorrent Handshake

The handshake is the first message exchanged between peers:

```
[1 byte: protocol length = 19]
[19 bytes: "BitTorrent protocol"]
[8 bytes: reserved/extension bits]
[20 bytes: info_hash]
[20 bytes: peer_id]
```

The handshake verifies:
- Both peers are using the BitTorrent protocol
- Both peers are sharing the same torrent (via info_hash)
- Establishes peer identities (via peer_id)

## Usage Example

### Connecting to a Peer

```csharp
// Create dependencies
var messageFramer = new MessageFramer();
var logger = loggerFactory.CreateLogger<PeerConnection>();

// Connect to remote peer
var connection = await PeerConnection.ConnectAsync(
    "192.168.1.50",
    6881,
    messageFramer,
    logger,
    cancellationToken);

// Perform handshake
byte[] infoHash = /* 20-byte torrent info hash */;
byte[] peerId = /* 20-byte our peer ID */;
await connection.HandshakeAsync(infoHash, peerId, cancellationToken);

// Connection is now ready for message exchange
```

### Accepting an Incoming Connection

```csharp
// Accept TCP connection
var tcpListener = new TcpListener(IPAddress.Any, 6881);
tcpListener.Start();
var tcpClient = await tcpListener.AcceptTcpClientAsync(cancellationToken);

// Create peer connection from accepted client
var connection = new PeerConnection(tcpClient, messageFramer, logger);

// Perform handshake
await connection.HandshakeAsync(infoHash, peerId, cancellationToken);
```

### Sending Messages

```csharp
// Send an Interested message (no payload)
await connection.SendMessageAsync(
    (byte)MessageType.Interested,
    Array.Empty<byte>(),
    cancellationToken);

// Send a Request message
var payload = new byte[12];
// ... encode piece index, offset, length into payload
await connection.SendMessageAsync(
    (byte)MessageType.Request,
    payload,
    cancellationToken);
```

### Receiving Messages

```csharp
// Receive messages in a loop
while (connection.IsConnected)
{
    var (messageType, payload) = await connection.ReceiveMessageAsync(cancellationToken);
    
    switch ((MessageType)messageType)
    {
        case MessageType.Bitfield:
            // Handle bitfield message
            break;
        case MessageType.Piece:
            // Handle piece data
            break;
        case MessageType.Request:
            // Handle piece request
            break;
        // ... handle other message types
    }
}
```

### Graceful Shutdown

```csharp
// Close the connection
await connection.CloseAsync();

// Or dispose (which calls CloseAsync internally)
connection.Dispose();
```

## Error Handling

The `PeerConnection` class handles various error scenarios:

- **Connection Failures**: Throws `SocketException` when connection cannot be established
- **Handshake Failures**: Throws `InvalidOperationException` when handshake fails
- **Protocol Errors**: Closes connection and throws exception for malformed messages
- **Network Errors**: Closes connection and logs error when network issues occur

## Performance Considerations

- **Buffer Size**: Uses 16 KB receive buffer for efficient network I/O
- **Message Framing**: Delegates to `MessageFramer` for efficient parsing
- **Channel Capacity**: Uses unbounded channel for incoming messages (consider bounded for backpressure)
- **Send Lock**: Minimal lock contention with SemaphoreSlim

## Future Enhancements

- Add connection timeout detection (2 minutes of inactivity)
- Implement keep-alive message sending
- Add connection statistics tracking
- Support for protocol extensions via reserved bytes
- Implement connection encryption (MSE/PE)
