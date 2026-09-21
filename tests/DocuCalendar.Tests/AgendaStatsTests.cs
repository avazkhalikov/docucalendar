using DocuCalendar.Application.Scheduling;

namespace DocuCalendar.Tests;

/// <summary>
/// The per-calendar agenda figures. The windows here mirror the agenda endpoint's: half-open,
/// month-booked running only to the end of today, booked meaning any channel but "manual".
/// A miscount does not throw — it tells the owner the wrong person's week is full.
/// </summary>
public class AgendaStatsTests
{
    private static readonly Guid CalA = Guid.NewGuid();
    private static readonly Guid CalB = Guid.NewGuid();

    // "Today" is a Monday so the week starts on the day itself and the arithmetic stays legible.
    private static readonly DateTimeOffset Day = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    private static AgendaStats.Windows Windows => new(
        DayFrom: Day, DayTo: Day.AddDays(1),
        WeekFrom: Day, WeekTo: Day.AddDays(7),
        MonthFrom: Day.AddDays(-20), PrevMonthFrom: Day.AddDays(-50));

    private static AgendaStats.Row Row(Guid cal, double daysFromToday, string channel = "phone") =>
        new(cal, Day.AddDays(daysFromToday).AddHours(9), channel);

    [Fact]
    public void Counts_split_by_calendar_and_window()
    {
        var lines = AgendaStats.PerCalendar(new[]
        {
            Row(CalA, 0),                    // A: today, by agent
            Row(CalA, 0, "manual"),          // A: today, walked in
            Row(CalA, 2),                    // A: later this week
            Row(CalB, -10),                  // B: earlier this month
            Row(CalB, -30),                  // B: previous month
        }, Windows);

        var a = lines[CalA];
        Assert.Equal(2, a.Today);
        Assert.Equal(3, a.Week);
        Assert.Equal(1, a.MonthBooked);      // only today's agent booking — the +2d one is beyond DayTo
        Assert.Equal(0, a.PrevMonthBooked);

        var b = lines[CalB];
        Assert.Equal(0, b.Today);
        Assert.Equal(0, b.Week);
        Assert.Equal(1, b.MonthBooked);
        Assert.Equal(1, b.PrevMonthBooked);
    }

    [Fact]
    public void Manual_bookings_never_count_as_booked()
    {
        var lines = AgendaStats.PerCalendar(new[]
        {
            Row(CalA, -5, "manual"),
            Row(CalA, -35, "manual"),
        }, Windows);

        Assert.Equal(0, lines[CalA].MonthBooked);
        Assert.Equal(0, lines[CalA].PrevMonthBooked);
    }

    [Fact]
    public void Every_agent_channel_counts_as_booked()
    {
        var lines = AgendaStats.PerCalendar(new[]
        {
            Row(CalA, 0, "phone"),
            Row(CalA, 0, "telegram"),
            Row(CalA, 0, "web"),
        }, Windows);

        Assert.Equal(3, lines[CalA].MonthBooked);
    }

    [Fact]
    public void A_calendar_with_no_appointments_has_no_line()
    {
        var lines = AgendaStats.PerCalendar(new[] { Row(CalA, 0) }, Windows);

        Assert.False(lines.ContainsKey(CalB));
        Assert.Empty(AgendaStats.PerCalendar(Array.Empty<AgendaStats.Row>(), Windows));
    }

    [Fact]
    public void Window_edges_are_half_open()
    {
        var lines = AgendaStats.PerCalendar(new[]
        {
            new AgendaStats.Row(CalA, Day, "phone"),               // first instant of today: in
            new AgendaStats.Row(CalA, Day.AddDays(1), "phone"),    // first instant of tomorrow: out of today, in week
            new AgendaStats.Row(CalA, Day.AddDays(7), "phone"),    // first instant past the week: out
        }, Windows);

        Assert.Equal(1, lines[CalA].Today);
        Assert.Equal(2, lines[CalA].Week);
    }
}
