using System.Globalization;
using System.Text.Json;

namespace DocuCalendar.Application.Scheduling;

/// <summary>A span of time that is not free — a busy block or an existing appointment. UTC.</summary>
public sealed record Interval(DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// An offerable appointment time. Carries both the instant (what gets stored and booked) and the
/// LOCAL rendering (what the AI says out loud and a person reads) — because a model handed only a
/// UTC instant will eventually read it to a caller as the wrong hour.
/// </summary>
public sealed record Slot(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string Local);

/// <summary>The calendar's booking rules, lifted off the entity so the engine stays pure.</summary>
public sealed record SchedulingRules(
    int SlotMinutes,
    int MaxMinutes,
    int BufferMinutes,
    int MinLeadMinutes,
    int HorizonDays,
    string WeeklyAvailabilityJson);

/// <summary>
/// Turns a working week, a pile of busy time and a clock into the list of times a visitor may
/// actually be offered.
///
/// Deliberately pure: no database, no HTTP, no ambient clock. Every rule that decides whether a
/// caller hears "twelve o'clock" or "quarter past two" lives in one function that a test can pin
/// down exactly — because the cost of a wrong slot is not a stack trace, it is a person arriving
/// at an office where nobody is expecting them.
///
/// All arithmetic is done on instants; the tenant's timezone is used only where a human meaning is
/// required — deciding which local day a slot belongs to, and rendering the text.
/// </summary>
public static class SlotEngine
{
    /// <summary>Never return an unbounded list to a caller (or a model) — this is a menu, not a dump.</summary>
    public const int DefaultMaxResults = 60;

    private static readonly string[] DayKeys = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    public static IReadOnlyList<Slot> GetSlots(
        SchedulingRules rules,
        TimeZoneInfo zone,
        IEnumerable<Interval> busy,
        DateTimeOffset nowUtc,
        int days,
        int? requestedMinutes = null,
        int maxResults = DefaultMaxResults)
    {
        var duration = ClampDuration(rules, requestedMinutes);
        var step = Math.Max(1, rules.SlotMinutes);
        var horizon = Math.Clamp(days, 1, Math.Max(1, rules.HorizonDays));
        var earliest = nowUtc.AddMinutes(Math.Max(0, rules.MinLeadMinutes));

        var week = ParseWeek(rules.WeeklyAvailabilityJson);
        if (week.Count == 0) return Array.Empty<Slot>();

        // Pad once, up front: a buffer is a property of the blocked time, not of every comparison.
        var blocked = busy
            .Select(b => new Interval(b.Start.AddMinutes(-rules.BufferMinutes), b.End.AddMinutes(rules.BufferMinutes)))
            .Where(b => b.End > b.Start)
            .OrderBy(b => b.Start)
            .ToList();

        var results = new List<Slot>();
        var startLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);

        for (var d = 0; d < horizon && results.Count < maxResults; d++)
        {
            var date = startLocalDate.AddDays(d);
            if (!week.TryGetValue(date.DayOfWeek, out var windows)) continue;

            foreach (var (winStart, winEnd) in windows)
            {
                if (results.Count >= maxResults) break;
                if (winEnd <= winStart) continue; // an overnight window is not expressible here, by design

                if (!TryToUtc(date, winStart, zone, out var windowStartUtc)) continue;
                if (!TryToUtc(date, winEnd, zone, out var windowEndUtc)) continue;

                for (var t = windowStartUtc; t.AddMinutes(duration) <= windowEndUtc; t = t.AddMinutes(step))
                {
                    if (results.Count >= maxResults) break;
                    var end = t.AddMinutes(duration);
                    if (t < earliest) continue;
                    if (OverlapsAny(t, end, blocked)) continue;
                    results.Add(new Slot(t, end, FormatLocal(t, zone)));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Would the engine offer exactly this slot right now? The booking endpoint asks before it
    /// writes, so a caller who invents a time — or replays one that has since been taken — is
    /// refused by the same rules that produced the offer in the first place.
    /// </summary>
    public static bool IsOfferable(
        SchedulingRules rules,
        TimeZoneInfo zone,
        IEnumerable<Interval> busy,
        DateTimeOffset nowUtc,
        DateTimeOffset startUtc,
        int minutes)
    {
        if (ClampDuration(rules, minutes) != minutes) return false;

        // Look only at the day in question, but ask the real engine — one source of truth for
        // "offerable", not a second implementation that can drift from the first.
        var localDay = TimeZoneInfo.ConvertTime(startUtc, zone).Date;
        var todayLocal = TimeZoneInfo.ConvertTime(nowUtc, zone).Date;
        var dayOffset = (int)(localDay - todayLocal).TotalDays;
        if (dayOffset < 0 || dayOffset >= Math.Max(1, rules.HorizonDays)) return false;

        var slots = GetSlots(rules, zone, busy, nowUtc, dayOffset + 1, minutes, maxResults: int.MaxValue);
        return slots.Any(s => s.StartsAtUtc == startUtc);
    }

    public static int ClampDuration(SchedulingRules rules, int? requested)
    {
        var slot = Math.Max(1, rules.SlotMinutes);
        var max = Math.Max(slot, rules.MaxMinutes);
        return Math.Clamp(requested ?? slot, slot, max);
    }

    /// <summary>
    /// Reads the working week. A malformed or empty document yields an empty week — nothing
    /// bookable — rather than an exception: the write path validates this JSON, so a bad value here
    /// means something upstream already went wrong, and the safe failure is to offer no times at
    /// all rather than to offer wrong ones.
    /// </summary>
    public static Dictionary<DayOfWeek, List<(TimeOnly Start, TimeOnly End)>> ParseWeek(string? json)
    {
        var week = new Dictionary<DayOfWeek, List<(TimeOnly, TimeOnly)>>();
        if (string.IsNullOrWhiteSpace(json)) return week;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return week;

            for (var day = 0; day < 7; day++)
            {
                if (!doc.RootElement.TryGetProperty(DayKeys[day], out var arr) || arr.ValueKind != JsonValueKind.Array)
                    continue;

                var windows = new List<(TimeOnly, TimeOnly)>();
                foreach (var pair in arr.EnumerateArray())
                {
                    if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2) continue;
                    if (!TimeOnly.TryParse(pair[0].GetString(), CultureInfo.InvariantCulture, out var from)) continue;
                    if (!TimeOnly.TryParse(pair[1].GetString(), CultureInfo.InvariantCulture, out var to)) continue;
                    if (to <= from) continue;
                    windows.Add((from, to));
                }

                if (windows.Count > 0)
                    week[(DayOfWeek)day] = windows.OrderBy(w => w.Item1).ToList();
            }
        }
        catch (JsonException)
        {
            return new Dictionary<DayOfWeek, List<(TimeOnly, TimeOnly)>>();
        }

        return week;
    }

    /// <summary>True when the document is a week this engine can read and something is bookable in it.</summary>
    public static bool IsValidWeek(string? json) => ParseWeek(json).Count > 0;

    private static bool OverlapsAny(DateTimeOffset start, DateTimeOffset end, List<Interval> blocked)
    {
        foreach (var b in blocked)
        {
            if (b.Start >= end) break;      // sorted: nothing later can overlap either
            if (b.End > start) return true; // half-open overlap: touching edges are free
        }
        return false;
    }

    /// <summary>
    /// Local wall-clock to instant. Returns false for a time that does not exist (the hour a
    /// spring-forward skips) — such a slot is simply never offered. An ambiguous time (the hour a
    /// fall-back repeats) resolves to the standard offset, which is what TimeZoneInfo does and what
    /// a person means when they say "two o'clock" on that morning.
    /// </summary>
    private static bool TryToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone, out DateTimeOffset instant)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            instant = default;
            return false;
        }
        instant = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return true;
    }

    /// <summary>"Thu 12 Sep, 14:20" — short enough to say on the phone, unambiguous enough to trust.</summary>
    public static string FormatLocal(DateTimeOffset instantUtc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instantUtc, zone).ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture);
}
