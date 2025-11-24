namespace Dotp2pNet.Core.ErrorHandling;

/// <summary>
/// Tracks activity timestamps to detect timeouts.
/// </summary>
/// <remarks>
/// <para>
/// TimeoutTracker helps detect inactive peers and stale operations:
/// </para>
/// <list type="bullet">
/// <item><description><b>Peer Timeout:</b> Detect peers that haven't sent messages in 2 minutes</description></item>
/// <item><description><b>Request Timeout:</b> Detect piece requests that haven't completed in 30 seconds</description></item>
/// <item><description><b>Thread-Safe:</b> Uses Interlocked operations for lock-free updates</description></item>
/// </list>
/// 
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// var tracker = new TimeoutTracker(TimeSpan.FromMinutes(2));
/// 
/// // Update activity when message received
/// tracker.RecordActivity();
/// 
/// // Check for timeout
/// if (tracker.HasTimedOut())
/// {
///     // Handle timeout - disconnect peer
/// }
/// 
/// // Get time since last activity
/// var elapsed = tracker.GetTimeSinceLastActivity();
/// </code>
/// </remarks>
public class TimeoutTracker
{
    private long _lastActivityTicks;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Gets the timeout duration.
    /// </summary>
    public TimeSpan Timeout => _timeout;

    /// <summary>
    /// Gets the timestamp of the last recorded activity.
    /// </summary>
    public DateTime LastActivity => new(Interlocked.Read(ref _lastActivityTicks));

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutTracker"/> class.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when timeout is negative.</exception>
    public TimeoutTracker(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be non-negative");
        }

        _timeout = timeout;
        _lastActivityTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Records activity at the current time.
    /// </summary>
    /// <remarks>
    /// This method is thread-safe and can be called from multiple threads concurrently.
    /// </remarks>
    public void RecordActivity()
    {
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// Checks if the timeout has been exceeded.
    /// </summary>
    /// <returns>True if the timeout has been exceeded, false otherwise.</returns>
    public bool HasTimedOut()
    {
        var lastActivity = new DateTime(Interlocked.Read(ref _lastActivityTicks));
        var elapsed = DateTime.UtcNow - lastActivity;
        return elapsed >= _timeout;
    }

    /// <summary>
    /// Gets the time elapsed since the last activity.
    /// </summary>
    /// <returns>The time elapsed since last activity.</returns>
    public TimeSpan GetTimeSinceLastActivity()
    {
        var lastActivity = new DateTime(Interlocked.Read(ref _lastActivityTicks));
        return DateTime.UtcNow - lastActivity;
    }

    /// <summary>
    /// Gets the remaining time before timeout.
    /// </summary>
    /// <returns>The remaining time, or TimeSpan.Zero if already timed out.</returns>
    public TimeSpan GetRemainingTime()
    {
        var elapsed = GetTimeSinceLastActivity();
        var remaining = _timeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Resets the timeout tracker to the current time.
    /// </summary>
    public void Reset()
    {
        RecordActivity();
    }
}
