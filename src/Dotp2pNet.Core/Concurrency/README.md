# Concurrency and Thread Safety in Dotp2pNet

This document explains the concurrency patterns and thread safety mechanisms used throughout the Dotp2pNet codebase.

## Overview

Dotp2pNet is a highly concurrent system that manages multiple peer connections, piece transfers, and disk I/O operations simultaneously. Proper thread safety is critical to prevent data corruption, race conditions, and deadlocks.

## Key Principles

### 1. ConfigureAwait(false) in Library Code

All library code (everything except the CLI entry point) uses `.ConfigureAwait(false)` on await calls. This prevents capturing the synchronization context, which:
- Improves performance by avoiding unnecessary context switches
- Prevents potential deadlocks in mixed sync/async code
- Is the recommended practice for library code

**Example:**
```csharp
// Good - library code
await stream.WriteAsync(data, ct).ConfigureAwait(false);

// Bad - captures synchronization context unnecessarily
await stream.WriteAsync(data, ct);
```

### 2. Cancellation Token Support

All async methods accept a `CancellationToken` parameter to enable graceful shutdown and operation cancellation. This allows:
- Stopping long-running operations when torrents are paused/stopped
- Graceful application shutdown
- Timeout handling

**Example:**
```csharp
public async Task DownloadPieceAsync(int pieceIndex, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    
    await SomeOperationAsync(ct).ConfigureAwait(false);
}
```

### 3. No Blocking Calls

The codebase avoids blocking calls like `.Result` or `.Wait()` which can cause deadlocks. Instead:
- Use `await` for all async operations
- Use `Task.Run()` for CPU-bound work that needs to run asynchronously
- Use proper async/await patterns throughout

## Thread Safety Mechanisms

### 1. Bitfield (Piece Availability Tracking)

**Location:** `src/Dotp2pNet.Storage/Bitfield/Bitfield.cs`

**Mechanism:** Uses a simple `lock` object for synchronization

**Why:** BitArray is not thread-safe, and operations are fast enough that lock contention is minimal.

```csharp
private readonly object _lock = new();

public bool HasPiece(int pieceIndex)
{
    lock (_lock)
    {
        return _bits[pieceIndex];
    }
}
```

### 2. Piece Manager (File I/O)

**Location:** `src/Dotp2pNet.Storage/Pieces/PieceManager.cs`

**Mechanisms:**
- **Keyed Semaphore:** Ensures only one write per piece at a time (prevents duplicate writes)
- **File Access Lock:** Coordinates all file I/O operations
- **ConcurrentDictionary:** Thread-safe piece cache

**Why:** Multiple peers may send the same piece simultaneously. The keyed semaphore ensures we only write each piece once, preventing wasted disk I/O and potential corruption.

```csharp
// Keyed semaphore - one lock per piece index
private readonly AsyncHelper.KeyedSemaphore<int> _pieceWriteLocks = new(1);

// File access lock - coordinates all file operations
private readonly SemaphoreSlim _fileAccessLock = new(1, 1);

// Thread-safe cache
private readonly ConcurrentDictionary<int, byte[]> _pieceCache = new();

public async Task<bool> StorePieceAsync(int pieceIndex, byte[] data, CancellationToken ct)
{
    // Check if already have piece (fast path, no lock)
    if (_bitfield.HasPiece(pieceIndex))
        return true;

    // Acquire piece-specific lock
    using (await _pieceWriteLocks.LockAsync(pieceIndex, ct).ConfigureAwait(false))
    {
        // Double-check after acquiring lock
        if (_bitfield.HasPiece(pieceIndex))
            return true;

        // Verify and write piece
        await WritePieceAsync(pieceIndex, data, ct).ConfigureAwait(false);
        
        // Mark as complete (bitfield is thread-safe)
        _bitfield.SetPiece(pieceIndex);
    }
}
```

### 3. Peer Connection (Network I/O)

**Location:** `src/Dotp2pNet.Networking/Connections/PeerConnection.cs`

