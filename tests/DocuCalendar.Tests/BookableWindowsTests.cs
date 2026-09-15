using DocuCalendar.Application.Scheduling;
using DocuCalendar.Application.Sync;

namespace DocuCalendar.Tests;

/// <summary>
/// The word a person writes in Outlook or Google to say "book me here", and what the sync does
/// with it. The rule is deliberately narrow — the word alone — because a wider match would turn
/// "not bookable today" into an invitation.
/// </summary>
public class BookableWindowsTests
{
    [Theory]
    [InlineData("Bookable")]
    [InlineData("bookable")]
    [InlineData("BOOKABLE")]
    [InlineData("  Bookable  ")]
    public void TheWordAloneInAnyCapitalisationIsAWindow(string subject)
    {
        Assert.True(BookableWindows.IsBookable(subject));
    }

    [Theory]
    [InlineData("Bookable 9-11")]
    [InlineData("not bookable")]
    [InlineData("Bookable?")]
    [InlineData("Meeting")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsAnOrdinaryEvent(string? subject)
    {
        Assert.False(BookableWindows.IsBookable(subject));
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int hour) => new(2026, 9, 11, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AFreeEventNamedBookableIsStillMirrored_SoTheEngineCanReadTheWindow()
    {
        // People forget "Show as: Free" — and when they remember, a free event must not vanish
        // from the mirror, or the window it marks is never seen. Both settings arrive.
        var free = new RemoteEvent("w1", "Bookable", At(10), At(11), false, IsBusy: false, IsCancelled: false);
        var busy = new RemoteEvent("w2", "bookable", At(16), At(18), false, IsBusy: true, IsCancelled: false);
        var lunch = new RemoteEvent("f1", "Lunch", At(13), At(14), false, IsBusy: false, IsCancelled: false);

        var plan = SyncPlanner.Plan(new[] { free, busy, lunch }, Array.Empty<MirroredBlock>(), Array.Empty<LinkedAppointment>(), Now);

        Assert.Equal(new[] { "w1", "w2" }, plan.BlocksToAdd.Select(e => e.Id).OrderBy(x => x));
    }

    // ---- two words: public and staff ----

    [Theory]
    [InlineData("Bookable staff", BookableKind.Staff)]
    [InlineData("bookable   STAFF", BookableKind.Staff)]
    [InlineData("Bookable", BookableKind.Public)]
    public void TheTwoWordsTellPublicFromStaff(string subject, BookableKind expected)
    {
        Assert.Equal(expected, BookableWindows.KindOf(subject));
    }

    [Theory]
    [InlineData("Bookable stuff")]
    [InlineData("Staff bookable")]
    [InlineData("Bookable staff meeting")]
    public void NearMissesAreOrdinaryEvents(string subject)
    {
        Assert.Null(BookableWindows.KindOf(subject));
    }

    [Theory]
    [InlineData("+998 90 123-45-67", "998901234567", true)]
    [InlineData("+998901234567", "90 123 45 67", true)]
    [InlineData("0090 123 45 67", "998901234567", true)] // an international "00" prefix is dropped; the nine local digits are the tail
    [InlineData("+998 71 200 00 00", "998901234567", false)]
    [InlineData("+998901234567", "+998901234568", false)]
    [InlineData("1234", "998901231234", false)] // an extension is not a phone number
    [InlineData("", "998901234567", false)]
    public void NumbersMatchWithOrWithoutTheCountryCode(string a, string b, bool same)
    {
        Assert.Equal(same, BookableWindows.SamePhone(a, b));
    }

    [Fact]
    public void StaffHoursAreVisibleOnlyToAStaffCaller()
    {
        var windows = new[]
        {
            new BookableWindow(new Interval(At(10), At(11)), BookableKind.Public),
            new BookableWindow(new Interval(At(16), At(18)), BookableKind.Staff),
        };
        var staff = new[] { "+998 90 123 45 67" };

        Assert.True(BookableWindows.IsStaffCaller("998901234567", staff));
        Assert.False(BookableWindows.IsStaffCaller("998901234568", staff));
        Assert.False(BookableWindows.IsStaffCaller(null, staff));

        Assert.Single(BookableWindows.Visible(windows, isStaffCaller: false));
        Assert.Equal(2, BookableWindows.Visible(windows, isStaffCaller: true).Count);
    }

    [Fact]
    public void AStaffEventIsMirroredLikeAPublicOne()
    {
        var staff = new RemoteEvent("s1", "Bookable staff", At(16), At(18), false, IsBusy: false, IsCancelled: false);

        var plan = SyncPlanner.Plan(new[] { staff }, Array.Empty<MirroredBlock>(), Array.Empty<LinkedAppointment>(), Now);

        Assert.Single(plan.BlocksToAdd);
    }
}
