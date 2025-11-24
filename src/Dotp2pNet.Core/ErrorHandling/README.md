# Error Handling and Resilience

This directory contains the comprehensive error handling and resilience infrastructure for Dotp2pNet.

## Overview

The error handling system provides:

- **Structured Error Representation**: `Error` and `Result<T>` types for explicit error handling
- **Error Categorization**: Five categories (Network, Protocol, DataIntegrity, Storage, Resource)
- **Retry Logic**: Exponential backoff with jitter for transient failures
- **Timeout Detection**: Track peer inactivity and piece request timeouts
- **Recovery Strategies**: Category-specific recovery actions

## Components

### Error Categories

Errors are categorized to enable appropriate recovery strategies:

| Category | Description | Recovery Strategy |
|----------|-------------|-------------------|
| **Network** | Connection failures, timeouts, socket errors | Retry with exponential backoff, mark peer temporarily unavailable |
| **Protocol** | Invalid messages, handshake failures | Disconnect immediately, blacklist peer temporarily |
| **DataIntegrity** | Hash mismatches, corrupted data | Discard piece, increment failure count, request from different peer |
| **Storage** | Disk full, I/O errors, permission denied | Pause torrent, notify user, attempt recovery if possible |
| **Resource** | Connection limits, memory pressure | Implement backpressure, close least useful connections |

### Result<T> Pattern

The `Result<T>` type makes error handling explicit and type-safe:

```csharp
// Creating results
var success = Result<int>.Success(42);
var failure = Result<int>.Failure(Error.Network("Connection failed"));

// Checking results
if (result.IsSuccess)
{
    Console.WriteLine($"Value: {result.Value}");
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}

// Pattern matching
var message = result switch
{
    { IsSuccess: true } => $"Success: {result.Value}",
    { IsSuccess: false } => $"Error: {result.Error.Message}"
};

// Functional composition
var transformed = result
    .Map(x => x * 2)
    .Bind(x => SomeOperationReturningResult(x));
```

### Retry Policy

Automatic retry with exponential backoff:

```csharp
var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 1000);

var result = await policy.ExecuteAsync(async () =>
{
    return await ConnectToPeerAsync(peer, ct);
}, ct);

// Backoff schedule:
// Attempt 1: immediate
// Attempt 2: 1000ms + jitter
// Attempt 3: 2000ms + jitter
```

### Timeout Tracking

Track activity and detect timeouts:

```csharp
// Peer inactivity timeout (2 minutes)
var tracker = new TimeoutTracker(TimeSpan.FromMinutes(2));

// Update when activity occurs
tracker.RecordActivity();

// Check for timeout
if (tracker.HasTimedOut())
{
    // Disconnect peer
}

// Get elapsed time
var elapsed = tracker.GetTimeSinceLastActivity();
```

### Piece Request Tracking

Track piece requests and detect timeouts (30 seconds):

```csharp
var requestTracker = new PieceRequestTracker(logger);

// Track a request
requestTracker.TrackRequest(peerId, pieceIndex);

// Complete when received
requestTracker.CompleteRequest(peerId, pieceIndex);

// Check for timeouts
var timedOut = requestTracker.GetTimedOutRequests();
foreach (var (peerId, pieceIndex) in timedOut)
{
    // Re-request from different peer
    requestTracker.RemoveRequest(peerId, pieceIndex);
}
```

### Error Recovery Coordinator

Determines appropriate recovery actions:

```csharp
var coordinator = new ErrorRecoveryCoordinator(logger);

var result = await SomeOperation();
if (result.IsFailure)
{
    var action = coordinator.DetermineRecoveryAction(result.Error);
    
    switch (action)
    {
        case RecoveryAction.RetryWithBackoff:
            // Retry with exponential backoff
            break;
        case RecoveryAction.DisconnectAndBlacklist:
            // Disconnect and blacklist peer
            break;
        case RecoveryAction.RequestFromDifferentPeer:
            // Try different peer
            break;
        case RecoveryAction.PauseTorrent:
            // Pause and notify user
            break;
        // ... handle other actions
    }
}
```

## Usage Examples

### Connection with Retry

```csharp
var retryHelper = new ConnectionRetryHelper(connectionManager, logger);

var result = await retryHelper.ConnectWithRetryAsync(
    ipAddress: "192.168.1.50",
    port: 6881,
    ct: cancellationToken);

if (result.IsSuccess)
{
    var connection = result.Value;
    // Use connection
}
else
{
    logger.LogError("Failed to connect after retries: {Error}", result.Error);
}
```

