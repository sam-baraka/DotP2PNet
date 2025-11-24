using Dotp2pNet.Core.ErrorHandling;
using Xunit;

namespace Dotp2pNet.Tests.Unit.ErrorHandling;

/// <summary>
/// Tests for the RetryPolicy class.
/// </summary>
public class RetryPolicyTests
{
    [Fact]
    public async Task RetryPolicy_SuccessOnFirstAttempt_ReturnsImmediately()
    {
        // Arrange
        var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 100);
        int attempts = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attempts++;
            await Task.Delay(1);
            return Result<int>.Success(42);
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryPolicy_RetryableError_RetriesUpToMaxAttempts()
    {
        // Arrange
        var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 10);
        int attempts = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attempts++;
            await Task.Delay(1);
            
            if (attempts < 3)
            {
                return Result<int>.Failure(Error.Network("Transient error", isRetryable: true));
            }
            
            return Result<int>.Success(42);
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task RetryPolicy_NonRetryableError_ReturnsImmediately()
    {
        // Arrange
        var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 100);
        int attempts = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attempts++;
            await Task.Delay(1);
            return Result<int>.Failure(Error.Protocol("Invalid handshake", null));
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Protocol, result.Error.Category);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryPolicy_AllAttemptsFailWithRetryableError_ReturnsLastError()
    {
        // Arrange
        var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 10);
        int attempts = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attempts++;
            await Task.Delay(1);
            return Result<int>.Failure(Error.Network($"Attempt {attempts} failed", isRetryable: true));
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Network, result.Error.Category);
        Assert.Contains("Attempt 3 failed", result.Error.Message);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task RetryPolicy_ExceptionOverload_ConvertsExceptionToResult()
    {
        // Arrange
        var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 10);
        int attempts = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attempts++;
            await Task.Delay(1);
            
            if (attempts < 2)
            {
                throw new System.Net.Sockets.SocketException();
            }
            
            return 42;
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryPolicy_CancellationToken_CancelsOperation()
    {
        // Arrange
        var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 100);
        using var cts = new CancellationTokenSource();
        int attempts = 0;

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                attempts++;
                await Task.Delay(1);
                
                if (attempts == 1)
                {
                    cts.Cancel();
                }
                
                return Result<int>.Failure(Error.Network("Error", isRetryable: true));
            }, cts.Token);
        });

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void RetryPolicy_InvalidMaxAttempts_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(maxAttempts: 0));
    }

    [Fact]
    public void RetryPolicy_NegativeInitialDelay_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(maxAttempts: 3, initialDelayMs: -1));
    }
}
