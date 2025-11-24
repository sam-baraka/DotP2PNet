# Metrics and Observability

## Overview

The metrics system provides comprehensive tracking and monitoring of P2P system performance. It uses sliding window calculations for accurate rate measurements and exposes metrics through the TorrentEngine's `GetStatus()` method and logging.

## Architecture

### MetricsCollector

The `MetricsCollector` class is the central component for tracking system metrics. It is thread-safe and designed for concurrent access from multiple components.

**Key Features:**
- Sliding window rate calculations for smooth, accurate metrics
- Per-torrent metrics tracking
- Global DHT routing table size tracking
- Thread-safe operations using locks and concurrent collections

### TorrentMetrics

The `TorrentMetrics` class tracks metrics for a specific torrent:
- Total bytes downloaded/uploaded
- Pieces completed (with deduplication)
- Hash verification success/failure counts
- Active peer connections and total known peers

## Tracked Metrics

### 1. Download/Upload Rates

**Implementation:** Sliding window calculation

The metrics collector maintains a time-series of data points within a configurable window (default: 10 seconds). When calculating rates:

```
Rate = Total Bytes in Window / Window Duration
```

**Benefits of Sliding Window:**
- Smooth rate calculations without sudden jumps
- Accurate representation of current transfer speed
- Automatic cleanup of old samples

**Example:**
```csharp
var collector = new MetricsCollector(TimeSpan.FromSeconds(10));

// Record downloads
collector.RecordDownload(infoHash, 1024);
collector.RecordDownload(infoHash, 2048);

// Get current rate (bytes per second)
long downloadRate = collector.GetDownloadRate();
```

### 2. Piece Completion Rate

Tracks which pieces have been completed and verified. Uses a `HashSet` to ensure each piece is only counted once.

**Metrics:**
- `PiecesCompleted`: Total number of unique pieces completed
- `CompletedPieceIndices`: Set of completed piece indices

**Example:**
```csharp
collector.RecordPieceCompleted(infoHash, pieceIndex: 0);
collector.RecordPieceCompleted(infoHash, pieceIndex: 1);
collector.RecordPieceCompleted(infoHash, pieceIndex: 0); // Duplicate - ignored

var metrics = collector.GetTorrentMetrics(infoHash);
Console.WriteLine($"Pieces completed: {metrics.PiecesCompleted}"); // Output: 2
```

### 3. Peer Connection Counts

Tracks both active connections and total known peers.

**Metrics:**
- `ActiveConnections`: Number of currently connected peers
- `TotalPeers`: Total number of known peers in the swarm

**Example:**
```csharp
collector.RecordPeerConnections(infoHash, activeConnections: 12, totalPeers: 45);

var metrics = collector.GetTorrentMetrics(infoHash);
Console.WriteLine($"Peers: {metrics.ActiveConnections}/{metrics.TotalPeers}");
// Output: Peers: 12/45
```

### 4. Hash Verification Success/Failure Rates

Tracks the success rate of piece hash verifications to identify problematic peers or network issues.

**Metrics:**
- `HashVerificationSuccesses`: Count of successful verifications
- `HashVerificationFailures`: Count of failed verifications
- `HashVerificationSuccessRate`: Percentage (0-100)

**Example:**
```csharp
collector.RecordHashVerification(infoHash, success: true);
collector.RecordHashVerification(infoHash, success: true);
collector.RecordHashVerification(infoHash, success: false);

var metrics = collector.GetTorrentMetrics(infoHash);
Console.WriteLine($"Hash success rate: {metrics.HashVerificationSuccessRate:F1}%");
// Output: Hash success rate: 66.7%
```

### 5. DHT Routing Table Size

Tracks the number of nodes in the DHT routing table, indicating network connectivity.

**Example:**
```csharp
collector.RecordDhtRoutingTableSize(156);

Console.WriteLine($"DHT nodes: {collector.DhtRoutingTableSize}");
// Output: DHT nodes: 156
```

## Integration with TorrentEngine

The `TorrentEngine` automatically records metrics during operation:

### Download Metrics
- Records bytes downloaded when pieces are received
- Updates download rate using sliding window
- Records peer connection counts
- Records DHT routing table size

### Upload Metrics
- Records bytes uploaded when serving pieces
- Updates upload rate using sliding window
- Records peer connection counts

### Piece Verification
- Records hash verification results (success/failure)
- Records piece completion on successful verification
- Logs verification outcomes

### Accessing Metrics

**Via GetStatus():**
```csharp
var status = torrentEngine.GetStatus(infoHash);
Console.WriteLine($"Download: {status.DownloadRate} bytes/sec");
Console.WriteLine($"Upload: {status.UploadRate} bytes/sec");
Console.WriteLine($"Peers: {status.ConnectedPeers}/{status.AvailablePeers}");
```

