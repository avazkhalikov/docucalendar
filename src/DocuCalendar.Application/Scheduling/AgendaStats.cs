namespace DocuCalendar.Application.Scheduling;

/// <summary>
/// Per-calendar counts for the owner's agenda. An account is several diaries — the owner's, each
/// operator's, a desk's — and a single tenant-wide total hides which of them the agent is
/// actually filling. Pure math over already-loaded rows, so the windowing rules live where a test
/// can reach them; the UTC-parameter lesson of the agenda endpoint is not one to relearn.
/// </summary>
public static class AgendaStats
{
    /// <summary>One appointment, reduced to what counting needs.</summary>
    public sealed record Row(Guid CalendarId, DateTimeOffset StartsAt, string Channel);

    /// <summary>
    /// The agenda's windows, half-open [from, to). MonthFrom..DayTo is "this month so far" —
    /// month-booked deliberately stops at the end of today, matching the account-wide figure.
    /// </summary>
    public sealed record Windows(
        DateTimeOffset DayFrom, DateTimeOffset DayTo,
        DateTimeOffset WeekFrom, DateTimeOffset WeekTo,
        DateTimeOffset MonthFrom, DateTimeOffset PrevMonthFrom);

    /// <summary>Booked figures count only what the agent took: any channel that is not "manual".</summary>
    public sealed record Line(Guid CalendarId, int Today, int Week, int MonthBooked, int PrevMonthBooked);

    public static IReadOnlyDictionary<Guid, Line> PerCalendar(IEnumerable<Row> window, Windows w)
    {
        var lines = new Dictionary<Guid, Line>();
        foreach (var g in window.GroupBy(r => r.CalendarId))
        {
            var rows = g.ToList();
            lines[g.Key] = new Line(
                g.Key,
                rows.Count(r => r.StartsAt >= w.DayFrom && r.StartsAt < w.DayTo),
                rows.Count(r => r.StartsAt >= w.WeekFrom && r.StartsAt < w.WeekTo),
                rows.Count(r => r.StartsAt >= w.MonthFrom && r.StartsAt < w.DayTo && r.Channel != "manual"),
                rows.Count(r => r.StartsAt >= w.PrevMonthFrom && r.StartsAt < w.MonthFrom && r.Channel != "manual"));
        }
        return lines;
    }
}
