# Structured Logging with Correlation IDs

This document explains how to use structured logging and correlation IDs in Dotp2pNet.

## Overview

Dotp2pNet uses Serilog for structured logging with the following features:

- **Structured Logging**: All log properties are captured as structured data, not just text
- **Correlation IDs**: Track related operations across multiple components
- **Log Enrichment**: Automatic enrichment with machine name, process ID, and thread ID
- **Multiple Sinks**: Console for development, file for production diagnostics
- **Log Levels**: ERROR, WARN, INFO, DEBUG for different verbosity needs

## Log Levels

### ERROR
Critical failures that prevent the application from functioning correctly.

```csharp
_logger.LogError(exception, "Failed to connect to peer {PeerId}", peerId);
```

### WARN
Recoverable issues that don't prevent operation but indicate potential problems.

```csharp
_logger.LogWarning("Peer {PeerId} sent invalid piece {PieceIndex} (hash mismatch)", peerId, pieceIndex);
```

### INFO
Normal operations and significant events.

```csharp
_logger.LogInformation("Connected to peer {PeerId} at {Endpoint}", peerId, endpoint);
```

### DEBUG
Detailed diagnostic information for troubleshooting.

```csharp
_logger.LogDebug("Received message type {MessageType} ({Size} bytes) from peer {PeerId}", 
    messageType, size, peerId);
```

## Correlation IDs

Correlation IDs allow you to track a single operation across multiple components and log entries.

### Why Use Correlation IDs?

In a P2P system, a single user action (like downloading a file) triggers operations across multiple layers:

1. **Tracker Announce**: Get list of peers
2. **DHT Lookup**: Find additional peers
3. **Peer Connections**: Connect to peers
4. **Piece Transfers**: Download pieces
5. **Storage**: Write pieces to disk

By using the same correlation ID for all these operations, you can filter logs to see everything related to that specific download.

### Creating a Correlation Context

```csharp
using Dotp2pNet.Core.Logging;

// Create a new correlation context for a download operation
using (CorrelationContext.Create("Download", 
    ("InfoHash", infoHashHex),
    ("FileName", fileName)))
{
    _logger.LogInformation("Starting download");
    
    // All logs within this scope will have the same CorrelationId
    await AnnounceToTrackerAsync();
    await FindPeersViaDhtAsync();
    await ConnectToPeersAsync();
    await DownloadPiecesAsync();
    
    _logger.LogInformation("Download complete");
}
```

### Passing Correlation IDs Between Components

When calling methods in other components, the correlation ID is automatically propagated through the `LogContext`:

```csharp
// In TorrentEngine
using (CorrelationContext.Create("Download", ("InfoHash", infoHashHex)))
{
    // This log has the CorrelationId
    _logger.LogInformation("Starting download");
    
    // TrackerClient logs will also have the same CorrelationId
    var peers = await _trackerClient.AnnounceAsync(...);
    
    // ConnectionManager logs will also have the same CorrelationId
    foreach (var peer in peers)
    {
        await _connectionManager.ConnectToPeerAsync(peer.IpAddress, peer.Port, ct);
    }
}
```

### Using Existing Correlation IDs

If you need to continue an operation with an existing correlation ID:

```csharp
using (CorrelationContext.CreateWithId(existingCorrelationId, "PieceTransfer",
    ("PieceIndex", pieceIndex)))
{
    _logger.LogInformation("Transferring piece");
    await TransferPieceAsync();
}
```

## Structured Logging Best Practices

### Use Named Properties

Instead of string interpolation, use named properties:

```csharp
// ❌ Bad: String interpolation loses structure
_logger.LogInformation($"Connected to peer {peerId} at {endpoint}");

// ✅ Good: Named properties are structured
_logger.LogInformation("Connected to peer {PeerId} at {Endpoint}", peerId, endpoint);
```

### Include Relevant Context

Include all relevant information that might be useful for debugging:

```csharp
_logger.LogInformation(
    "Piece stored: PieceIndex={PieceIndex}, Size={Size}, Hash={Hash}, Progress={Progress:F2}%",
    pieceIndex,
    size,
    hashHex,
    progress);
```

### Use Consistent Property Names

Use consistent property names across the codebase:

- `PeerId` for peer identifiers
- `InfoHash` for torrent info hashes
- `PieceIndex` for piece indices
- `Endpoint` for network endpoints
- `CorrelationId` for correlation IDs

### Log State Transitions

Log important state transitions:

```csharp
_logger.LogInformation(
    "Connection state changed: PeerId={PeerId}, OldState={OldState}, NewState={NewState}",
    peerId,
    oldState,
    newState);
```

## Example: Complete Download Operation

Here's a complete example showing how to use correlation IDs for a download operation:

