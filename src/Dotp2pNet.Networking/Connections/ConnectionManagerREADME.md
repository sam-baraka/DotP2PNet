# ConnectionManager

## Overview

The `ConnectionManager` is responsible for managing multiple peer connections in the P2P network. It handles both outgoing connections (connecting to remote peers) and incoming connections (accepting connections from remote peers), while enforcing connection limits and maintaining a pool of active connections.

## Key Concepts

### Connection Pooling

The ConnectionManager maintains a pool of active connections using a `ConcurrentDictionary<string, IPeerConnection>`. Each connection is assigned a unique GUID identifier when added to the pool. This allows:

- Fast lookup and removal of connections
- Thread-safe concurrent access
- Efficient tracking of connection count

### Connection Limits

To prevent resource exhaustion, the ConnectionManager enforces a maximum connection limit (configurable, default 50). When the limit is reached:

- New outgoing connection attempts throw an `InvalidOperationException`
- New incoming connections are rejected and immediately closed
- Existing connections continue to operate normally

This ensures the system remains stable even under high peer discovery scenarios.

### Thread Safety

The ConnectionManager is fully thread-safe:

- `ConcurrentDictionary` provides lock-free concurrent access to the connection pool
- `SemaphoreSlim` protects listener start/stop operations
- All public methods can be called concurrently from multiple threads

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│              ConnectionManager                           │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │  Connection Pool (ConcurrentDictionary)        │    │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐     │    │
│  │  │ Peer 1   │  │ Peer 2   │  │ Peer 3   │ ... │    │
│  │  └──────────┘  └──────────┘  └──────────┘     │    │
│  └────────────────────────────────────────────────┘    │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │  TcpListener (for incoming connections)        │    │
│  │  - Accept Loop (background task)               │    │
│  │  - Connection limit enforcement                │    │
│  └────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

## Usage Examples

### Connecting to a Remote Peer

```csharp
// Inject ConnectionManager via dependency injection
var connectionManager = serviceProvider.GetRequiredService<IConnectionManager>();

try
{
    // Connect to a peer
    var connection = await connectionManager.ConnectToPeerAsync(
        "192.168.1.50",
        6881,
        cancellationToken);

    // Perform handshake
    await connection.HandshakeAsync(infoHash, peerId, cancellationToken);

    // Connection is now ready for message exchange
    await connection.SendMessageAsync(messageType, payload, cancellationToken);
}
catch (InvalidOperationException ex)
{
    // Maximum connection limit reached
    Console.WriteLine($"Cannot connect: {ex.Message}");
}
catch (TimeoutException ex)
{
    // Connection timed out
    Console.WriteLine($"Connection timeout: {ex.Message}");
}
```

### Accepting Incoming Connections

```csharp
// Start listening for incoming connections
await connectionManager.StartListeningAsync(6881, cancellationToken);

// Connections are automatically accepted and added to the pool
// Access active connections
foreach (var connection in connectionManager.ActiveConnections)
{
    Console.WriteLine($"Connected to peer: {connection.PeerId}");
}

// Stop listening when done
await connectionManager.StopListeningAsync();
```

### Managing Connections

```csharp
// Get all active connections
var connections = connectionManager.ActiveConnections;
Console.WriteLine($"Active connections: {connections.Count}");

// Remove a specific connection
await connectionManager.RemoveConnectionAsync(connection);

// Close all connections (typically during shutdown)
await connectionManager.CloseAllConnectionsAsync();
```

## Connection Lifecycle

### Outgoing Connection Flow

```
1. ConnectToPeerAsync called
   ↓
2. Check connection limit
   ↓
3. Create TcpClient and connect
   ↓
4. Create PeerConnection wrapper
   ↓
5. Add to connection pool
   ↓
6. Return connection to caller
```

### Incoming Connection Flow

```
1. TcpListener accepts connection
   ↓
2. Check connection limit
   ↓
3. Create PeerConnection wrapper
   ↓
4. Add to connection pool
   ↓
5. Continue accepting next connection
```

### Connection Removal Flow

```
1. RemoveConnectionAsync called
   ↓
2. Find connection in pool
   ↓
3. Remove from dictionary
   ↓
4. Close connection
   ↓
5. Dispose connection
```

## Configuration

The ConnectionManager uses the `NetworkConfiguration` settings:

```json
{
  "Dotp2pNet": {
    "Network": {
      "ListenPort": 6881,
      "MaxConnections": 50,
      "ConnectionTimeoutSeconds": 30
    }
  }
}
```

- **ListenPort**: Port to listen on for incoming connections
- **MaxConnections**: Maximum number of concurrent peer connections
- **ConnectionTimeoutSeconds**: Timeout for outgoing connection attempts

## Error Handling

### Connection Limit Reached

When the maximum connection limit is reached:

```csharp
try
{
    var connection = await connectionManager.ConnectToPeerAsync(ip, port, ct);
}
catch (InvalidOperationException ex)
{
    // Handle: Wait for connections to close, or increase MaxConnections
}
```

### Connection Timeout

When a connection attempt times out:

```csharp
try
{
    var connection = await connectionManager.ConnectToPeerAsync(ip, port, ct);
}
catch (TimeoutException ex)
{
    // Handle: Retry with backoff, or mark peer as unreachable
}
```

### Socket Errors

When network errors occur:

```csharp
try
{
    var connection = await connectionManager.ConnectToPeerAsync(ip, port, ct);
}
catch (SocketException ex)
{
    // Handle: Connection refused, network unreachable, etc.
}
```

## Learning Experiments

### Experiment 1: Connection Limit Enforcement

**Goal**: Observe what happens when the connection limit is reached.

