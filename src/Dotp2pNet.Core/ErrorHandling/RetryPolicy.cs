namespace Dotp2pNet.Core.ErrorHandling;

/// <summary>
/// Defines a retry policy with exponential backoff for handling transient failures.
/// </summary>
/// <remarks>
/// <para>
/// RetryPolicy implements exponential backoff with jitter to handle transient failures gracefully:
/// </para>
/// <list type="bullet">
/// <item><description><b>Exponential Backoff:</b> Delay doubles after each retry (1s, 2s, 4s, ...)</description></item>
/// <item><description><b>Jitter:</b> Random variation to prevent thundering herd</description></item>
/// <item><description><b>Max Attempts:</b> Configurable limit to prevent infinite retries</description></item>
/// <item><description><b>Selective Retry:</b> Only retries errors marked as retryable</description></item>
/// </list>
/// 
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// var policy = new RetryPolicy(maxAttempts: 3, initialDelayMs: 1000);
/// 
/// var result = await policy.ExecuteAsync(async () =>
/// {
///     return await ConnectToPeerAsync(peer, ct);
/// }, ct);
/// 
/// if (result.IsSuccess)
/// {
///     // Use result.Value
/// }
/// else
/// {
///     // Handle result.Error
/// }
/// </code>
/// 
/// <para><b>Backoff Calculation:</b></para>
/// <code>
/// delay = initialDelay * (2 ^ attempt) + random(0, initialDelay)
/// 
/// Example with initialDelay = 1000ms:
/// Attempt 1: 1000ms + jitter
/// Attempt 2: 2000ms + jitter
/// Attempt 3: 4000ms + jitter
/// </code>
/// </remarks>
public class RetryPolicy
{
    private readonly int _maxAttempts;
    private readonly int _initialDelayMs;
    private readonly Random _random;

    /// <summary>
    /// Gets the maximum number of retry attempts.
    /// </summary>
    public int MaxAttempts => _maxAttempts;

    /// <summary>
    /// Gets the initial delay in milliseconds.
    /// </summary>
    public int InitialDelayMs => _initialDelayMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryPolicy"/> class.
    /// </summary>
    /// <param name="maxAttempts">Maximum number of attempts (including initial attempt).</param>
    /// <param name="initialDelayMs">Initial delay in milliseconds before first retry.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are invalid.</exception>
    public RetryPolicy(int maxAttempts = 3, int initialDelayMs = 1000)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                "Max attempts must be at least 1");
        }

        if (initialDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialDelayMs),
                "Initial delay must be non-negative");
        }

        _maxAttempts = maxAttempts;
        _initialDelayMs = initialDelayMs;
        _random = new Random();
    }

    /// <summary>
    /// Executes an async operation with retry logic.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    /// <remarks>
    /// The operation will be retried up to MaxAttempts times if it returns a retryable error.
    /// Non-retryable errors are returned immediately without retry.
    /// </remarks>
    public async Task<Result<T>> ExecuteAsync<T>(
        Func<Task<Result<T>>> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Result<T>? lastResult = null;
        
        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // Execute the operation
            lastResult = await operation().ConfigureAwait(false);

            // If successful, return immediately
            if (lastResult.IsSuccess)
            {
                return lastResult;
            }

            // If error is not retryable, return immediately
            if (!lastResult.Error.IsRetryable)
            {
                return lastResult;
            }

            // If this was the last attempt, return the error
            if (attempt == _maxAttempts)
            {
                return lastResult;
            }

            // Calculate delay with exponential backoff and jitter
            int delay = CalculateDelay(attempt);

            // Wait before retrying
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        // This should never be reached, but return the last result just in case
        return lastResult!;
    }

    /// <summary>
    /// Executes an async operation that might throw exceptions, converting them to Results.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    /// <remarks>
    /// This overload catches exceptions and converts them to Result&lt;T&gt; failures,
    /// then applies retry logic based on whether the error is retryable.
    /// </remarks>
    public async Task<Result<T>> ExecuteAsync<T>(
        Func<Task<T>> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return await ExecuteAsync(async () =>
        {
            try
            {
                var value = await operation().ConfigureAwait(false);
                return Result<T>.Success(value);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Don't convert cancellation to Result
            }
            catch (Exception ex)
            {
                return Result<T>.Failure(ex);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Calculates the delay for a given attempt using exponential backoff with jitter.
    /// </summary>
    /// <param name="attempt">The attempt number (1-based).</param>
    /// <returns>The delay in milliseconds.</returns>
    private int CalculateDelay(int attempt)
    {
        // Exponential backoff: initialDelay * 2^(attempt-1)
        int exponentialDelay = _initialDelayMs * (1 << (attempt - 1));

        // Add jitter: random value between 0 and initialDelay
        int jitter = _random.Next(0, _initialDelayMs);

        return exponentialDelay + jitter;
    }
}
