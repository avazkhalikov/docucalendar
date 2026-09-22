using DocuCalendar.Application.Scheduling;

namespace DocuCalendar.Tests;

/// <summary>
/// "Every Friday, 14:00–17:00, until the end of term" — turned into the days it means.
///
/// The expansion is done once, on creation, and each day becomes its own row. That is what lets
/// the slot engine, the grids and the Outlook push carry on seeing ordinary blocks, and lets one
/// Friday be cancelled without arguing with a rule. The caps exist because a typo in the end date
/// is otherwise a few thousand rows.
/// </summary>
public class RepeatRuleTests
{
    private static readonly DateOnly Friday = new(2026, 9, 25);   // a Friday
    private static readonly DayOfWeek[] Fridays = { DayOfWeek.Friday };

    [Fact]
    public void EveryFridayForAMonthIsFourOrFiveFridays()
    {
        var days = RepeatRule.Weekly(Friday, new DateOnly(2026, 10, 23), Fridays);

        Assert.Equal(5, days.Count);
        Assert.All(days, d => Assert.Equal(DayOfWeek.Friday, d.DayOfWeek));
        Assert.Equal(Friday, days[0]);
        Assert.Equal(new DateOnly(2026, 10, 23), days[^1]);
    }

    [Fact]
    public void TheStartingDayCountsWhenItIsOneOfTheChosenWeekdays()
    {
        // Somebody setting up "every Friday" ON a Friday means this Friday too; being told it
        // starts next week would be a small daily annoyance.
        var days = RepeatRule.Weekly(Friday, Friday, Fridays);

        Assert.Single(days);
        Assert.Equal(Friday, days[0]);
    }

    [Fact]
    public void SeveralWeekdaysAreAllExpanded()
    {
        var monday = new DateOnly(2026, 9, 21);
        var days = RepeatRule.Weekly(monday, monday.AddDays(13),
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday });

        Assert.Equal(4, days.Count);   // two Mondays, two Wednesdays
        Assert.Equal(new[] { 21, 23, 28, 30 }, days.Select(d => d.Day).ToArray());
    }

    [Fact]
    public void AnEndBeforeTheStartMeansNothing()
    {
        Assert.Empty(RepeatRule.Weekly(Friday, Friday.AddDays(-1), Fridays));
    }

    [Fact]
    public void NoWeekdaysMeansNothing()
    {
        Assert.Empty(RepeatRule.Weekly(Friday, Friday.AddDays(60), Array.Empty<DayOfWeek>()));
    }

    [Fact]
    public void ARunawayEndDateIsCappedRatherThanObeyed()
    {
        // "until 2099" must not write ten thousand rows.
        var days = RepeatRule.Weekly(Friday, new DateOnly(2099, 1, 1), Fridays);

        Assert.True(days.Count <= RepeatRule.MaxOccurrences);
        Assert.True(days[^1] <= Friday.AddDays(RepeatRule.MaxDaysAhead));
    }

    [Fact]
    public void EveryDayOfTheWeekIsStillCappedAtTwoHundred()
    {
        var all = Enum.GetValues<DayOfWeek>();
        var days = RepeatRule.Weekly(Friday, new DateOnly(2030, 1, 1), all);

        Assert.Equal(RepeatRule.MaxOccurrences, days.Count);
    }

    [Fact]
    public void WeekdayNumbersFollowJavaScriptWithSundayZero()
    {
        // The page sends what Date.getDay() gives it; a mismatch here moves somebody's hours.
        Assert.Equal(new[] { DayOfWeek.Sunday }, RepeatRule.WeekdaysFrom(new[] { 0 }));
        Assert.Equal(new[] { DayOfWeek.Friday }, RepeatRule.WeekdaysFrom(new[] { 5 }));
        Assert.Equal(new[] { DayOfWeek.Saturday }, RepeatRule.WeekdaysFrom(new[] { 6 }));
    }

    [Fact]
    public void RubbishWeekdayNumbersAreIgnoredNotGuessed()
    {
        Assert.Empty(RepeatRule.WeekdaysFrom(new[] { -1, 7, 99 }));
        Assert.Equal(new[] { DayOfWeek.Monday }, RepeatRule.WeekdaysFrom(new[] { 1, 1, 42 }));
        Assert.Empty(RepeatRule.WeekdaysFrom(null));
    }
}
