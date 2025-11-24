# Error Handling Examples

This document provides practical examples of using the error handling system in Dotp2pNet.

## Example 1: Connection with Retry

```csharp
using Dotp2pNet.Core.ErrorHandling;
using Dotp2pNet.Networking.Connections;

public class PeerConnectionService
{
    private readonly ConnectionRetryHelper _retryHelper;
    private readonly ILogger _logger;

    public async Task<Result<IPeerConnection>> ConnectToPeerAsync(
        string ipAddress,
        int port,
        CancellationToken ct)
    {
        _logger.LogInformation("Attempting to connect to {IpAddress}:{Port}", ipAddress, port);

        // Use retry helper for automatic retry with exponential backoff
        var result = await _retryHelper.ConnectWithRetryAsync(ipAddress, port, ct);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Successfully connected to {IpAddress}:{Port}", ipAddress, port);
            return result;
        }

        // Log the error
        _logger.LogError(
            "Failed to connect to {IpAddress}:{Port} after retries: {Error}",
            ipAddress,
            port,
            result.Error);

        return result;
    }
}
```

## Example 2: Piece Download with Timeout and Error Recovery

```csharp
using Dotp2pNet.Core.ErrorHandling;

public class PieceDownloader
{
    private readonly PieceRequestTracker _requestTracker;
    private readonly ErrorRecoveryCoordinator _recoveryCoordinator;
    private readonly ILogger _logger;

    public async Task<Result<byte[]>> DownloadPieceAsync(
        string peerId,
        int pieceIndex,
        CancellationToken ct)
    {
        try
        {
            // Track the request
            _requestTracker.TrackRequest(peerId, pieceIndex);

            // Create timeout for piece request (30 seconds)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            // Request the piece
            var pieceData = await RequestPieceFromPeerAsync(peerId, pieceIndex, timeoutCts.Token);

            // Verify hash
            if (!VerifyPieceHash(pieceData, pieceIndex))
            {
                _requestTracker.CompleteRequest(peerId, pieceIndex);
                
                var error = Error.DataIntegrity(
                    $"Hash mismatch for piece {pieceIndex} from peer {peerId}");
                
                // Determine recovery action
                var action = _recoveryCoordinator.DetermineRecoveryAction(error);
                
                if (action == RecoveryAction.RequestFromDifferentPeer)
                {
                    _logger.LogWarning(
                        "Hash mismatch for piece {PieceIndex} from peer {PeerId}, will request from different peer",
                        pieceIndex,
                        peerId);
                }
                
                return Result<byte[]>.Failure(error);
            }

            // Success
            _requestTracker.CompleteRequest(peerId, pieceIndex);
            return Result<byte[]>.Success(pieceData);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancellation - propagate
            _requestTracker.RemoveRequest(peerId, pieceIndex);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Timeout
            _requestTracker.RemoveRequest(peerId, pieceIndex);
            
            var error = Error.Network(
                $"Piece request timed out: piece {pieceIndex} from peer {peerId}",
                isRetryable: true);
            
            _logger.LogWarning(
                "Piece request timed out: piece={PieceIndex}, peer={PeerId}",
                pieceIndex,
                peerId);
            
            return Result<byte[]>.Failure(error);
        }
        catch (Exception ex)
        {
            _requestTracker.RemoveRequest(peerId, pieceIndex);
            return Result<byte[]>.Failure(ex);
        }
    }

    private async Task<byte[]> RequestPieceFromPeerAsync(
        string peerId,
        int pieceIndex,
        CancellationToken ct)
    {
        // Implementation details...
        await Task.Delay(100, ct); // Placeholder
        return new byte[16384];
    }

    private bool VerifyPieceHash(byte[] data, int pieceIndex)
    {
        // Implementation details...
        return true;
    }
}
```

## Example 3: Monitoring Peer Timeouts

```csharp
using Dotp2pNet.Core.ErrorHandling;

public class PeerMonitor
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger _logger;
    private readonly Timer _monitorTimer;

    public PeerMonitor(IConnectionManager connectionManager, ILogger logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        
        // Check for timeouts every 30 seconds
        _monitorTimer = new Timer(CheckForTimeouts, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private async void CheckForTimeouts(object? state)
    {
        try
        {
            var connections = _connectionManager.ActiveConnections.ToList();
            
            foreach (var connection in connections)
            {
                // Check if peer has timed out (2 minutes of inactivity)
                if (connection.HasTimedOut())
                {
                    var elapsed = connection.GetTimeSinceLastActivity();
                    
                    _logger.LogWarning(
                        "Peer {PeerId} has been inactive for {Elapsed}, disconnecting",
                        connection.PeerId,
                        elapsed);

                    // Disconnect the peer
                    await _connectionManager.RemoveConnectionAsync(connection);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for peer timeouts");
        }
    }

    public void Dispose()
    {
        _monitorTimer?.Dispose();
    }
}
```

