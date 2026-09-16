using DocuCalendar.Application.Scheduling;

namespace DocuCalendar.Tests;

/// <summary>
/// A booking is a request only when the calendar asks for it AND the caller can be told the
/// answer; otherwise it is final, as it always was. A request nobody decides on lapses an hour
/// before its time.
/// </summary>
public class PendingRulesTests
{
    [Theory]
    [InlineData(false, false, "confirmed")]
    [InlineData(false, true, "confirmed")]
    [InlineData(true, false, "confirmed")] // wants confirmation, but nobody could tell the caller: book it
    [InlineData(true, true, "pending")]
    public void ARequestNeedsBothTheWishAndAWayToAnswer(bool requiresConfirmation, bool callerCanBeTold, string expected)
    {
        Assert.Equal(expected, PendingRules.InitialStatus(requiresConfirmation, callerCanBeTold));
    }

    [Fact]
    public void AnUndecidedRequestLapsesAnHourBeforeItsTime()
    {
        var start = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        Assert.False(PendingRules.HasLapsed(start, start.AddHours(-2)));
        Assert.True(PendingRules.HasLapsed(start, start.AddMinutes(-59)));
        Assert.True(PendingRules.HasLapsed(start, start.AddHours(1)));
    }

    [Fact]
    public void ARequestHoldsItsSlotWhileItWaits()
    {
        Assert.True(PendingRules.OccupiesSlot("pending"));
        Assert.True(PendingRules.OccupiesSlot("confirmed"));
        Assert.False(PendingRules.OccupiesSlot("declined"));
        Assert.False(PendingRules.OccupiesSlot("expired"));
        Assert.False(PendingRules.OccupiesSlot("cancelled"));
    }
}
