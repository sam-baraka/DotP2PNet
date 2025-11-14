# Piece Exchange Protocol Implementation

## Overview

The piece exchange protocol is the heart of the BitTorrent P2P file sharing system. This implementation provides a complete, production-ready piece exchange mechanism with proper flow control, state management, and error handling.

## Architecture

The piece exchange system is composed of four main components:

```
┌─────────────────────────────────────────────────────────────┐
│         PieceExchangeCoordinator                            │
│  (Orchestrates handshake, bitfield exchange, and messages)  │
└─────────────────────────────────────────────────────────────┘
           │                │                │
           ▼                ▼                ▼
    ┌──────────┐    ┌──────────────┐  ┌──────────────┐
    │PeerState │    │PieceRequester│  │PieceResponder│
    │          │    │              │  │              │
    │• Choking │    │• Flow Control│  │• Validation  │
    │• Interest│    │• Requests    │  │• Responses   │
    │• Bitfield│    │• Verification│  │• Upload Stats│
    └──────────┘    └──────────────┘  └──────────────┘
```

## Components

### 1. PeerState

**Purpose**: Tracks the state of a peer connection throughout the piece exchange lifecycle.

**Key Responsibilities**:
- Maintains choking state (am I choking them? are they choking me?)
- Tracks interest state (am I interested? are they interested?)
- Stores peer's bitfield (which pieces they have)
- Provides thread-safe state transitions

**State Machine**:
```
Initial State: Choked + Not Interested

Transitions:
- Receive Bitfield → Update peer bitfield → Evaluate interest
- Peer has needed pieces → Send Interested
- Peer has no needed pieces → Send Not Interested
- Receive Unchoke → Can send requests
- Receive Choke → Cancel all requests
```

**Thread Safety**: All state access is protected by locks to ensure consistency across concurrent operations.

### 2. PieceRequester

**Purpose**: Manages outgoing piece requests with flow control and timeout handling.

**Key Responsibilities**:
- Implements flow control (max 5 concurrent requests per peer)
- Tracks outstanding requests with timestamps
- Handles received pieces and verifies hashes
- Detects and handles request timeouts (30 seconds)
- Manages request cancellation when choked

**Flow Control Algorithm**:
```
1. Check if peer is unchoked
2. Wait for available request slot (semaphore)
3. Send request message
4. Track request with timestamp
5. On piece receipt:
   - Verify hash
   - Store piece
   - Release semaphore slot
6. On timeout:
   - Remove from outstanding requests
   - Release semaphore slot
   - Re-request from different peer (handled by orchestration layer)
```

**Request Pipelining**: By allowing up to 5 concurrent requests, we keep the network pipe full and maximize throughput.

### 3. PieceResponder

**Purpose**: Handles incoming piece requests from remote peers.

**Key Responsibilities**:
- Validates incoming requests (bounds checking, piece availability)
- Respects choking state (only responds when unchoked)
- Reads pieces from storage
- Sends piece data to requesting peer
- Tracks upload statistics

**Request Validation**:
- Piece index must be in valid range [0, totalPieces)
- Offset must be non-negative
- Length must be between 1 byte and 128 KB
- Offset + length must not exceed piece size
- We must have the requested piece

**Idempotency**: Duplicate requests are handled gracefully by simply re-sending the piece data.

### 4. PieceExchangeCoordinator

**Purpose**: Orchestrates the complete piece exchange protocol lifecycle.

**Key Responsibilities**:
- Performs handshake with remote peer
- Exchanges bitfield messages
- Processes all incoming messages
- Updates interest state based on peer's pieces
- Coordinates between PeerState, PieceRequester, and PieceResponder
- Handles message deserialization

**Protocol Flow**:
```
1. Handshake
   - Send: Protocol version, info hash, peer ID
   - Receive: Peer's handshake
   - Verify: Info hash matches

2. Bitfield Exchange
   - Send: Our bitfield (which pieces we have)
   - Receive: Peer's bitfield
   - Evaluate: Are we interested in their pieces?

3. Interest Management
   - If peer has pieces we need → Send Interested
   - If peer has no pieces we need → Send Not Interested
   - Update interest when peer announces new pieces (Have messages)

4. Piece Exchange
   - When unchoked: Request pieces via PieceRequester
   - When peer requests: Respond via PieceResponder
   - Verify received pieces before acceptance

5. State Transitions
   - Handle Choke/Unchoke messages
   - Handle Interested/Not Interested messages
   - Update peer bitfield on Have messages
```

## Message Types

The protocol uses the following message types:

| Message Type | Direction | Purpose |
|-------------|-----------|---------|
| Handshake | Bidirectional | Establish connection, verify torrent |
| Bitfield | Bidirectional | Communicate piece availability |
| Interested | Bidirectional | Express desire to download |
| Not Interested | Bidirectional | Express no need to download |
| Choke | Bidirectional | Refuse to fulfill requests |
| Unchoke | Bidirectional | Allow requests to be fulfilled |
| Request | Outgoing | Ask for a piece |
| Piece | Incoming | Receive piece data |
| Have | Bidirectional | Announce new piece acquisition |
| Keep-Alive | Bidirectional | Prevent connection timeout |

