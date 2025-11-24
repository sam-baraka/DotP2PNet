using Dotp2pNet.Core.ErrorHandling;
using Xunit;

namespace Dotp2pNet.Tests.Unit.ErrorHandling;

/// <summary>
/// Tests for the TimeoutTracker class.
/// </summary>
public class TimeoutTrackerTests
{
    [Fact]
    public void TimeoutTracker_NewInstance_NotTimedOut()
    {
        // Arrange
        var tracker = new TimeoutTracker(TimeSpan.FromSeconds(1));

        // Act & Assert
        Assert.False(tracker.HasTimedOut());
    }

    [Fact]
    public async Task TimeoutTracker_AfterTimeout_IsTimedOut()
    {
        // Arrange
        var tracker = new TimeoutTracker(TimeSpan.FromMilliseconds(50));

        // Act
        await Task.Delay(100);

        // Assert
        Assert.True(tracker.HasTimedOut());
    }

    [Fact]
    public async Task TimeoutTracker_RecordActivity_ResetsTimeout()
    {
        // Arrange
        var tracker = new TimeoutTracker(TimeSpan.FromMilliseconds(100));

        // Act
        await Task.Delay(60);
        tracker.RecordActivity();
        await Task.Delay(60);

        // Assert
        Assert.False(tracker.HasTimedOut());
    }

    [Fact]
    public void TimeoutTracker_GetTimeSinceLastActivity_ReturnsElapsedTime()
    {
        // Arrange
        var tracker = new TimeoutTracker(TimeSpan.FromSeconds(10));

        // Act
        Thread.Sleep(50);
        var elapsed = tracker.GetTimeSinceLastActivity();

        // Assert
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(40));
        Assert.True(elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TimeoutTracker_GetRemainingTime_ReturnsCorrectValue()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(1);
        var tracker = new TimeoutTracker(timeout);

        // Act
        Thread.Sleep(100);
        var remaining = tracker.GetRemainingTime();

        // Assert
        Assert.True(remaining > TimeSpan.Zero);
        Assert.True(remaining < timeout);
    }

    [Fact]
    public async Task TimeoutTracker_GetRemainingTime_AfterTimeout_ReturnsZero()
    {
        // Arrange
        var tracker = new TimeoutTracker(TimeSpan.FromMilliseconds(50));

        // Act
        await Task.Delay(100);
        var remaining = tracker.GetRemainingTime();

        // Assert
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public async Task TimeoutTracker_Reset_ResetsTimer()
    {
        // Arrange
        var tracker = new TimeoutTracker(TimeSpan.FromMilliseconds(100));

        // Act
        await Task.Delay(60);
        tracker.Reset();
        await Task.Delay(60);

        // Assert
        Assert.False(tracker.HasTimedOut());
    }

    [Fact]
    public void TimeoutTracker_NegativeTimeout_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            new TimeoutTracker(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void TimeoutTracker_LastActivity_ReturnsCorrectTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow;
        var tracker = new TimeoutTracker(TimeSpan.FromSeconds(10));
        var after = DateTime.UtcNow;

        // Act
        var lastActivity = tracker.LastActivity;

        // Assert
        Assert.True(lastActivity >= before);
        Assert.True(lastActivity <= after);
    }
}
