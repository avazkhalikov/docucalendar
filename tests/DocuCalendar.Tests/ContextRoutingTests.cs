using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Services;

namespace DocuCalendar.Tests;

/// <summary>
/// Who the assistant may book on one knowledge base.
///
/// Written after an account with three connected calendars was asked on the phone who could be
/// seen and answered with one name. The old arrangement allowed exactly one calendar per context
/// plus an account-wide fallback, so "whoever the owner picked" was the only possible answer.
/// A context now has as many calendars as it needs, and when several serve it the assistant is
/// told to ask the caller rather than choose somebody's day for them.
/// </summary>
public class ContextRoutingTests
{
    private static StaffCalendar Cal(string label) => new() { Label = label, OwnerUserId = Guid.NewGuid() };

    private static readonly StaffCalendar Avaz = Cal("Avaz Outlook");
    private static readonly StaffCalendar Gozal = Cal("Gozal - Call Center");
    private static readonly StaffCalendar Admissions = Cal("Aziza — Admissions");

    [Fact]
    public void NobodyServesTheContextSoNothingIsOffered()
    {
        var resolution = CalendarService.Decide(Array.Empty<StaffCalendar>(), null);

        Assert.Null(resolution.Calendar);
        Assert.False(resolution.UnknownStaff);
        Assert.False(resolution.MustChoose);
        Assert.Empty(resolution.BookableLabels);
    }

    [Fact]
    public void OnePersonServesItSoNobodyIsAsked()
    {
        var resolution = CalendarService.Decide(new[] { Avaz }, null);

        Assert.Same(Avaz, resolution.Calendar);
        Assert.False(resolution.MustChoose);
    }

    [Fact]
    public void SeveralServeItAndTheCallerNamedNobodySoTheAssistantAsks()
    {
        var resolution = CalendarService.Decide(new[] { Avaz, Gozal, Admissions }, null);

        // The heart of it: no diary is chosen on the caller's behalf.
        Assert.Null(resolution.Calendar);
        Assert.True(resolution.MustChoose);
        Assert.False(resolution.UnknownStaff);
        Assert.Equal(3, resolution.BookableLabels.Count);
        Assert.Contains("Gozal - Call Center", resolution.BookableLabels);
    }

    [Fact]
    public void ANamedPersonStillWinsOverAsking()
    {
        var resolution = CalendarService.Decide(new[] { Avaz, Gozal, Admissions }, "call center");

        Assert.Same(Gozal, resolution.Calendar);
        Assert.False(resolution.MustChoose);
    }

    [Fact]
    public void SomebodyWhoDoesNotServeThisContextIsRefusedWithWhoDoes()
    {
        // The caller asks for a colleague who takes appointments on another line entirely.
        var resolution = CalendarService.Decide(new[] { Avaz, Gozal }, "Aziza");

        Assert.Null(resolution.Calendar);
        Assert.True(resolution.UnknownStaff);
        Assert.False(resolution.MustChoose);
        // …and is told who CAN be seen here, which is never somebody from another context.
        Assert.Equal(new[] { "Avaz Outlook", "Gozal - Call Center" }, resolution.BookableLabels);
    }

    [Fact]
    public void TheRosterIsAlphabeticalAndOnlyEverThisContexts()
    {
        var resolution = CalendarService.Decide(new[] { Gozal, Admissions, Avaz }, null);

        Assert.Equal(new[] { "Avaz Outlook", "Aziza — Admissions", "Gozal - Call Center" }, resolution.BookableLabels);
    }
}
