namespace Dotp2pNet.Core.ErrorHandling;

/// <summary>
/// Represents an error that occurred during P2P operations.
/// </summary>
/// <remarks>
/// <para>
/// The Error class provides structured error information including:
/// </para>
/// <list type="bullet">
/// <item><description><b>Category:</b> Type of error for recovery strategy selection</description></item>
/// <item><description><b>Message:</b> Human-readable error description</description></item>
/// <item><description><b>IsRetryable:</b> Whether the operation can be retried</description></item>
/// <item><description><b>InnerException:</b> Original exception for debugging</description></item>
/// </list>
/// 
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// // Create a network error
/// var error = Error.Network("Connection timeout", isRetryable: true);
/// 
/// // Create from exception
/// var error = Error.FromException(ex, ErrorCategory.Storage);
/// </code>
/// </remarks>
public class Error
{
    /// <summary>
    /// Gets the category of this error.
    /// </summary>
    public ErrorCategory Category { get; }

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets a value indicating whether this error is retryable.
    /// </summary>
    public bool IsRetryable { get; }

    /// <summary>
    /// Gets the inner exception that caused this error, if any.
    /// </summary>
    public Exception? InnerException { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Error"/> class.
    /// </summary>
    /// <param name="category">The error category.</param>
    /// <param name="message">The error message.</param>
    ///  /// <param sRetryable">Whether the operation can be retried.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public Error(
        ErrorCategory category,
        string message,
        bool isRetryable = false,
        Exception? innerException = null)
    {
        Category = category;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        IsRetryable = isRetryable;
        InnerException = innerException;
    }

    /// <summary>
    /// Creates a network error.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="isRetryable">Whether the operation can be retried.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    /// <returns>A new Error instance.</returns>
    public static Error Network(string message, bool isRetryable = true, Exception? innerException = null)
        => new(ErrorCategory.Network, message, isRetryable, innerException);

    /// <summary>
    /// Creates a protocol error.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    /// <returns>A new Error instance.</returns>
    public static Error Protocol(string message, Exception? innerException = null)
        => new(ErrorCategory.Protocol, message, isRetryable: false, innerException);

    /// <summary>
    /// Creates a data integrity error.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    /// <returns>A new Error instance.</returns>
    public static Error DataIntegrity(string message, Exception? innerException = null)
        => new(ErrorCategory.DataIntegrity, message, isRetryable: true, innerException);

    /// <summary>
    /// Creates a storage error.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="isRetryable">Whether the operation can be retried.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    /// <returns>A new Error instance.</returns>
    public static Error Storage(string message, bool isRetryable = false, Exception? innerException = null)
        => new(ErrorCategory.Storage, message, isRetryable, innerException);

    /// <summary>
    /// Creates a resource exhaustion error.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    /// <returns>A new Error instance.</returns>
    public static Error Resource(string message, Exception? innerException = null)
        => new(ErrorCategory.Resource, message, isRetryable: true, innerException);

    /// <summary>
    /// Creates an Error from an exception, automatically determining the category.
    /// </summary>
    /// <param name="exception">The exception to convert.</param>
    /// <param name="defaultCategory">The default category if type cannot be determined.</param>
    /// <returns>A new Error instance.</returns>
    public static Error FromException(Exception exception, ErrorCategory defaultCategory = ErrorCategory.Network)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Determine category based on exception type
        var category = exception switch
        {
            System.Net.Sockets.SocketException => ErrorCategory.Network,
            TimeoutException => ErrorCategory.Network,
            System.IO.IOException => ErrorCategory.Storage,
            UnauthorizedAccessException => ErrorCategory.Storage,
            InvalidOperationException => ErrorCategory.Protocol,
            ArgumentException => ErrorCategory.Protocol,
            OutOfMemoryException => ErrorCategory.Resource,
            _ => defaultCategory
        };

        // Determine if retryable based on category and exception type
        bool isRetryable = category switch
        {
            ErrorCategory.Network => true,
            ErrorCategory.Protocol => false,
            ErrorCategory.DataIntegrity => true,
            ErrorCategory.Storage => exception is not UnauthorizedAccessException,
            ErrorCategory.Resource => true,
            _ => false
        };

        return new Error(category, exception.Message, isRetryable, exception);
    }

    /// <summary>
    /// Returns a string representation of this error.
    /// </summary>
    public override string ToString()
    {
        var retryable = IsRetryable ? "retryable" : "not retryable";
        return $"[{Category}] {Message} ({retryable})";
    }
}
//     /