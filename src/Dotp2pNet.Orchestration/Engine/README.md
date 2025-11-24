# Torrent Engine

The Torrent Engine is the main orchestrator that coordinates all aspects of torrent downloads and seeding.

## Overview

The `TorrentEngine` class implements the `ITorrentEngine` interface and serves as the central coordinator for:

- **Peer Discovery**: Finding peers through trackers and DHT
- **Connection Management**: Establishing and maintaining peer connections
- **Piece Selection**: Choosing which pieces to download next
- **Peer Selection**: Deciding which peers to connect to
- **Progress Tracking**: Monitoring download/upload progress and rates
- **State Management**: Handling torrent lifecycle (starting, pausing, resuming, stopping)

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    TorrentEngine                         │
│  (Orchestrates downloads, manages state, tracks status) │
└─────────────────────────────────────────────────────────┘
           │                │                │
           ▼                ▼                ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│  Discovery   │  │  Networking  │  │   Storage    │
│              │  │              │  │              │
│ • Tracker    │  │ • Connection │  │ • Piece      │
│ • DHT        │  │   Manager    │  │   Manager    │
│ • Peer List  │  │ • Peer Conn  │  │ • Bitfield   │
└──────────────┘  └──────────────┘  └──────────────┘
```

## Key Responsibilities

### 1. Download Coordination

When `StartDownloadAsync` is called:

1. **Peer Discovery**
   - Announces to all configured trackers
   - Performs DHT lookup for additional peers
   - Maintains list of available peers

2. **Connection Management**
   - Uses `IPeerSelector` to choose best peers
   - Connects to selected peers via `IConnectionManager`
   - Maintains up to `maxConnections` concurrent connections

3. **Piece Exchange**
   - Uses `IPieceSelector` to choose next piece to download
   - Coordinates piece requests across multiple peers
   - Verifies received pieces via `IPieceManager`

4. **Progress Tracking**
   - Updates `TorrentStatus` with current progress
   - Calculates download/upload rates
   - Estimates time remaining

### 2. Seeding Coordination

When `StartSeedingAsync` is called:

1. **Verification**
   - Verifies file is complete and matches metadata
   - Marks all pieces as available

2. **Announcement**
   - Announces to trackers as a seeder
   - Announces to DHT network

3. **Serving**
   - Accepts incoming connections
   - Responds to piece requests from peers
   - Tracks upload statistics

### 3. State Management

The engine manages torrent state transitions:

```
Stopped → Starting → Downloading → Seeding
                ↓         ↓
              Paused    Error
```

- **Paused**: Stops piece transfers but maintains connections
- **Resumed**: Restarts piece transfers
- **Stopped**: Closes connections and announces to trackers

## Internal Architecture

### TorrentContext

Each active torrent has an associated `TorrentContext` that tracks:

- **Metadata**: Torrent information (name, size, hashes)
- **Status**: Current state and progress
- **Peers**: Available and connected peers
- **Coordination**: Background task managing the torrent
- **Timing**: Last announce, status update timestamps

### Coordination Loop

The main coordination loop runs continuously for each torrent:

```csharp
while (!cancelled && remaining > 0)
{
    if (paused) { wait; continue; }
    
    // Connect to new peers
    await ConnectToPeersAsync();
    
    // Request pieces
    await RequestPiecesAsync();
    
    // Update status
    UpdateDownloadStatus();
    
    await Task.Delay(100);
}
```

## Usage Example

```csharp
// Create engine with dependencies
var engine = new TorrentEngine(
    connectionManager,
    trackerClient,
    dhtNode,
    pieceManager,
    pieceSelector,
    peerSelector,
    logger,
    listenPort: 6881,
    maxConnections: 50);

// Start downloading
var metadata = LoadTorrentMetadata("ubuntu.torrent");
await engine.StartDownloadAsync(metadata, "./downloads");

// Monitor progress
while (true)
{
    var status = engine.GetStatus(metadata.InfoHash);
    Console.WriteLine($"Progress: {status.Progress:F1}%");
    
    if (status.IsComplete)
        break;
        
    await Task.Delay(1000);
}

// Pause if needed
await engine.PauseAsync(metadata.InfoHash);

// Resume
await engine.ResumeAsync(metadata.InfoHash);

// Stop when done
await engine.StopAsync(metadata.InfoHash);
```

## Configuration

The engine accepts configuration parameters:

- **listenPort**: Port to listen for incoming connections (default: 6881)
- **maxConnections**: Maximum concurrent peer connections (default: 50)

Additional configuration comes from injected dependencies:

- **PieceSelector**: Strategy for choosing pieces (rarest-first, random, endgame)
- **PeerSelector**: Strategy for choosing peers (reputation-based, tit-for-tat)

## Error Handling

The engine handles errors gracefully:

- **Network Errors**: Retries with exponential backoff
- **Tracker Failures**: Continues with DHT and other trackers
- **Peer Failures**: Disconnects and tries other peers
- **Storage Errors**: Marks torrent as error state

All errors are logged with appropriate context.

## Thread Safety

The engine is designed for concurrent operation:

- Uses `ConcurrentDictionary` for torrent tracking
- Each torrent has its own coordination task
- Status updates are thread-safe
- Cancellation tokens for graceful shutdown

## Future Enhancements

Planned improvements:

1. **Resume Support**: Save/load download state
2. **Bandwidth Management**: Throttle upload/download rates
3. **Choking Algorithm**: Implement BitTorrent tit-for-tat
4. **Endgame Mode**: Request same piece from multiple peers
5. **Super Seeding**: Optimize seeding for new torrents
6. **Multi-file Torrents**: Support directory structures
7. **Selective Downloads**: Choose specific files to download

## Learning Notes

### Why Orchestration?

The engine demonstrates several distributed systems concepts:

1. **Coordination**: Managing multiple concurrent operations
2. **State Management**: Tracking complex state across components
3. **Fault Tolerance**: Handling failures gracefully
4. **Resource Management**: Limiting connections and bandwidth
5. **Observability**: Providing visibility into system behavior

### Design Patterns Used

- **Facade Pattern**: Simplifies interaction with multiple subsystems
- **Strategy Pattern**: Pluggable piece/peer selection strategies
- **Observer Pattern**: Status updates for monitoring
- **Command Pattern**: Torrent operations (start, pause, resume, stop)

### Key Challenges

1. **Concurrency**: Managing multiple torrents and peers simultaneously
2. **Coordination**: Synchronizing discovery, networking, and storage
3. **Performance**: Balancing throughput with resource usage
4. **Reliability**: Handling network failures and peer disconnections
