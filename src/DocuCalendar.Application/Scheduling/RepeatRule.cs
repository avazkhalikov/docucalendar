namespace DocuCalendar.Application.Scheduling;

/// <summary>
/// "Every Friday, 14:00–17:00, until the end of term" — turned into the actual days it means.
///
/// The expansion happens once, when the entry is created, and each occurrence is stored as its
/// own row. Keeping a rule and interpreting it at read time would mean teaching the slot engine,
/// both grids and the Outlook push what recurrence is; expanding it here means they carry on
/// seeing ordinary blocks, and a single occurrence can be cancelled without arguing with a rule.
/// </summary>
public static class RepeatRule
{
    /// <summary>A year of Fridays is 52 rows; the cap is what stops a typo writing thousands.</summary>
    public const int MaxOccurrences = 200;

    /// <summary>How far ahead a repeat may be set, whatever date is asked for.</summary>
    public const int MaxDaysAhead = 366;

    /// <summary>
    /// The local dates a weekly repeat covers: every chosen weekday from <paramref name="from"/>
    /// through <paramref name="until"/> inclusive.
    ///
    /// <paramref name="from"/> is included when its weekday is chosen — somebody setting up
    /// "every Friday" on a Friday means this Friday too, and being told the first one starts next
    /// week would be a small daily annoyance.
    /// </summary>
    public static IReadOnlyList<DateOnly> Weekly(DateOnly from, DateOnly until, IReadOnlyCollection<DayOfWeek> weekdays)
    {
        var days = new List<DateOnly>();
        if (weekdays.Count == 0 || until < from) return days;

        var last = from.AddDays(MaxDaysAhead);
        if (until > last) until = last;

        for (var day = from; day <= until && days.Count < MaxOccurrences; day = day.AddDays(1))
            if (weekdays.Contains(day.DayOfWeek))
                days.Add(day);

        return days;
    }

    /// <summary>
    /// Reads the weekdays a request names. Accepts numbers (0 = Sunday, as JavaScript's
    /// getDay() gives them) so the page can send what it already has.
    /// </summary>
    public static IReadOnlyList<DayOfWeek> WeekdaysFrom(IEnumerable<int>? numbers)
    {
        if (numbers == null) return Array.Empty<DayOfWeek>();
        return numbers
            .Where(n => n is >= 0 and <= 6)
            .Select(n => (DayOfWeek)n)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
    }
}