## Flow Control

Flow control prevents overwhelming peers with too many simultaneous requests:

**Mechanism**: Semaphore with max count of 5
- Before sending request: Acquire semaphore slot
- After receiving piece: Release semaphore slot
- On timeout: Release semaphore slot

**Benefits**:
- Prevents network congestion
- Allows fair bandwidth distribution
- Enables request pipelining for efficiency
- Respects peer's processing capacity

## Hash Verification

Every received piece is verified before acceptance:

```csharp
1. Receive piece data
2. Compute SHA-256 hash of data
3. Compare with expected hash from torrent metadata
4. If match: Store piece, broadcast Have message
5. If mismatch: Discard piece, re-request from different peer
```

This ensures data integrity and protects against:
- Corrupted data during transmission
- Malicious peers sending incorrect data
- Storage errors

## Error Handling

The implementation handles various error scenarios:

### Connection Errors
- **Peer disconnects**: Stop message processing, clean up resources
- **Send failures**: Log error, potentially disconnect peer
- **Receive failures**: Log error, attempt reconnection

### Protocol Errors
- **Malformed messages**: Log warning, ignore message
- **Invalid requests**: Log warning, don't respond
- **Unexpected messages**: Log warning, ignore

### Data Errors
- **Hash mismatch**: Discard piece, increment peer failure count, re-request
- **Storage errors**: Log error, pause torrent
- **Timeout**: Cancel request, re-request from different peer

### Resource Errors
- **Too many requests**: Flow control prevents this
- **Memory pressure**: Use streaming for large pieces
- **Disk full**: Pause torrent, notify user

## Concurrency Model

The implementation uses modern async/await patterns:

**Message Processing**:
- Runs in dedicated background task
- Processes messages sequentially per peer
- Uses cancellation tokens for graceful shutdown

**Thread Safety**:
- PeerState: Protected by locks
- PieceRequester: Uses SemaphoreSlim and ConcurrentDictionary
- PieceResponder: Thread-safe statistics tracking

**Async Operations**:
- All I/O operations are async
- No blocking calls (.Result, .Wait())
- Proper cancellation token propagation

## Usage Example

```csharp
// Create coordinator
var coordinator = new PieceExchangeCoordinator(
    connection,
    pieceManager,
    loggerFactory);

// Perform handshake and bitfield exchange
await coordinator.PerformHandshakeAsync(infoHash, ourPeerId, ct);

// Start processing messages
coordinator.StartMessageProcessing();

// Request pieces (when unchoked)
if (!coordinator.State.PeerChoking && coordinator.State.AmInterested)
{
    var pieceIndex = pieceManager.SelectPiece(coordinator.State.PeerBitfield);
    if (pieceIndex >= 0)
    {
        await coordinator.Requester.RequestPieceAsync(pieceIndex, ct);
    }
}

// Cleanup
coordinator.Dispose();
```

## Performance Considerations

### Memory Management
- Piece data is streamed, not loaded entirely into memory
- Request tracking uses minimal memory (int + DateTime per request)
- Bitfield uses compact bit array representation

### Network Optimization
- Request pipelining keeps network pipe full
- Flow control prevents congestion
- Timeout detection prevents hanging requests

### CPU Optimization
- Hash verification is the main CPU cost
- Serialization/deserialization is lightweight
- State transitions are lock-protected but fast

## Testing Strategy

### Unit Tests
- PeerState state transitions
- PieceRequester flow control
- PieceResponder request validation
- Message serialization/deserialization

### Integration Tests
- Two-peer piece exchange
- Hash verification
- Timeout handling
- Choke/unchoke behavior

### Stress Tests
- Many concurrent requests
- Large pieces
- Slow peers
- Network errors

## Learning Concepts

This implementation demonstrates several key distributed systems concepts:

1. **Flow Control**: Limiting concurrent operations to prevent overload
2. **State Machines**: Managing complex peer state transitions
3. **Idempotency**: Handling duplicate messages gracefully
4. **Hash Verification**: Ensuring data integrity in untrusted networks
5. **Async Programming**: Non-blocking I/O for scalability
6. **Error Recovery**: Graceful handling of various failure modes
7. **Resource Management**: Proper cleanup and disposal patterns

## Future Enhancements

Potential improvements for production use:

1. **Endgame Mode**: Request same piece from multiple peers when almost done
2. **Fast Extension**: Support for fast peer protocol extension
3. **Encryption**: Add protocol encryption (MSE/PE)
4. **Bandwidth Throttling**: Implement upload/download rate limiting
5. **Peer Reputation**: Track peer performance and prioritize good peers
6. **Piece Caching**: Cache frequently requested pieces in memory
7. **Metrics**: Detailed performance metrics and monitoring

## References

- [BitTorrent Protocol Specification](http://www.bittorrent.org/beps/bep_0003.html)
- [BitTorrent Enhancement Proposals](http://www.bittorrent.org/beps/bep_0000.html)
- [Piece Selection Algorithms](http://www.bittorrent.org/beps/bep_0003.html#piece-selection)
