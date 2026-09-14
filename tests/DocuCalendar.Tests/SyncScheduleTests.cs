using DocuCalendar.Application.Sync;

namespace DocuCalendar.Tests;

/// <summary>The one-minute tick asks this for every connection; it had better say yes at the
/// right times and no at the others.</summary>
public class SyncScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANeverSyncedConnectionIsDueAtOnce()
    {
        Assert.True(SyncSchedule.IsDue("connected", 5, null, Now));
    }

    [Fact]
    public void ManualOnlyIsNeverDue()
    {
        Assert.False(SyncSchedule.IsDue("connected", 0, null, Now));
        Assert.False(SyncSchedule.IsDue("connected", 0, Now.AddHours(-3), Now));
    }

    [Fact]
    public void WaitingForAPersonToReconnectIsNotSomethingToRetry()
    {
        Assert.False(SyncSchedule.IsDue("reconnect", 5, Now.AddHours(-1), Now));
    }

    [Fact]
    public void DueWhenTheIntervalHasPassed_WithTickSlack()
    {
        // Ran 4 min 40 s ago on a 5-minute interval: the tolerance means this tick takes it, rather
        // than the one after — otherwise "every 5 minutes" would mean every 6.
        Assert.True(SyncSchedule.IsDue("connected", 5, Now.AddSeconds(-280), Now));
        Assert.False(SyncSchedule.IsDue("connected", 5, Now.AddSeconds(-200), Now));
        Assert.True(SyncSchedule.IsDue("error", 10, Now.AddMinutes(-10), Now)); // a passing error is retried on schedule
    }

    [Fact]
    public void OnlyTheOfferedIntervalsAreAllowed()
    {
        Assert.True(SyncSchedule.IsAllowed(0));
        Assert.True(SyncSchedule.IsAllowed(10));
        Assert.False(SyncSchedule.IsAllowed(7));
        Assert.False(SyncSchedule.IsAllowed(-5));
    }
}
