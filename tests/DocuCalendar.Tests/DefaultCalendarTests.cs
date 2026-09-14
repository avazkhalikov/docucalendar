using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Services;

namespace DocuCalendar.Tests;

/// <summary>
/// A person may have several calendars here (a work Outlook, a personal Google). The assistant
/// must land on the one they starred — and must still refuse to guess between different people.
/// </summary>
public class DefaultCalendarTests
{
    private static readonly Guid Avaz = Guid.NewGuid();
    private static readonly Guid Behzod = Guid.NewGuid();

    private static StaffCalendar Cal(string label, Guid owner, bool isDefault = false) =>
        new() { Label = label, OwnerUserId = owner, IsDefault = isDefault };

    [Fact]
    public void TwoCalendarsOfOnePersonResolveToTheirDefault()
    {
        var calendars = new[] { Cal("Avaz", Avaz), Cal("Avaz Outlook", Avaz, isDefault: true) };

        var picked = CalendarService.MatchByLabel(calendars, "Avaz");

        Assert.NotNull(picked);
        Assert.Equal("Avaz Outlook", picked!.Label);
    }

    [Fact]
    public void WithoutAStarThePersonsFirstCalendarIsUsed()
    {
        var calendars = new[] { Cal("Avaz", Avaz), Cal("Avaz Outlook", Avaz) };

        Assert.Equal("Avaz", CalendarService.MatchByLabel(calendars, "Avaz")!.Label);
    }

    [Fact]
    public void CandidatesForDifferentPeopleAreStillAnAmbiguity()
    {
        // "reception" matches both, and they are different people: ask, do not guess.
        var calendars = new[] { Cal("Reception — Avaz", Avaz, isDefault: true), Cal("Reception — Behzod", Behzod, isDefault: true) };

        Assert.Null(CalendarService.MatchByLabel(calendars, "reception"));
    }

    [Fact]
    public void AnExactLabelForTheNonDefaultStillGoesToTheDefault()
    {
        // The star is the diary; the other row is the same person seen through another provider.
        var calendars = new[] { Cal("Avaz", Avaz, isDefault: true), Cal("Avaz Outlook", Avaz) };

        Assert.Equal("Avaz", CalendarService.MatchByLabel(calendars, "Avaz Outlook")!.Label);
    }

    [Fact]
    public void ASingleMatchIsHonouredAsNamed_EvenWhenItsOwnerStarredAnotherCalendar()
    {
        // The owner's "Admissions" desk is a calendar in its own right, not a second view of
        // the owner's diary — a caller asking for admissions must land there.
        var calendars = new[] { Cal("Avaz", Avaz, isDefault: true), Cal("Admissions", Avaz) };

        Assert.Equal("Admissions", CalendarService.MatchByLabel(calendars, "admissions")!.Label);
    }

    [Fact]
    public void TheFullNameSettlesItBetweenDifferentPeople()
    {
        var calendars = new[] { Cal("Reception", Avaz, isDefault: true), Cal("Reception — Behzod", Behzod, isDefault: true) };

        Assert.Equal("Reception", CalendarService.MatchByLabel(calendars, "reception")!.Label);
    }

    [Fact]
    public void ChooseHandlesTheEdges()
    {
        Assert.Null(CalendarService.Choose(Array.Empty<StaffCalendar>(), "x"));
        var only = Cal("Only", Avaz);
        Assert.Same(only, CalendarService.Choose(new[] { only }, "only"));
    }
}
