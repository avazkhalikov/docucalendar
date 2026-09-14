using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Services;

namespace DocuCalendar.Tests;

/// <summary>
/// Matching what a caller SAYS to whose calendar they meant. The bias is deliberate: a wrong match
/// books a stranger into somebody else's day, so anything less than obvious returns nothing and
/// lets the context default decide.
/// </summary>
public class CalendarMatchingTests
{
    // Each a different person: two calendars of ONE person are never an ambiguity (see
    // DefaultCalendarTests), and the roster here is about telling people apart.
    private static StaffCalendar Cal(string label) => new() { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Label = label };

    private static readonly List<StaffCalendar> Roster = new()
    {
        Cal("Aziza — Admissions"),
        Cal("Reception"),
        Cal("Dr Karimov (Finance)"),
    };

    [Fact]
    public void AnExactNameMatches()
    {
        Assert.Equal("Reception", CalendarService.MatchByLabel(Roster, "reception")?.Label);
    }

    [Fact]
    public void PunctuationAndCaseDoNotMatter()
    {
        Assert.Equal("Aziza — Admissions", CalendarService.MatchByLabel(Roster, "AZIZA - ADMISSIONS")?.Label);
    }

    [Fact]
    public void ADepartmentInsideALabelIsFound()
    {
        Assert.Equal("Aziza — Admissions", CalendarService.MatchByLabel(Roster, "admissions")?.Label);
        Assert.Equal("Dr Karimov (Finance)", CalendarService.MatchByLabel(Roster, "finance")?.Label);
    }

    [Fact]
    public void AWholePhraseStillFindsTheOnePersonItCanMean()
    {
        Assert.Equal("Aziza — Admissions", CalendarService.MatchByLabel(Roster, "the admissions officer")?.Label);
    }

    [Fact]
    public void SomethingNobodyIsCalled_MatchesNothing()
    {
        Assert.Null(CalendarService.MatchByLabel(Roster, "the swimming pool"));
    }

    [Fact]
    public void AnAmbiguousWordMatchesNothing_RatherThanGuessing()
    {
        var twoAdmissions = new List<StaffCalendar> { Cal("Aziza — Admissions"), Cal("Bobur — Admissions") };

        // Two people could be meant. Guessing would book a stranger into one of their days.
        Assert.Null(CalendarService.MatchByLabel(twoAdmissions, "admissions"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a")]
    [InlineData("hi")]
    public void TooLittleToGoOn_MatchesNothing(string hint)
    {
        Assert.Null(CalendarService.MatchByLabel(Roster, hint));
    }
}
