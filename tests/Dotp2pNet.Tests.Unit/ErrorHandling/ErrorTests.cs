using Dotp2pNet.Core.ErrorHandling;
using Xunit;

namespace Dotp2pNet.Tests.Unit.ErrorHandling;

/// <summary>
/// Tests for the Error class.
/// </summary>
public class ErrorTests
{
    [Fact]
    public void Error_Network_CreatesNetworkError()
    {
        // Arrange & Act
        var error = Error.Network("Connection failed", isRetryable: true);

        // Assert
        Assert.Equal(ErrorCategory.Network, error.Category);
        Assert.Equal("Connection failed", error.Message);
        Assert.True(error.IsRetryable);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Error_Protocol_CreatesProtocolError()
    {
        // Arrange & Act
        var error = Error.Protocol("Invalid handshake");

        // Assert
        Assert.Equal(ErrorCategory.Protocol, error.Category);
        Assert.Equal("Invalid handshake", error.Message);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public void Error_DataIntegrity_CreatesDataIntegrityError()
    {
        // Arrange & Act
        var error = Error.DataIntegrity("Hash mismatch");

        // Assert
        Assert.Equal(ErrorCategory.DataIntegrity, error.Category);
        Assert.Equal("Hash mismatch", error.Message);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public void Error_Storage_CreatesStorageError()
    {
        // Arrange & Act
        var error = Error.Storage("Disk full", isRetryable: false);

        // Assert
        Assert.Equal(ErrorCategory.Storage, error.Category);
        Assert.Equal("Disk full", error.Message);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public void Error_Resource_CreatesResourceError()
    {
        // Arrange & Act
        var error = Error.Resource("Connection limit reached");

        // Assert
        Assert.Equal(ErrorCategory.Resource, error.Category);
        Assert.Equal("Connection limit reached", error.Message);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public void Error_FromException_SocketException_CreatesNetworkError()
    {
        // Arrange
        var exception = new System.Net.Sockets.SocketException();

        // Act
        var error = Error.FromException(exception);

        // Assert
        Assert.Equal(ErrorCategory.Network, error.Category);
        Assert.True(error.IsRetryable);
        Assert.Same(exception, error.InnerException);
    }

    [Fact]
    public void Error_FromException_TimeoutException_CreatesNetworkError()
    {
        // Arrange
        var exception = new TimeoutException("Connection timed out");

        // Act
        var error = Error.FromException(exception);

        // Assert
        Assert.Equal(ErrorCategory.Network, error.Category);
        Assert.True(error.IsRetryable);
        Assert.Equal("Connection timed out", error.Message);
    }

    [Fact]
    public void Error_FromException_IOException_CreatesStorageError()
    {
        // Arrange
        var exception = new System.IO.IOException("Disk error");

        // Act
        var error = Error.FromException(exception);

        // Assert
        Assert.Equal(ErrorCategory.Storage, error.Category);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public void Error_FromException_UnauthorizedAccessException_CreatesNonRetryableStorageError()
    {
        // Arrange
        var exception = new UnauthorizedAccessException("Access denied");

        // Act
        var error = Error.FromException(exception);

        // Assert
        Assert.Equal(ErrorCategory.Storage, error.Category);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public void Error_FromException_InvalidOperationException_CreatesProtocolError()
    {
        // Arrange
        var exception = new InvalidOperationException("Invalid state");

        // Act
        var error = Error.FromException(exception);

        // Assert
        Assert.Equal(ErrorCategory.Protocol, error.Category);
        Assert.False(error.IsRetryable);
    }

    [Fact]
    public void Error_ToString_ReturnsFormattedString()
    {
        // Arrange
        var error = Error.Network("Connection failed", isRetryable: true);

        // Act
        var result = error.ToString();

        // Assert
        Assert.Contains("[Network]", result);
        Assert.Contains("Connection failed", result);
        Assert.Contains("retryable", result);
    }
}
