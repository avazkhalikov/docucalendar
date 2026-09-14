namespace DocuCalendar.Application.Sync;

/// <summary>A busy block this service already holds for the remote calendar, inside the window.</summary>
public sealed record MirroredBlock(Guid Id, string ExternalId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string? Reason);

/// <summary>An appointment booked here that already has a copy in the remote calendar.</summary>
public sealed record LinkedAppointment(
    Guid Id, string ExternalEventId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, DateTimeOffset? SyncedAtUtc);

public sealed record BlockUpdate(Guid BlockId, RemoteEvent Event);
public sealed record AppointmentMove(Guid AppointmentId, DateTimeOffset NewStartUtc, DateTimeOffset NewEndUtc);

public sealed record SyncPlan(
    IReadOnlyList<RemoteEvent> BlocksToAdd,
    IReadOnlyList<BlockUpdate> BlocksToUpdate,
    IReadOnlyList<Guid> BlocksToRemove,
    IReadOnlyList<AppointmentMove> AppointmentsToMove,
    IReadOnlyList<Guid> AppointmentsToCancel)
{
    public bool IsEmpty =>
        BlocksToAdd.Count == 0 && BlocksToUpdate.Count == 0 && BlocksToRemove.Count == 0
        && AppointmentsToMove.Count == 0 && AppointmentsToCancel.Count == 0;
}

/// <summary>
/// Decides what one sync run changes, from what the remote calendar says and what this service
/// already holds. Pure on purpose: the rules that can cancel somebody's appointment deserve to be
/// tested without a network in the way.
/// </summary>
public static class SyncPlanner
{
    /// <summary>
    /// A remote copy we created moments ago may not show up in the very next listing — providers
    /// are not instantly consistent. Inside this grace period, "not in the list" is not "deleted".
    /// </summary>
    public static readonly TimeSpan PushGrace = TimeSpan.FromMinutes(10);

    /// <param name="ownEventIds">
    /// Remote ids of every event this service ever created there, whatever the appointment's
    /// status now. A cancelled appointment whose remote copy has not been deleted yet is still
    /// ours — mirroring it as somebody else's busy time would block the very slot the
    /// cancellation just freed, until the copy is gone and the next run notices.
    /// </param>
    public static SyncPlan Plan(
        IReadOnlyList<RemoteEvent> remote,
        IReadOnlyList<MirroredBlock> existing,
        IReadOnlyList<LinkedAppointment> linked,
        DateTimeOffset nowUtc,
        IReadOnlyCollection<string>? ownEventIds = null)
    {
        var add = new List<RemoteEvent>();
        var update = new List<BlockUpdate>();
        var remove = new List<Guid>();
        var move = new List<AppointmentMove>();
        var cancel = new List<Guid>();

        var linkedById = new Dictionary<string, LinkedAppointment>(StringComparer.Ordinal);
        foreach (var a in linked) linkedById[a.ExternalEventId] = a;

        var existingById = new Dictionary<string, MirroredBlock>(StringComparer.Ordinal);
        foreach (var b in existing) existingById[b.ExternalId] = b;

        var own = ownEventIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(ownEventIds, StringComparer.Ordinal);

        var seenBlocks = new HashSet<string>(StringComparer.Ordinal);
        var seenLinked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in remote)
        {
            if (linkedById.TryGetValue(e.Id, out var ours))
            {
                // One of ours. It is never mirrored as busy — the appointment already occupies the
                // time, and a block on top would count it twice.
                seenLinked.Add(e.Id);
                if (e.IsCancelled)
                    cancel.Add(ours.Id);
                else if (e.StartUtc != ours.StartUtc || e.EndUtc != ours.EndUtc)
                    move.Add(new AppointmentMove(ours.Id, e.StartUtc, e.EndUtc));
                continue;
            }

            if (own.Contains(e.Id))
            {
                // Ours too, but no longer a live appointment (cancelled here, remote copy still
                // to be deleted). Not busy time — and if an earlier run mirrored it, undo that.
                if (existingById.TryGetValue(e.Id, out var phantom))
                {
                    seenBlocks.Add(e.Id);
                    remove.Add(phantom.Id);
                }
                continue;
            }

            var occupies = e.IsBusy && !e.IsCancelled && e.EndUtc > e.StartUtc;
            if (!occupies)
            {
                // Marked free, declined, or cancelled: if we were mirroring it, stop.
                if (existingById.TryGetValue(e.Id, out var stale))
                {
                    seenBlocks.Add(e.Id);
                    remove.Add(stale.Id);
                }
                continue;
            }

            seenBlocks.Add(e.Id);
            if (existingById.TryGetValue(e.Id, out var block))
            {
                if (block.StartUtc != e.StartUtc || block.EndUtc != e.EndUtc || !string.Equals(block.Reason, e.Subject, StringComparison.Ordinal))
                    update.Add(new BlockUpdate(block.Id, e));
            }
            else
            {
                add.Add(e);
            }
        }

        // Gone from the remote calendar: gone from here.
        foreach (var b in existing)
            if (!seenBlocks.Contains(b.ExternalId))
                remove.Add(b.Id);

        // Our appointment vanished over there: the person deleted it in their own calendar, and
        // their calendar is the one they look at. Follow them — unless we only just created the
        // copy and the provider has not caught up yet.
        foreach (var a in linked)
        {
            if (seenLinked.Contains(a.ExternalEventId)) continue;
            if (a.SyncedAtUtc is { } synced && nowUtc - synced < PushGrace) continue;
            cancel.Add(a.Id);
        }

        return new SyncPlan(add, update, remove.Distinct().ToList(), move, cancel.Distinct().ToList());
    }
}