**Via MetricsCollector:**
```csharp
var collector = torrentEngine.GetMetricsCollector();
var metrics = collector.GetTorrentMetrics(infoHash);

Console.WriteLine($"Total downloaded: {metrics.TotalDownloaded}");
Console.WriteLine($"Total uploaded: {metrics.TotalUploaded}");
Console.WriteLine($"Pieces completed: {metrics.PiecesCompleted}");
Console.WriteLine($"Hash success rate: {metrics.HashVerificationSuccessRate:F1}%");
Console.WriteLine($"DHT nodes: {collector.DhtRoutingTableSize}");
```

## Logging

The TorrentEngine logs metrics-related events:

**Piece Completion:**
```
[INFO] Piece 42 completed for torrent ubuntu-22.04.iso (hash verified)
```

**Hash Verification Failure:**
```
[WARN] Piece 42 failed hash verification for torrent ubuntu-22.04.iso
```

## Performance Considerations

### Memory Usage

The sliding window maintains a list of data points. With a 10-second window and frequent updates:
- Each data point: ~24 bytes (DateTime + long)
- Typical window: ~100-1000 data points
- Total memory per torrent: ~2-24 KB

Old samples are automatically cleaned up when accessing rates.

### Thread Safety

All operations are thread-safe:
- `MetricsCollector` uses locks for sliding window operations
- `TorrentMetrics` uses locks for all updates
- `ConcurrentDictionary` for per-torrent metrics storage

### Overhead

Metrics collection has minimal overhead:
- Recording operations: O(1) with lock contention
- Rate calculations: O(n) where n = samples in window (typically < 1000)
- Cleanup: O(n) but only performed during rate calculations

## Configuration

### Window Duration

The sliding window duration can be configured:

```csharp
// Default: 10 seconds
var collector = new MetricsCollector();

// Custom: 30 seconds
var collector = new MetricsCollector(TimeSpan.FromSeconds(30));
```

**Trade-offs:**
- Shorter window: More responsive to rate changes, but less smooth
- Longer window: Smoother rates, but slower to reflect changes

**Recommended:** 10-30 seconds for most use cases

## Example: Complete Metrics Workflow

```csharp
// 1. Create metrics collector
var collector = new MetricsCollector(TimeSpan.FromSeconds(10));

// 2. Create torrent engine with metrics
var engine = new TorrentEngine(
    connectionManager,
    trackerClient,
    dhtNode,
    pieceManager,
    pieceSelector,
    peerSelector,
    logger,
    metricsCollector: collector);

// 3. Start download
await engine.StartDownloadAsync(metadata, savePath);

// 4. Monitor metrics
while (true)
{
    var status = engine.GetStatus(metadata.InfoHash);
    var metrics = collector.GetTorrentMetrics(metadata.InfoHash);
    
    Console.WriteLine($"Progress: {status.Progress:F1}%");
    Console.WriteLine($"Download: {FormatRate(status.DownloadRate)}");
    Console.WriteLine($"Upload: {FormatRate(status.UploadRate)}");
    Console.WriteLine($"Pieces: {metrics.PiecesCompleted}");
    Console.WriteLine($"Hash Success: {metrics.HashVerificationSuccessRate:F1}%");
    Console.WriteLine($"Peers: {metrics.ActiveConnections}/{metrics.TotalPeers}");
    Console.WriteLine($"DHT Nodes: {collector.DhtRoutingTableSize}");
    
    await Task.Delay(1000);
}

static string FormatRate(long bytesPerSecond)
{
    if (bytesPerSecond < 1024)
        return $"{bytesPerSecond} B/s";
    if (bytesPerSecond < 1024 * 1024)
        return $"{bytesPerSecond / 1024.0:F2} KB/s";
    return $"{bytesPerSecond / (1024.0 * 1024.0):F2} MB/s";
}
```

## Future Enhancements

Potential improvements for the metrics system:

1. **Histogram Metrics**: Track distribution of piece download times
2. **Percentile Calculations**: P50, P95, P99 latencies
3. **Prometheus Integration**: Export metrics in Prometheus format
4. **Grafana Dashboards**: Pre-built dashboards for visualization
5. **Alerting**: Threshold-based alerts for anomalies
6. **Metrics Persistence**: Save metrics to disk for historical analysis
7. **Per-Peer Metrics**: Track performance of individual peers
8. **Network Quality Metrics**: Packet loss, jitter, latency

## References

- **Sliding Window Algorithm**: Used for smooth rate calculations
- **Concurrent Collections**: For thread-safe metrics storage
- **Requirement 12.3**: Metrics tracking and display requirements