### Peer Timeout Detection

```csharp
// In connection monitoring loop
foreach (var connection in activeConnections)
{
    if (connection.HasTimedOut())
    {
        logger.LogWarning(
            "Peer {PeerId} timed out after {Elapsed}",
            connection.PeerId,
            connection.GetTimeSinceLastActivity());
        
        await connectionManager.RemoveConnectionAsync(connection);
    }
}
```

### Piece Request Timeout Handling

```csharp
// Periodically check for timed-out requests
var timedOut = requestTracker.GetTimedOutRequests();

foreach (var (peerId, pieceIndex) in timedOut)
{
    logger.LogWarning(
        "Piece request timed out: peer={PeerId}, piece={PieceIndex}",
        peerId,
        pieceIndex);
    
    // Remove the timed-out request
    requestTracker.RemoveRequest(peerId, pieceIndex);
    
    // Re-request from a different peer
    var differentPeer = SelectDifferentPeer(peerId, pieceIndex);
    if (differentPeer != null)
    {
        await RequestPieceAsync(differentPeer, pieceIndex);
        requestTracker.TrackRequest(differentPeer.PeerId, pieceIndex);
    }
}
```

### Comprehensive Error Handling

```csharp
public async Task<Result<byte[]>> DownloadPieceAsync(
    string peerId,
    int pieceIndex,
    CancellationToken ct)
{
    try
    {
        // Track the request
        _requestTracker.TrackRequest(peerId, pieceIndex);
        
        // Send request with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        
        var pieceData = await RequestPieceFromPeerAsync(peerId, pieceIndex, timeoutCts.Token);
        
        // Verify hash
        if (!VerifyPieceHash(pieceData, pieceIndex))
        {
            _requestTracker.CompleteRequest(peerId, pieceIndex);
            return Result<byte[]>.Failure(
                Error.DataIntegrity($"Hash mismatch for piece {pieceIndex} from peer {peerId}"));
        }
        
        // Success
        _requestTracker.CompleteRequest(peerId, pieceIndex);
        return Result<byte[]>.Success(pieceData);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // User cancellation
        _requestTracker.RemoveRequest(peerId, pieceIndex);
        throw;
    }
    catch (OperationCanceledException)
    {
        // Timeout
        _requestTracker.RemoveRequest(peerId, pieceIndex);
        return Result<byte[]>.Failure(
            Error.Network($"Piece request timed out: piece {pieceIndex} from peer {peerId}", isRetryable: true));
    }
    catch (Exception ex)
    {
        _requestTracker.RemoveRequest(peerId, pieceIndex);
        return Result<byte[]>.Failure(ex);
    }
}
```

## Design Principles

1. **Explicit Error Handling**: Use `Result<T>` instead of exceptions for expected failures
2. **Categorization**: Group errors by type to enable appropriate recovery
3. **Retryability**: Mark errors as retryable or not to guide retry logic
4. **Observability**: Log all errors with appropriate context
5. **Graceful Degradation**: Fail gracefully and provide recovery options
6. **No Silent Failures**: All errors must be handled or propagated

## Benefits

- **Type Safety**: Compiler ensures error handling
- **Explicit**: No hidden exceptions
- **Composable**: Chain operations with Map/Bind
- **Performance**: No exception overhead for expected failures
- **Testable**: Easy to test error paths
- **Maintainable**: Clear error handling patterns

## Testing

Error handling components are designed to be easily testable:

```csharp
[Fact]
public async Task RetryPolicy_RetriesOnRetryableError()
{
    var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 100);
    int attempts = 0;
    
    var result = await policy.ExecuteAsync(async () =>
    {
        attempts++;
        if (attempts < 3)
        {
            return Result<int>.Failure(Error.Network("Transient error", isRetryable: true));
        }
        return Result<int>.Success(42);
    });
    
    Assert.True(result.IsSuccess);
    Assert.Equal(42, result.Value);
    Assert.Equal(3, attempts);
}

[Fact]
public void TimeoutTracker_DetectsTimeout()
{
    var tracker = new TimeoutTracker(TimeSpan.FromMilliseconds(100));
    
    Assert.False(tracker.HasTimedOut());
    
    Thread.Sleep(150);
    
    Assert.True(tracker.HasTimedOut());
}
```

## Future Enhancements

- Circuit breaker pattern for failing peers
- Adaptive timeout based on peer performance
- Error rate limiting per peer
- Automatic peer reputation scoring
- Telemetry and metrics integration