```csharp
public async Task StartDownloadAsync(TorrentMetadata metadata, CancellationToken ct)
{
    var infoHashHex = Convert.ToHexString(metadata.InfoHash);
    
    // Create correlation context for the entire download operation
    using (CorrelationContext.Create("Download",
        ("InfoHash", infoHashHex),
        ("FileName", metadata.Name),
        ("TotalSize", metadata.TotalSize)))
    {
        _logger.LogInformation(
            "Starting download: FileName={FileName}, Size={Size} bytes, Pieces={PieceCount}",
            metadata.Name,
            metadata.TotalSize,
            metadata.PieceCount);
        
        try
        {
            // Announce to tracker
            _logger.LogDebug("Announcing to tracker");
            var trackerPeers = await _trackerClient.AnnounceAsync(
                metadata.Trackers[0],
                metadata.InfoHash,
                _peerId,
                _port,
                0, 0, metadata.TotalSize,
                TrackerEvent.Started,
                ct);
            
            _logger.LogInformation("Received {PeerCount} peers from tracker", trackerPeers.Peers.Count);
            
            // Find peers via DHT
            _logger.LogDebug("Finding peers via DHT");
            var dhtPeers = await _dhtNode.FindPeersAsync(metadata.InfoHash, ct);
            _logger.LogInformation("Found {PeerCount} peers via DHT", dhtPeers.Count);
            
            // Connect to peers
            var allPeers = trackerPeers.Peers.Concat(dhtPeers).ToList();
            _logger.LogInformation("Connecting to {PeerCount} peers", allPeers.Count);
            
            foreach (var peer in allPeers.Take(10))
            {
                try
                {
                    var connection = await _connectionManager.ConnectToPeerAsync(
                        peer.IpAddress.ToString(),
                        peer.Port,
                        ct);
                    
                    _logger.LogInformation("Connected to peer {PeerId}", connection.PeerId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to peer {Endpoint}", peer.Endpoint);
                }
            }
            
            // Download pieces
            _logger.LogInformation("Starting piece downloads");
            await DownloadPiecesAsync(metadata, ct);
            
            _logger.LogInformation(
                "Download complete: FileName={FileName}, Size={Size} bytes",
                metadata.Name,
                metadata.TotalSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Download failed: FileName={FileName}, Error={ErrorMessage}",
                metadata.Name,
                ex.Message);
            throw;
        }
    }
}
```

## Querying Logs

### Filter by Correlation ID

To see all logs for a specific operation:

```bash
# In log files
grep "CorrelationId.*abc123-def456" dotp2pnet-20251124.log

# Or use structured log viewers like Seq, Splunk, or ELK
```

### Filter by Operation Type

```bash
grep "Operation.*Download" dotp2pnet-20251124.log
```

### Filter by Info Hash

```bash
grep "InfoHash.*a1b2c3d4" dotp2pnet-20251124.log
```

## Configuration

Configure logging in your application startup:

```csharp
using Dotp2pNet.Core.Logging;
using Serilog.Events;

// Configure logging with INFO level
LoggingConfiguration.ConfigureLogging(
    logDirectory: "/var/log/dotp2pnet",
    minimumLevel: LogEventLevel.Information);

// For development, use DEBUG level
LoggingConfiguration.ConfigureLogging(
    minimumLevel: LogEventLevel.Debug);

// Remember to close logging on shutdown
LoggingConfiguration.CloseLogging();
```

## Log Output Examples

### Console Output (Development)

```
[14:23:45 INF] Starting download {CorrelationId="abc123", Operation="Download", InfoHash="a1b2c3d4", FileName="ubuntu.iso"}
[14:23:46 INF] Received 50 peers from tracker {CorrelationId="abc123", Operation="Download", PeerCount=50}
[14:23:47 INF] Connected to peer 2f3a8b1c {CorrelationId="abc123", Operation="Download", PeerId="2f3a8b1c"}
[14:23:48 INF] Piece stored: PieceIndex=0 {CorrelationId="abc123", Operation="Download", PieceIndex=0, Progress=0.01}
```

### File Output (Production)

```
2025-11-24 14:23:45.123 +00:00 [INF] [Dotp2pNet.Orchestration.Engine.TorrentEngine] Starting download {"CorrelationId":"abc123","Operation":"Download","InfoHash":"a1b2c3d4","FileName":"ubuntu.iso","TotalSize":3758096384,"MachineName":"server01","ProcessId":12345,"ThreadId":1}
2025-11-24 14:23:46.456 +00:00 [INF] [Dotp2pNet.Discovery.Tracker.TrackerClient] Received 50 peers from tracker {"CorrelationId":"abc123","Operation":"Download","PeerCount":50,"TrackerUrl":"http://tracker.ubuntu.com","MachineName":"server01","ProcessId":12345,"ThreadId":2}
```

## Summary

- Use correlation IDs to track operations across components
- Use structured logging with named properties
- Include relevant context in all log messages
- Use appropriate log levels (ERROR, WARN, INFO, DEBUG)
- Create correlation contexts at operation entry points
- Let LogContext automatically propagate correlation IDs