**Mechanisms:**
- **Send Lock:** SemaphoreSlim ensures only one thread sends at a time
- **Channels:** Thread-safe message queue for incoming messages
- **Volatile Fields:** For connection state visibility across threads

**Why:** TCP streams are not thread-safe for concurrent writes. The send lock serializes all outgoing messages while allowing concurrent reads.

```csharp
private readonly SemaphoreSlim _sendLock = new(1, 1);
private readonly Channel<(byte, byte[])> _incomingMessages;
private volatile bool _isConnected;

public async Task SendMessageAsync(byte messageType, byte[] payload, CancellationToken ct)
{
    await _sendLock.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        await _stream.WriteAsync(framedMessage, ct).ConfigureAwait(false);
    }
    finally
    {
        _sendLock.Release();
    }
}
```

### 4. Connection Manager

**Location:** `src/Dotp2pNet.Networking/Connections/ConnectionManager.cs`

**Mechanism:** ConcurrentDictionary for active connections

**Why:** Multiple threads may add/remove connections simultaneously. ConcurrentDictionary provides thread-safe operations without explicit locking.

```csharp
private readonly ConcurrentDictionary<string, IPeerConnection> _connections = new();

public async Task<IPeerConnection> ConnectToPeerAsync(string host, int port, CancellationToken ct)
{
    var connection = await PeerConnection.ConnectAsync(host, port, _messageFramer, _logger, ct)
        .ConfigureAwait(false);
    
    _connections.TryAdd(connectionKey, connection);
    return connection;
}
```

### 5. Routing Table (DHT)

**Location:** `src/Dotp2pNet.Discovery/Dht/RoutingTable.cs`

**Mechanism:** Simple `lock` object for all operations

**Why:** DHT operations are relatively infrequent, and the routing table structure is complex. A simple lock provides correctness without complexity.

```csharp
private readonly object _lock = new();

public bool AddNode(DhtNodeInfo node)
{
    lock (_lock)
    {
        int bucketIndex = XorDistance.GetBucketIndex(_ourNodeId, node.NodeId);
        return _buckets[bucketIndex].TryAddNode(node);
    }
}
```

### 6. Torrent Engine

**Location:** `src/Dotp2pNet.Orchestration/Engine/TorrentEngine.cs`

**Mechanisms:**
- **ConcurrentDictionary:** For active torrents and peer connections
- **CancellationTokenSource:** Per-torrent cancellation
- **Task Coordination:** Each torrent runs in its own async task

**Why:** The engine coordinates multiple torrents, each with multiple peer connections. ConcurrentDictionary allows thread-safe access to torrent state.

```csharp
private readonly ConcurrentDictionary<string, TorrentContext> _activeTorrents = new();

private class TorrentContext
{
    public ConcurrentDictionary<string, PeerInfo> AvailablePeers { get; } = new();
    public ConcurrentDictionary<string, IPeerConnection> PeerConnections { get; } = new();
    public CancellationTokenSource CancellationTokenSource { get; init; }
}
```

### 7. State Manager (Persistence)

**Location:** `src/Dotp2pNet.Storage/State/StateManager.cs`

**Mechanism:** SemaphoreSlim for write operations

**Why:** Multiple torrents may try to save state simultaneously. The semaphore ensures atomic file writes.

```csharp
private readonly SemaphoreSlim _writeLock = new(1, 1);

public async Task SaveStateAsync(...)
{
    await _writeLock.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        // Atomic write: temp file + rename
        await File.WriteAllTextAsync(tempFilePath, json, ct).ConfigureAwait(false);
        File.Move(tempFilePath, stateFilePath, overwrite: true);
    }
    finally
    {
        _writeLock.Release();
    }
}
```

## Common Patterns

### Pattern 1: Double-Checked Locking

Used when an expensive operation should only happen once:

```csharp
// Fast path - no lock
if (_bitfield.HasPiece(pieceIndex))
    return true;

// Acquire lock
using (await _lock.LockAsync(pieceIndex, ct).ConfigureAwait(false))
{
    // Double-check after acquiring lock
    if (_bitfield.HasPiece(pieceIndex))
        return true;

    // Perform expensive operation
    await WritePieceAsync(pieceIndex, data, ct).ConfigureAwait(false);
}
```

