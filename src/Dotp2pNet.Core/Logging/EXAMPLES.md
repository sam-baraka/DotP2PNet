# Structured Logging Examples

This document provides practical examples of using structured logging with correlation IDs in Dotp2pNet.

## Example 1: Basic Logging

```csharp
using Microsoft.Extensions.Logging;

public class MyComponent
{
    private readonly ILogger<MyComponent> _logger;

    public MyComponent(ILogger<MyComponent> logger)
    {
        _logger = logger;
    }

    public void DoSomething()
    {
        // ❌ Bad: String interpolation
        _logger.LogInformation($"Processing item {itemId}");

        // ✅ Good: Structured logging with named properties
        _logger.LogInformation("Processing item {ItemId}", itemId);
    }
}
```

## Example 2: Connection Events

```csharp
public async Task ConnectToPeerAsync(string ipAddress, int port, CancellationToken ct)
{
    _logger.LogInformation(
        "Connecting to peer at {IpAddress}:{Port}",
        ipAddress,
        port);

    try
    {
        var connection = await PeerConnection.ConnectAsync(ipAddress, port, _messageFramer, _logger, ct);

        _logger.LogInformation(
            "Successfully connected to peer {PeerId} at {Endpoint}",
            connection.PeerId,
            $"{ipAddress}:{port}");

        return connection;
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Failed to connect to peer at {IpAddress}:{Port}, Error={ErrorMessage}",
            ipAddress,
            port,
            ex.Message);
        throw;
    }
}
```

## Example 3: Piece Transfer Logging

```csharp
public async Task<bool> StorePieceAsync(int pieceIndex, byte[] data, CancellationToken ct)
{
    _logger.LogDebug(
        "Storing piece {PieceIndex}, Size={Size} bytes",
        pieceIndex,
        data.Length);

    // Verify hash
    if (!await VerifyPieceAsync(pieceIndex, data, ct))
    {
        _logger.LogWarning(
            "Piece {PieceIndex} failed hash verification. Expected={ExpectedHash}, Actual={ActualHash}",
            pieceIndex,
            Convert.ToHexString(_metadata.PieceHashes[pieceIndex]),
            Convert.ToHexString(SHA256.HashData(data)));
        return false;
    }

    // Write to disk
    await WritePieceAsync(pieceIndex, data, ct);

    var progress = (CompletedPieces * 100.0) / TotalPieces;

    _logger.LogInformation(
        "Successfully stored piece {PieceIndex}/{TotalPieces} ({Progress:F2}%), Hash={Hash}",
        pieceIndex,
        TotalPieces,
        progress,
        Convert.ToHexString(_metadata.PieceHashes[pieceIndex][..4]));

    return true;
}
```

## Example 4: Tracker Operations

```csharp
public async Task<TrackerResponse> AnnounceAsync(
    string trackerUrl,
    byte[] infoHash,
    byte[] peerId,
    int port,
    long downloaded,
    long uploaded,
    long left,
    TrackerEvent eventType,
    CancellationToken ct)
{
    _logger.LogInformation(
        "Announcing to tracker {TrackerUrl} with event {Event}, " +
        "Downloaded={Downloaded}, Uploaded={Uploaded}, Left={Left}",
        trackerUrl,
        eventType,
        downloaded,
        uploaded,
        left);

    try
    {
        var response = await SendAnnounceAsync(trackerUrl, infoHash, peerId, port, 
            downloaded, uploaded, left, eventType, ct);

        _logger.LogInformation(
            "Received {PeerCount} peers from tracker {TrackerUrl}. " +
            "Seeders={Seeders}, Leechers={Leechers}, Interval={Interval}s",
            response.Peers.Count,
            trackerUrl,
            response.Complete,
            response.Incomplete,
            response.Interval);

        return response;
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Failed to announce to tracker {TrackerUrl}, Error={ErrorMessage}",
            trackerUrl,
            ex.Message);
        throw;
    }
}
```

## Example 5: DHT Operations

```csharp
public async Task<List<PeerInfo>> FindPeersAsync(byte[] infoHash, CancellationToken ct)
{
    var infoHashHex = Convert.ToHexString(infoHash[..4]);

    _logger.LogDebug(
        "Finding peers for info hash {InfoHash}",
        infoHashHex);

    var peers = new List<PeerInfo>();
    var queriedNodes = new HashSet<string>();

    // Perform iterative lookup
    for (int iteration = 0; iteration < 10; iteration++)
    {
        _logger.LogDebug(
            "DHT lookup iteration {Iteration}, QueriedNodes={QueriedNodes}, FoundPeers={FoundPeers}",
            iteration,
            queriedNodes.Count,
            peers.Count);

        // ... lookup logic ...
    }

    _logger.LogInformation(
        "Found {PeerCount} peers for info hash {InfoHash} after {Iterations} iterations",
        peers.Count,
        infoHashHex,
        iteration);

    return peers;
}
```

