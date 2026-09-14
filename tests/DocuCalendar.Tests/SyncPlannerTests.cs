using DocuCalendar.Application.Sync;

namespace DocuCalendar.Tests;

/// <summary>
/// The rules that can cancel a person's appointment, exercised without a network in the way.
/// Every case is one sentence of the plan: "mirrored", "not mirrored", "moved", "cancelled".
/// </summary>
public class SyncPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 9, 11, hour, minute, 0, TimeSpan.Zero);

    private static RemoteEvent Remote(string id, int startHour, int endHour, string? subject = "Meeting", bool busy = true, bool cancelled = false, bool allDay = false) =>
        new(id, subject, At(startHour), At(endHour), allDay, busy, cancelled);

    [Fact]
    public void ARemoteBusyEventBecomesAMirroredBlock()
    {
        var plan = SyncPlanner.Plan(new[] { Remote("ev1", 9, 10, "Dentist") }, Array.Empty<MirroredBlock>(), Array.Empty<LinkedAppointment>(), Now);

        var added = Assert.Single(plan.BlocksToAdd);
        Assert.Equal("ev1", added.Id);
        Assert.Equal("Dentist", added.Subject);
        Assert.Empty(plan.BlocksToRemove);
        Assert.Empty(plan.AppointmentsToCancel);
    }

    [Fact]
    public void AnEventMarkedFreeDoesNotOccupyTheCalendar()
    {
        var plan = SyncPlanner.Plan(new[] { Remote("ev1", 9, 10, busy: false) }, Array.Empty<MirroredBlock>(), Array.Empty<LinkedAppointment>(), Now);

        Assert.Empty(plan.BlocksToAdd);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void AnEventThatBecameFreeReleasesItsBlock()
    {
        var existing = new[] { new MirroredBlock(Guid.NewGuid(), "ev1", At(9), At(10), "Meeting") };
        var plan = SyncPlanner.Plan(new[] { Remote("ev1", 9, 10, busy: false) }, existing, Array.Empty<LinkedAppointment>(), Now);

        Assert.Equal(existing[0].Id, Assert.Single(plan.BlocksToRemove));
        Assert.Empty(plan.BlocksToAdd);
    }

    [Fact]
    public void OurOwnPushedEventIsNeverMirrored()
    {
        // The appointment already occupies 9–10; a block on top would count it twice.
        var linked = new[] { new LinkedAppointment(Guid.NewGuid(), "ours", At(9), At(10), Now.AddHours(-1)) };
        var plan = SyncPlanner.Plan(new[] { Remote("ours", 9, 10, "Appointment: Aziz") }, Array.Empty<MirroredBlock>(), linked, Now);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void MovingOurEventOverThereMovesTheAppointmentHere()
    {
        var id = Guid.NewGuid();
        var linked = new[] { new LinkedAppointment(id, "ours", At(9), At(10), Now.AddHours(-1)) };
        var plan = SyncPlanner.Plan(new[] { Remote("ours", 14, 15) }, Array.Empty<MirroredBlock>(), linked, Now);

        var move = Assert.Single(plan.AppointmentsToMove);
        Assert.Equal(id, move.AppointmentId);
        Assert.Equal(At(14), move.NewStartUtc);
        Assert.Equal(At(15), move.NewEndUtc);
        Assert.Empty(plan.AppointmentsToCancel);
        Assert.Empty(plan.BlocksToAdd);
    }

    [Fact]
    public void CancellingOurEventOverThereCancelsTheAppointmentHere()
    {
        var id = Guid.NewGuid();
        var linked = new[] { new LinkedAppointment(id, "ours", At(9), At(10), Now.AddHours(-1)) };
        var plan = SyncPlanner.Plan(new[] { Remote("ours", 9, 10, cancelled: true) }, Array.Empty<MirroredBlock>(), linked, Now);

        Assert.Equal(id, Assert.Single(plan.AppointmentsToCancel));
    }

    [Fact]
    public void OurEventVanishingOverThereCancelsTheAppointmentHere()
    {
        var id = Guid.NewGuid();
        var linked = new[] { new LinkedAppointment(id, "ours", At(9), At(10), Now.AddHours(-1)) };
        var plan = SyncPlanner.Plan(Array.Empty<RemoteEvent>(), Array.Empty<MirroredBlock>(), linked, Now);

        Assert.Equal(id, Assert.Single(plan.AppointmentsToCancel));
    }

    [Fact]
    public void AnEventPushedMomentsAgoIsNotPresumedDeleted()
    {
        // Providers are not instantly consistent; a copy created a minute ago may not list yet.
        var linked = new[] { new LinkedAppointment(Guid.NewGuid(), "ours", At(9), At(10), Now.AddMinutes(-1)) };
        var plan = SyncPlanner.Plan(Array.Empty<RemoteEvent>(), Array.Empty<MirroredBlock>(), linked, Now);

        Assert.Empty(plan.AppointmentsToCancel);
    }

    [Fact]
    public void AMirroredBlockWhoseEventIsGoneIsRemoved()
    {
        var existing = new[] { new MirroredBlock(Guid.NewGuid(), "ev-old", At(9), At(10), "Old") };
        var plan = SyncPlanner.Plan(new[] { Remote("ev-new", 11, 12) }, existing, Array.Empty<LinkedAppointment>(), Now);

        Assert.Equal(existing[0].Id, Assert.Single(plan.BlocksToRemove));
        Assert.Equal("ev-new", Assert.Single(plan.BlocksToAdd).Id);
    }

    [Fact]
    public void AMirroredBlockThatMovedOrWasRenamedIsUpdatedNotDuplicated()
    {
        var existing = new[] { new MirroredBlock(Guid.NewGuid(), "ev1", At(9), At(10), "Meeting") };
        var plan = SyncPlanner.Plan(new[] { Remote("ev1", 9, 11, "Longer meeting") }, existing, Array.Empty<LinkedAppointment>(), Now);

        var update = Assert.Single(plan.BlocksToUpdate);
        Assert.Equal(existing[0].Id, update.BlockId);
        Assert.Equal(At(11), update.Event.EndUtc);
        Assert.Empty(plan.BlocksToAdd);
        Assert.Empty(plan.BlocksToRemove);
    }

    [Fact]
    public void AnUnchangedMirroredBlockIsLeftAlone()
    {
        var existing = new[] { new MirroredBlock(Guid.NewGuid(), "ev1", At(9), At(10), "Meeting") };
        var plan = SyncPlanner.Plan(new[] { Remote("ev1", 9, 10, "Meeting") }, existing, Array.Empty<LinkedAppointment>(), Now);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void ACancelledAppointmentAwaitingRemoteDeletionIsNotMirrored()
    {
        // Cancelled here a moment ago; the pull runs before the remote copy is deleted, so the
        // listing still returns it. It is not a live appointment (not in "linked"), but it IS
        // ours — mirroring it would block the slot the cancellation just freed.
        var plan = SyncPlanner.Plan(
            new[] { Remote("ours-cancelled", 9, 10, "Appointment: Aziz") },
            Array.Empty<MirroredBlock>(),
            Array.Empty<LinkedAppointment>(),
            Now,
            ownEventIds: new[] { "ours-cancelled" });

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void APhantomBlockOfOurOwnEventIsRemoved()
    {
        var phantom = new[] { new MirroredBlock(Guid.NewGuid(), "ours-cancelled", At(9), At(10), "Appointment: Aziz") };
        var plan = SyncPlanner.Plan(
            new[] { Remote("ours-cancelled", 9, 10, "Appointment: Aziz") },
            phantom,
            Array.Empty<LinkedAppointment>(),
            Now,
            ownEventIds: new[] { "ours-cancelled" });

        Assert.Equal(phantom[0].Id, Assert.Single(plan.BlocksToRemove));
        Assert.Empty(plan.BlocksToAdd);
    }

    [Fact]
    public void ACancelledRemoteEventNeverBecomesABlock()
    {
        var plan = SyncPlanner.Plan(new[] { Remote("ev1", 9, 10, cancelled: true) }, Array.Empty<MirroredBlock>(), Array.Empty<LinkedAppointment>(), Now);

        Assert.Empty(plan.BlocksToAdd);
    }
}