### Pattern 2: Keyed Locking

Used when you need fine-grained locking per resource:

```csharp
// One lock per piece index
private readonly AsyncHelper.KeyedSemaphore<int> _pieceLocks = new(1);

// Only blocks operations on the same piece
using (await _pieceLocks.LockAsync(pieceIndex, ct).ConfigureAwait(false))
{
    // Critical section for this specific piece
}
```

### Pattern 3: Atomic File Operations

Used for safe file persistence:

```csharp
// Write to temporary file
await File.WriteAllTextAsync(tempPath, data, ct).ConfigureAwait(false);

// Atomic rename (overwrites existing)
File.Move(tempPath, finalPath, overwrite: true);
```

## Performance Considerations

### 1. Lock Granularity

- **Coarse-grained locks** (e.g., routing table): Simple, but may cause contention
- **Fine-grained locks** (e.g., piece writes): More complex, but better concurrency

### 2. Lock-Free Data Structures

- Use `ConcurrentDictionary` instead of `Dictionary` + lock when possible
- Use `Interlocked` operations for simple atomic updates
- Use `volatile` for visibility of simple state flags

### 3. Async Locks

- Use `SemaphoreSlim` instead of `lock` for async code
- Never use `lock` in async methods (can cause deadlocks)
- Always use `ConfigureAwait(false)` when awaiting in library code

## Testing Thread Safety

### Unit Tests

Test concurrent operations:

```csharp
[Fact]
public async Task StorePiece_ConcurrentWrites_OnlyWritesOnce()
{
    var pieceManager = CreatePieceManager();
    var pieceData = GenerateRandomData(256 * 1024);

    // Simulate 10 peers sending the same piece simultaneously
    var tasks = Enumerable.Range(0, 10)
        .Select(_ => pieceManager.StorePieceAsync(0, pieceData, CancellationToken.None))
        .ToArray();

    await Task.WhenAll(tasks);

    // Verify piece was only written once (check file system, logs, etc.)
    Assert.True(pieceManager.HasPiece(0));
}
```

### Integration Tests

Test real-world scenarios with multiple peers and torrents.

## Common Pitfalls to Avoid

### 1. Deadlocks

```csharp
// BAD - can deadlock
lock (_lock1)
{
    lock (_lock2)
    {
        // ...
    }
}

// GOOD - acquire locks in consistent order
// Or better: redesign to avoid nested locks
```

### 2. Blocking in Async Code

```csharp
// BAD - blocks thread
var result = SomeAsyncMethod().Result;

// GOOD - await properly
var result = await SomeAsyncMethod().ConfigureAwait(false);
```

### 3. Forgetting ConfigureAwait

```csharp
// BAD - captures context in library code
await stream.WriteAsync(data, ct);

// GOOD - doesn't capture context
await stream.WriteAsync(data, ct).ConfigureAwait(false);
```

### 4. Race Conditions

```csharp
// BAD - race condition
if (!_bitfield.HasPiece(index))
{
    // Another thread might write here!
    await WritePieceAsync(index, data, ct);
}

// GOOD - atomic check-and-set
using (await _lock.LockAsync(index, ct).ConfigureAwait(false))
{
    if (!_bitfield.HasPiece(index))
    {
        await WritePieceAsync(index, data, ct).ConfigureAwait(false);
    }
}
```

## Summary

Dotp2pNet uses a layered approach to thread safety:

1. **Bitfield:** Simple locks for fast, infrequent operations
2. **Piece Manager:** Keyed semaphores for fine-grained piece locking
3. **Connections:** Send locks + channels for network I/O
4. **Collections:** ConcurrentDictionary for shared state
5. **Persistence:** Semaphores + atomic file operations

All async code uses `ConfigureAwait(false)` and proper cancellation token support for optimal performance and reliability.
