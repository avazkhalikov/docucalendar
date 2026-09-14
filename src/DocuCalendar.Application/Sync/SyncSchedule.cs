namespace DocuCalendar.Application.Sync;

/// <summary>
/// When a connection is due for a scheduled run. The worker ticks every minute and asks this
/// for each connection; the answer depends only on what the connection says about itself.
/// </summary>
public static class SyncSchedule
{
    /// <summary>The intervals a person may choose. 0 is "manual only".</summary>
    public static readonly int[] AllowedIntervals = { 0, 5, 10, 15, 30, 60 };

    /// <summary>
    /// A little slack, because the worker's tick and the last run's timestamp never line up
    /// exactly: without it a five-minute interval would routinely wait for the sixth tick.
    /// </summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(30);

    public static bool IsAllowed(int everyMinutes) => Array.IndexOf(AllowedIntervals, everyMinutes) >= 0;

    public static bool IsDue(string status, int everyMinutes, DateTimeOffset? lastSyncAt, DateTimeOffset now)
    {
        if (string.Equals(status, "reconnect", StringComparison.Ordinal)) return false; // needs a person, not a retry
        if (everyMinutes <= 0) return false;
        if (lastSyncAt is null) return true;
        return lastSyncAt.Value + TimeSpan.FromMinutes(everyMinutes) <= now + Tolerance;
    }
}