```csharp
// Set MaxConnections to 5 in configuration
// Try to connect to 10 peers
for (int i = 0; i < 10; i++)
{
    try
    {
        var conn = await connectionManager.ConnectToPeerAsync($"peer{i}.example.com", 6881, ct);
        Console.WriteLine($"Connected to peer {i}");
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"Connection {i} rejected: {ex.Message}");
    }
}

// Observe: First 5 succeed, remaining 5 are rejected
```

### Experiment 2: Concurrent Connection Attempts

**Goal**: Verify thread safety under concurrent load.

```csharp
// Start 20 concurrent connection attempts
var tasks = Enumerable.Range(0, 20).Select(async i =>
{
    try
    {
        var conn = await connectionManager.ConnectToPeerAsync($"peer{i}.example.com", 6881, ct);
        Console.WriteLine($"Thread {i}: Connected");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Thread {i}: Failed - {ex.Message}");
    }
});

await Task.WhenAll(tasks);

// Observe: No race conditions, connection limit properly enforced
```

### Experiment 3: Incoming Connection Handling

**Goal**: Understand how incoming connections are accepted.

```csharp
// Start listening
await connectionManager.StartListeningAsync(6881, ct);

// Use another instance to connect
var client = new TcpClient();
await client.ConnectAsync("localhost", 6881);

// Check active connections
Console.WriteLine($"Active connections: {connectionManager.ActiveConnections.Count}");

// Observe: Connection automatically added to pool
```

## Common Pitfalls

### 1. Not Checking Connection Limit

**Problem**: Attempting to connect without handling the limit exception.

```csharp
// ❌ Bad: No error handling
var connection = await connectionManager.ConnectToPeerAsync(ip, port, ct);
```

**Solution**: Always handle the `InvalidOperationException`.

```csharp
// ✅ Good: Handle limit exception
try
{
    var connection = await connectionManager.ConnectToPeerAsync(ip, port, ct);
}
catch (InvalidOperationException)
{
    // Wait for connections to close or implement connection prioritization
}
```

### 2. Forgetting to Remove Closed Connections

**Problem**: Connections that fail or close naturally aren't removed from the pool.

**Solution**: Monitor connection health and remove dead connections.

```csharp
// Periodically check and remove dead connections
foreach (var connection in connectionManager.ActiveConnections.ToList())
{
    if (!connection.IsConnected)
    {
        await connectionManager.RemoveConnectionAsync(connection);
    }
}
```

### 3. Not Stopping Listener on Shutdown

**Problem**: Listener continues running after application shutdown.

**Solution**: Always stop the listener during cleanup.

```csharp
// ✅ Good: Proper cleanup
try
{
    await connectionManager.StopListeningAsync();
    await connectionManager.CloseAllConnectionsAsync();
}
finally
{
    connectionManager.Dispose();
}
```

## Integration with Other Components

### With PeerConnection

The ConnectionManager creates and manages `PeerConnection` instances:

```csharp
// ConnectionManager creates PeerConnection
var connection = await connectionManager.ConnectToPeerAsync(ip, port, ct);

// Use PeerConnection for message exchange
await connection.HandshakeAsync(infoHash, peerId, ct);
await connection.SendMessageAsync(messageType, payload, ct);
```

### With TorrentEngine (Future)

The TorrentEngine will use ConnectionManager to manage peer connections:

```csharp
// TorrentEngine discovers peers
var peers = await trackerClient.GetPeersAsync(infoHash);

// Connect to discovered peers
foreach (var peer in peers)
{
    try
    {
        var connection = await connectionManager.ConnectToPeerAsync(
            peer.IpAddress,
            peer.Port,
            ct);
        
        // Add to active peer list
    }
    catch (InvalidOperationException)
    {
        // Connection limit reached, stop connecting
        break;
    }
}
```

## Performance Considerations

### Memory Usage

- Each connection consumes memory for buffers and state
- With 50 connections and 16KB buffers, expect ~800KB for buffers alone
- Monitor memory usage and adjust MaxConnections accordingly

### CPU Usage

- Accept loop runs continuously but blocks on AcceptTcpClientAsync
- Minimal CPU usage when idle
- Connection establishment is async and non-blocking

### Network Bandwidth

- ConnectionManager itself doesn't consume bandwidth
- Bandwidth is consumed by PeerConnection message exchange
- Consider implementing bandwidth throttling at the PeerConnection level

## Testing

### Unit Testing

Mock the dependencies to test connection management logic:

```csharp
[Fact]
public async Task ConnectToPeerAsync_WhenLimitReached_ThrowsException()
{
    // Arrange
    var config = new NetworkConfiguration { MaxConnections = 2 };
    var manager = new ConnectionManager(messageFramer, config, loggerFactory, logger);
    
    // Connect to max connections
    await manager.ConnectToPeerAsync("peer1", 6881, ct);
    await manager.ConnectToPeerAsync("peer2", 6881, ct);
    
    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => manager.ConnectToPeerAsync("peer3", 6881, ct));
}
```

### Integration Testing

Test with real TCP connections:

```csharp
[Fact]
public async Task AcceptConnection_AddsToPool()
{
    // Arrange
    var manager = new ConnectionManager(messageFramer, config, loggerFactory, logger);
    await manager.StartListeningAsync(6881, ct);
    
    // Act
    var client = new TcpClient();
    await client.ConnectAsync("localhost", 6881);
    await Task.Delay(100); // Allow time for accept
    
    // Assert
    Assert.Equal(1, manager.ActiveConnections.Count);
}
```

## Further Reading

- [BitTorrent Protocol Specification](http://www.bittorrent.org/beps/bep_0003.html)
- [TCP Connection Management](https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.tcplistener)
- [Concurrent Collections in .NET](https://docs.microsoft.com/en-us/dotnet/standard/collections/thread-safe/)
