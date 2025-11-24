using Dotp2pNet.Core.ErrorHandling;
using Xunit;

namespace Dotp2pNet.Tests.Unit.ErrorHandling;

/// <summary>
/// Tests for the Result&lt;T&gt; class.
/// </summary>
public class ResultTests
{
    [Fact]
    public void Result_Success_CreatesSuccessResult()
    {
        // Arrange & Act
        var result = Result<int>.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Result_Failure_CreatesFailureResult()
    {
        // Arrange
        var error = Error.Network("Connection failed");

        // Act
        var result = Result<int>.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Result_Success_AccessingError_ThrowsException()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Result_Failure_AccessingValue_ThrowsException()
    {
        // Arrange
        var error = Error.Network("Connection failed");
        var result = Result<int>.Failure(error);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("Connection failed", exception.Message);
    }

    [Fact]
    public void Result_Match_ExecutesSuccessFunction()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var output = result.Match(
            onSuccess: value => $"Success: {value}",
            onFailure: error => $"Failure: {error.Message}");

        // Assert
        Assert.Equal("Success: 42", output);
    }

    [Fact]
    public void Result_Match_ExecutesFailureFunction()
    {
        // Arrange
        var error = Error.Network("Connection failed");
        var result = Result<int>.Failure(error);

        // Act
        var output = result.Match(
            onSuccess: value => $"Success: {value}",
            onFailure: error => $"Failure: {error.Message}");

        // Assert
        Assert.Equal("Failure: Connection failed", output);
    }

    [Fact]
    public void Result_Map_TransformsSuccessValue()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(84, mapped.Value);
    }

    [Fact]
    public void Result_Map_PreservesFailure()
    {
        // Arrange
        var error = Error.Network("Connection failed");
        var result = Result<int>.Failure(error);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Same(error, mapped.Error);
    }

    [Fact]
    public void Result_Bind_ChainsSuccessfulOperations()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var bound = result.Bind(x => Result<string>.Success($"Value: {x}"));

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal("Value: 42", bound.Value);
    }

    [Fact]
    public void Result_Bind_PreservesFirstFailure()
    {
        // Arrange
        var error = Error.Network("Connection failed");
        var result = Result<int>.Failure(error);

        // Act
        var bound = result.Bind(x => Result<string>.Success($"Value: {x}"));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void Result_Bind_PreservesSecondFailure()
    {
        // Arrange
        var result = Result<int>.Success(42);
        var error = Error.Network("Processing failed");

        // Act
        var bound = result.Bind(x => Result<string>.Failure(error));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void Result_GetValueOrDefault_ReturnsValueOnSuccess()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var value = result.GetValueOrDefault(0);

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void Result_GetValueOrDefault_ReturnsDefaultOnFailure()
    {
        // Arrange
        var error = Error.Network("Connection failed");
        var result = Result<int>.Failure(error);

        // Act
        var value = result.GetValueOrDefault(0);

        // Assert
        Assert.Equal(0, value);
    }

    [Fact]
    public void Result_ToString_Success_ReturnsFormattedString()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var str = result.ToString();

        // Assert
        Assert.Contains("Success", str);
        Assert.Contains("42", str);
    }

    [Fact]
    public void Result_ToString_Failure_ReturnsFormattedString()
    {
        // Arrange
        var error = Error.Network("Connection failed");
        var result = Result<int>.Failure(error);

        // Act
        var str = result.ToString();

        // Assert
        Assert.Contains("Failure", str);
        Assert.Contains("Connection failed", str);
    }

    [Fact]
    public void Result_StaticFactory_Success_CreatesSuccessResult()
    {
        // Arrange & Act
        var result = Result.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Result_StaticFactory_Failure_CreatesFailureResult()
    {
        // Arrange
        var error = Error.Network("Connection failed");

        // Act
        var result = Result.Failure<int>(error);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }
}