## Example 6: Using Correlation IDs for Download Operation

```csharp
using Dotp2pNet.Core.Logging;

public async Task StartDownloadAsync(TorrentMetadata metadata, CancellationToken ct)
{
    var infoHashHex = Convert.ToHexString(metadata.InfoHash[..8]);

    // Create correlation context for the entire download
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
            // Step 1: Announce to tracker
            _logger.LogDebug("Step 1: Announcing to tracker");
            var trackerPeers = await AnnounceToTrackerAsync(metadata, ct);
            _logger.LogInformation("Received {PeerCount} peers from tracker", trackerPeers.Count);

            // Step 2: Find peers via DHT
            _logger.LogDebug("Step 2: Finding peers via DHT");
            var dhtPeers = await FindPeersViaDhtAsync(metadata.InfoHash, ct);
            _logger.LogInformation("Found {PeerCount} peers via DHT", dhtPeers.Count);

            // Step 3: Connect to peers
            _logger.LogDebug("Step 3: Connecting to peers");
            var connections = await ConnectToPeersAsync(trackerPeers.Concat(dhtPeers).ToList(), ct);
            _logger.LogInformation("Connected to {ConnectionCount} peers", connections.Count);

            // Step 4: Download pieces
            _logger.LogDebug("Step 4: Downloading pieces");
            await DownloadPiecesAsync(metadata, connections, ct);

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

## Example 7: Nested Correlation Contexts

```csharp
public async Task DownloadPiecesAsync(TorrentMetadata metadata, List<IPeerConnection> connections, CancellationToken ct)
{
    // This inherits the parent correlation ID from StartDownloadAsync
    _logger.LogInformation("Starting piece downloads");

    foreach (var pieceIndex in GetMissingPieces())
    {
        // Create a nested context for each piece download
        using (LogContext.PushProperty("PieceIndex", pieceIndex))
        {
            _logger.LogDebug("Requesting piece {PieceIndex}", pieceIndex);

            try
            {
                var peer = SelectPeerForPiece(pieceIndex, connections);
                var pieceData = await RequestPieceAsync(peer, pieceIndex, ct);

                _logger.LogInformation(
                    "Received piece {PieceIndex} from peer {PeerId}, Size={Size} bytes",
                    pieceIndex,
                    peer.PeerId,
                    pieceData.Length);

                await StorePieceAsync(pieceIndex, pieceData, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to download piece {PieceIndex}, will retry",
                    pieceIndex);
            }
        }
    }

    _logger.LogInformation("Piece downloads complete");
}
```

## Example 8: State Transition Logging

```csharp
public void UpdateConnectionState(ConnectionState newState)
{
    var oldState = _currentState;
    _currentState = newState;

    _logger.LogInformation(
        "Connection state changed: PeerId={PeerId}, OldState={OldState}, NewState={NewState}",
        _peerId,
        oldState,
        newState);

    // Log additional details at debug level
    _logger.LogDebug(
        "Connection state details: PeerId={PeerId}, State={State}, " +
        "AmChoking={AmChoking}, PeerChoking={PeerChoking}, " +
        "AmInterested={AmInterested}, PeerInterested={PeerInterested}",
        _peerId,
        newState,
        _amChoking,
        _peerChoking,
        _amInterested,
        _peerInterested);
}
```

## Example 9: Error Handling with Context

```csharp
public async Task<Result<byte[]>> ReadPieceAsync(int pieceIndex, CancellationToken ct)
{
    try
    {
        _logger.LogDebug("Reading piece {PieceIndex} from disk", pieceIndex);

        var data = await ReadPieceFromDiskAsync(pieceIndex, ct);

        _logger.LogDebug(
            "Successfully read piece {PieceIndex}, Size={Size} bytes",
            pieceIndex,
            data.Length);

        return Result<byte[]>.Success(data);
    }
    catch (FileNotFoundException ex)
    {
        _logger.LogError(
            ex,
            "Piece file not found: PieceIndex={PieceIndex}, FilePath={FilePath}",
            pieceIndex,
            GetPieceFilePath(pieceIndex));

        return Result<byte[]>.Failure(new Error(
            ErrorCategory.Storage,
            $"Piece {pieceIndex} file not found",
            ex,
            isRetryable: false));
    }
    catch (IOException ex)
    {
        _logger.LogError(
            ex,
            "I/O error reading piece: PieceIndex={PieceIndex}, Error={ErrorMessage}",
            pieceIndex,
            ex.Message);

        return Result<byte[]>.Failure(new Error(
            ErrorCategory.Storage,
            $"Failed to read piece {pieceIndex}",
            ex,
            isRetryable: true));
    }
}
```

## Example 10: Performance Metrics Logging

```csharp
public async Task TransferPieceAsync(IPeerConnection peer, int pieceIndex, CancellationToken ct)
{
    var stopwatch = Stopwatch.StartNew();

    _logger.LogDebug(
        "Starting piece transfer: PeerId={PeerId}, PieceIndex={PieceIndex}",
        peer.PeerId,
        pieceIndex);

    try
    {
        var pieceData = await GetPieceAsync(pieceIndex, ct);
        await peer.SendPieceAsync(pieceIndex, pieceData, ct);

        stopwatch.Stop();

        var throughput = pieceData.Length / stopwatch.Elapsed.TotalSeconds;

        _logger.LogInformation(
            "Piece transfer complete: PeerId={PeerId}, PieceIndex={PieceIndex}, " +
            "Size={Size} bytes, Duration={Duration}ms, Throughput={Throughput:F2} bytes/sec",
            peer.PeerId,
            pieceIndex,
            pieceData.Length,
            stopwatch.ElapsedMilliseconds,
            throughput);
    }
    catch (Exception ex)
    {
        stopwatch.Stop();

        _logger.LogError(
            ex,
            "Piece transfer failed: PeerId={PeerId}, PieceIndex={PieceIndex}, " +
            "Duration={Duration}ms, Error={ErrorMessage}",
            peer.PeerId,
            pieceIndex,
            stopwatch.ElapsedMilliseconds,
            ex.Message);
        throw;
    }
}
```

## Log Output Examples

### Console Output (Development)

```
[14:23:45 INF] Starting download: FileName=ubuntu.iso, Size=3758096384 bytes, Pieces=14336 {"CorrelationId":"abc123-def456","Operation":"Download","InfoHash":"a1b2c3d4e5f6g7h8"}
[14:23:46 DBG] Step 1: Announcing to tracker {"CorrelationId":"abc123-def456","Operation":"Download"}
[14:23:47 INF] Received 50 peers from tracker {"CorrelationId":"abc123-def456","Operation":"Download","PeerCount":50}
[14:23:48 DBG] Step 2: Finding peers via DHT {"CorrelationId":"abc123-def456","Operation":"Download"}
[14:23:49 INF] Found 25 peers via DHT {"CorrelationId":"abc123-def456","Operation":"Download","PeerCount":25}
[14:23:50 INF] Connected to peer 2f3a8b1c at 192.168.1.50:6881 {"CorrelationId":"abc123-def456","Operation":"Download","PeerId":"2f3a8b1c"}
[14:23:51 INF] Successfully stored piece 0/14336 (0.01%), Hash=a1b2 {"CorrelationId":"abc123-def456","Operation":"Download","PieceIndex":0}
```

### File Output (Production)

```
2025-11-24 14:23:45.123 +00:00 [INF] [Dotp2pNet.Orchestration.Engine.TorrentEngine] Starting download: FileName=ubuntu.iso, Size=3758096384 bytes, Pieces=14336 {"CorrelationId":"abc123-def456","Operation":"Download","InfoHash":"a1b2c3d4e5f6g7h8","FileName":"ubuntu.iso","TotalSize":3758096384,"MachineName":"server01","ProcessId":12345,"ThreadId":1}
2025-11-24 14:23:46.456 +00:00 [DBG] [Dotp2pNet.Orchestration.Engine.TorrentEngine] Step 1: Announcing to tracker {"CorrelationId":"abc123-def456","Operation":"Download","MachineName":"server01","ProcessId":12345,"ThreadId":1}
2025-11-24 14:23:47.789 +00:00 [INF] [Dotp2pNet.Discovery.Tracker.TrackerClient] Received 50 peers from tracker {"CorrelationId":"abc123-def456","Operation":"Download","PeerCount":50,"TrackerUrl":"http://tracker.ubuntu.com","MachineName":"server01","ProcessId":12345,"ThreadId":2}
```

## Querying Logs

### Find all logs for a specific download

```bash
grep "CorrelationId.*abc123-def456" dotp2pnet-20251124.log
```

### Find all piece transfer logs

```bash
grep "Piece transfer" dotp2pnet-20251124.log
```

### Find all errors for a specific info hash

```bash
grep "InfoHash.*a1b2c3d4" dotp2pnet-20251124.log | grep "\[ERR\]"
```

### Find all logs from a specific component

```bash
grep "\[Dotp2pNet.Discovery.Tracker.TrackerClient\]" dotp2pnet-20251124.log
```