## Example 4: Handling Piece Request Timeouts

```csharp
using Dotp2pNet.Core.ErrorHandling;

public class PieceRequestManager
{
    private readonly PieceRequestTracker _requestTracker;
    private readonly ILogger _logger;
    private readonly Timer _timeoutCheckTimer;

    public PieceRequestManager(ILogger logger)
    {
        _logger = logger;
        _requestTracker = new PieceRequestTracker(logger);
        
        // Check for timeouts every 5 seconds
        _timeoutCheckTimer = new Timer(
            CheckForTimeouts,
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
    }

    public void TrackRequest(string peerId, int pieceIndex)
    {
        _requestTracker.TrackRequest(peerId, pieceIndex);
    }

    public void CompleteRequest(string peerId, int pieceIndex)
    {
        _requestTracker.CompleteRequest(peerId, pieceIndex);
    }

    private async void CheckForTimeouts(object? state)
    {
        try
        {
            // Get all timed-out requests
            var timedOut = _requestTracker.GetTimedOutRequests();

            foreach (var (peerId, pieceIndex) in timedOut)
            {
                _logger.LogWarning(
                    "Piece request timed out: peer={PeerId}, piece={PieceIndex}",
                    peerId,
                    pieceIndex);

                // Remove the timed-out request
                _requestTracker.RemoveRequest(peerId, pieceIndex);

                // Re-request from a different peer
                await ReRequestPieceFromDifferentPeerAsync(peerId, pieceIndex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for piece request timeouts");
        }
    }

    private async Task ReRequestPieceFromDifferentPeerAsync(string originalPeerId, int pieceIndex)
    {
        // Find a different peer that has this piece
        var differentPeer = await FindPeerWithPieceAsync(pieceIndex, excludePeerId: originalPeerId);
        
        if (differentPeer != null)
        {
            _logger.LogInformation(
                "Re-requesting piece {PieceIndex} from different peer {NewPeerId} (original peer: {OriginalPeerId})",
                pieceIndex,
                differentPeer.PeerId,
                originalPeerId);

            // Track the new request
            _requestTracker.TrackRequest(differentPeer.PeerId, pieceIndex);

            // Send the request
            await SendPieceRequestAsync(differentPeer, pieceIndex);
        }
        else
        {
            _logger.LogWarning(
                "No alternative peer found for piece {PieceIndex}",
                pieceIndex);
        }
    }

    private async Task<PeerInfo?> FindPeerWithPieceAsync(int pieceIndex, string excludePeerId)
    {
        // Implementation details...
        await Task.Delay(10);
        return null;
    }

    private async Task SendPieceRequestAsync(PeerInfo peer, int pieceIndex)
    {
        // Implementation details...
        await Task.Delay(10);
    }

    public void Dispose()
    {
        _timeoutCheckTimer?.Dispose();
    }
}
```

## Example 5: Comprehensive Error Recovery

```csharp
using Dotp2pNet.Core.ErrorHandling;

public class TorrentDownloadCoordinator
{
    private readonly ErrorRecoveryCoordinator _recoveryCoordinator;
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger _logger;

    public async Task<Result<bool>> DownloadTorrentAsync(
        TorrentMetadata metadata,
        CancellationToken ct)
    {
        var retryPolicy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 1000);

        var result = await retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                // Attempt to download
                await PerformDownloadAsync(metadata, ct);
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                var error = Error.FromException(ex);
                
                // Determine recovery action
                var action = _recoveryCoordinator.DetermineRecoveryAction(error);
                
                // Handle the error based on recovery action
                await HandleRecoveryActionAsync(action, error);
                
                return Result<bool>.Failure(error);
            }
        }, ct);

        return result;
    }

    private async Task HandleRecoveryActionAsync(RecoveryAction action, Error error)
    {
        switch (action)
        {
            case RecoveryAction.RetryWithBackoff:
                _logger.LogInformation("Will retry operation with backoff");
                break;

            case RecoveryAction.DisconnectAndBlacklist:
                _logger.LogWarning("Disconnecting and blacklisting peer due to protocol error");
                // Disconnect peer and add to blacklist
                break;

            case RecoveryAction.RequestFromDifferentPeer:
                _logger.LogInformation("Will request from different peer");
                // Find and connect to different peer
                break;

            case RecoveryAction.PauseTorrent:
                _logger.LogError("Pausing torrent due to storage error: {Error}", error.Message);
                // Pause the torrent
                break;

            case RecoveryAction.CloseLeastUsefulConnections:
                _logger.LogWarning("Closing least useful connections to free resources");
                await CloseLeastUsefulConnectionsAsync();
                break;

            case RecoveryAction.ImplementBackpressure:
                _logger.LogWarning("Implementing backpressure due to resource pressure");
                // Reduce request rate
                break;

            case RecoveryAction.Fail:
                _logger.LogError("Operation failed without recovery: {Error}", error.Message);
                break;
        }
    }

    private async Task CloseLeastUsefulConnectionsAsync()
    {
        // Get connections sorted by usefulness (upload rate, download rate, etc.)
        var connections = _connectionManager.ActiveConnections
            .OrderBy(c => GetConnectionUsefulness(c))
            .Take(5)
            .ToList();

        foreach (var connection in connections)
        {
            _logger.LogInformation("Closing connection to peer {PeerId}", connection.PeerId);
            await _connectionManager.RemoveConnectionAsync(connection);
        }
    }

    private double GetConnectionUsefulness(IPeerConnection connection)
    {
        // Calculate usefulness score based on metrics
        return 0.0; // Placeholder
    }

    private async Task PerformDownloadAsync(TorrentMetadata metadata, CancellationToken ct)
    {
        // Implementation details...
        await Task.Delay(100, ct);
    }
}
```

