namespace Dotp2pNet.Core.ErrorHandling;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <remarks>
/// <para>
/// Result&lt;T&gt; implements the Result pattern for explicit error handling without exceptions.
/// This pattern makes error handling explicit and forces callers to handle both success and failure cases.
/// </para>
/// 
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// // Creating results
/// var success = Result&lt;int&gt;.Success(42);
/// var failure = Result&lt;int&gt;.Failure(Error.Network("Connection failed"));
/// 
/// // Checking results
/// if (result.IsSuccess)
/// {
///     Console.WriteLine($"Value: {result.Value}");
/// }
/// else
/// {
///     Console.WriteLine($"Error: {result.Error}");
/// }
/// 
/// // Pattern matching
/// var message = result switch
/// {
///     { IsSuccess: true } => $"Success: {result.Value}",
///     { IsSuccess: false } => $"Error: {result.Error.Message}"
/// };
/// </code>
/// 
/// <para><b>Benefits:</b></para>
/// <list type="bullet">
/// <item><description>Explicit error handling - no hidden exceptions</description></item>
/// <item><description>Type-safe - compiler ensures error handling</description></item>
/// <item><description>Composable - can chain operations with Match/Bind</description></item>
/// <item><description>Performance - no exception overhead for expected failures</description></item>
/// </list>
/// </remarks>
public class Result<T>
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing Value on a failed result.</exception>
    public T Value
    {
        get
        {
            if (!IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Cannot access Value on a failed result. Error: {Error}");
            }
            return _value!;
        }
    }

    /// <summary>
    /// Gets the error.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing Error on a successful result.</exception>
    public Error Error
    {
        get
        {
            if (IsSuccess)
            {
                throw new InvalidOperationException("Cannot access Error on a successful result");
            }
            return _error!;
        }
    }

    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
        _error = null;
    }

    private Result(Error error)
    {
        IsSuccess = false;
        _value = default;
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful Result.</returns>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed Result.</returns>
    public static Result<T> Failure(Error error) => new(error);

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="defaultCategory">The default error category.</param>
    /// <returns>A failed Result.</returns>
    public static Result<T> Failure(Exception exception, ErrorCategory defaultCategory = ErrorCategory.Network)
        => new(Error.FromException(exception, defaultCategory));

    /// <summary>
    /// Executes one of two functions depending on whether the result is success or failure.
    /// </summary>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onFailure">Function to execute on failure.</param>
    /// <returns>The result of the executed function.</returns>
    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(_error!);
    }

    /// <summary>
    /// Transforms the success value using the specified function.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="mapper">Function to transform the value.</param>
    /// <returns>A new Result with the transformed value, or the original error.</returns>
    public Result<TResult> Map<TResult>(Func<T, TResult> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? Result<TResult>.Success(mapper(_value!))
            : Result<TResult>.Failure(_error!);
    }

    /// <summary>
    ///  /// Chains anothen that returns a Result.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="binder">Function that returns a new Result.</param>
    /// <returns>The result of the binder function, or the original error.</returns>
    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        return IsSuccess
            ? binder(_value!)
            : Result<TResult>.Failure(_error!);
    }

    /// <summary>
    /// Returns the value if successful, otherwise returns the default value.
    /// </summary>
    /// <param name="defaultValue">The default value to return on failure.</param>
    /// <returns>The value or default.</returns>
    public T GetValueOrDefault(T defaultValue = default!)
        => IsSuccess ? _value! : defaultValue;

    /// <summary>
    ///  ///tring representation of this result.
    /// </summary>
    public override string ToString()
        => IsSuccess ? $"Success({_value})" : $"Failure({_error})";
}

/// <summary>
/// Provides factory methods for creating Result instances without specifying the type parameter.
/// </summary>
public static class Result
{
    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    public static Result<T> Failure<T>(Exception exception, ErrorCategory defaultCategory = ErrorCategory.Network)
        => Result<T>.Failure(exception, defaultCategory);
}