## Example 6: Using Result Pattern for Functional Composition

```csharp
using Dotp2pNet.Core.ErrorHandling;

public class PieceProcessor
{
    public async Task<Result<ProcessedPiece>> ProcessPieceAsync(int pieceIndex)
    {
        // Chain operations using Map and Bind
        var result = await DownloadPieceAsync(pieceIndex)
            .Bind(async data => await VerifyPieceAsync(data, pieceIndex))
            .Bind(async data => await DecompressPieceAsync(data))
            .Map(data => new ProcessedPiece(pieceIndex, data));

        return result;
    }

    private async Task<Result<byte[]>> DownloadPieceAsync(int pieceIndex)
    {
        try
        {
            // Download logic
            await Task.Delay(100);
            return Result<byte[]>.Success(new byte[16384]);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(ex);
        }
    }

    private async Task<Result<byte[]>> VerifyPieceAsync(byte[] data, int pieceIndex)
    {
        await Task.Delay(10);
        
        if (ComputeHash(data) == GetExpectedHash(pieceIndex))
        {
            return Result<byte[]>.Success(data);
        }
        
        return Result<byte[]>.Failure(
            Error.DataIntegrity($"Hash mismatch for piece {pieceIndex}"));
    }

    private async Task<Result<byte[]>> DecompressPieceAsync(byte[] data)
    {
        try
        {
            await Task.Delay(10);
            // Decompression logic
            return Result<byte[]>.Success(data);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(
                Error.Storage("Decompression failed", isRetryable: false, ex));
        }
    }

    private byte[] ComputeHash(byte[] data) => new byte[32];
    private byte[] GetExpectedHash(int pieceIndex) => new byte[32];
}

public class ProcessedPiece
{
    public int Index { get; }
    public byte[] Data { get; }

    public ProcessedPiece(int index, byte[] data)
    {
        Index = index;
        Data = data;
    }
}

// Extension methods for async Result operations
public static class ResultExtensions
{
    public static async Task<Result<TResult>> Bind<T, TResult>(
        this Task<Result<T>> resultTask,
        Func<T, Task<Result<TResult>>> binder)
    {
        var result = await resultTask;
        if (result.IsFailure)
        {
            return Result<TResult>.Failure(result.Error);
        }
        return await binder(result.Value);
    }

    public static async Task<Result<TResult>> Map<T, TResult>(
        this Task<Result<T>> resultTask,
        Func<T, TResult> mapper)
    {
        var result = await resultTask;
        return result.Map(mapper);
    }
}
```

## Best Practices

1. **Use Result<T> for Expected Failures**: Don't throw exceptions for expected failures like network errors or hash mismatches.

2. **Categorize Errors Appropriately**: Use the correct error category to enable proper recovery strategies.

3. **Log with Context**: Always include relevant context (peer ID, piece index, etc.) in log messages.

4. **Track Timeouts**: Use TimeoutTracker for peer inactivity and PieceRequestTracker for piece requests.

5. **Implement Retry Logic**: Use RetryPolicy for transient failures with exponential backoff.

6. **Handle Cancellation**: Always check for user cancellation vs. timeout cancellation.

7. **Clean Up Resources**: Remove tracked requests and close connections when operations complete or fail.

8. **Monitor Continuously**: Use timers to periodically check for timeouts and take action.

9. **Provide Recovery Options**: Use ErrorRecoveryCoordinator to determine appropriate recovery actions.

10. **Test Error Paths**: Write tests for error scenarios to ensure proper handling.
