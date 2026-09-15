using System.Data;
using DocuCalendar.Application.Scheduling;
using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocuCalendar.Infrastructure.Services;

/// <summary>Why a booking did not happen, and what to offer instead.</summary>
public sealed record BookOutcome(
    bool Success,
    Appointment? Appointment,
    string? Error,
    IReadOnlyList<Slot> Alternatives)
{
    public static BookOutcome Ok(Appointment a) => new(true, a, null, Array.Empty<Slot>());
    public static BookOutcome Invalid(string error) => new(false, null, error, Array.Empty<Slot>());
    public static BookOutcome Taken(string error, IReadOnlyList<Slot> alternatives) => new(false, null, error, alternatives);
}

/// <summary>
/// Offers times and books them. The two jobs live together because they must agree: a slot is
/// offered by exactly the rules that will be re-checked when somebody tries to take it.
/// </summary>
public sealed class BookingService
{
    private readonly CalendarDbContext _db;
    private readonly Sync.SyncScheduler _scheduler;
    private readonly ILogger<BookingService> _logger;

    public BookingService(CalendarDbContext db, Sync.SyncScheduler scheduler, ILogger<BookingService> logger)
    {
        _db = db;
        _scheduler = scheduler;
        _logger = logger;
    }

    public static SchedulingRules RulesOf(StaffCalendar c) =>
        new(c.SlotMinutes, c.MaxMinutes, c.BufferMinutes, c.MinLeadMinutes, c.HorizonDays, c.WeeklyAvailabilityJson);

    /// <summary>
    /// Free times. <paramref name="maxResults"/> exists because two callers want different things
    /// from this: an assistant on the phone needs a short menu, while the week view needs EVERY
    /// slot or its per-day counts lie — with the default cap, a week's worth of slots ran out on
    /// Tuesday and Wednesday was reported as fully booked when it was empty.
    /// </summary>
    public async Task<IReadOnlyList<Slot>> GetSlotsAsync(
        TenantRegistration tenant, StaffCalendar calendar, int days, int? minutes, CancellationToken ct,
        int maxResults = SlotEngine.DefaultMaxResults, DateOnly? from = null, int perDay = 0, string? callerPhone = null)
    {
        var zone = TenantService.ZoneOf(tenant);
        var now = DateTimeOffset.UtcNow;
        var busy = await LoadBusyAsync(calendar.Id, now.AddDays(-1), now.AddDays(calendar.HorizonDays + 1), ct);
        var windows = await LoadWindowsAsync(calendar, now.AddDays(-1), now.AddDays(calendar.HorizonDays + 1), callerPhone, ct);
        return SlotEngine.GetSlots(RulesOf(calendar), zone, busy, now, days, minutes, maxResults, from, perDay, windows);
    }

    /// <summary>
    /// Takes a slot, or explains why it could not and offers the nearest real alternatives.
    ///
    /// The overlap check and the insert share one serializable transaction: two callers asking for
    /// the same time a second apart is not a rare edge case on a phone line, it is Tuesday. The
    /// loser is told immediately and handed three fresh times, so the AI can re-offer inside the
    /// same breath rather than starting the conversation again.
    /// </summary>
    public async Task<BookOutcome> BookAsync(
        TenantRegistration tenant,
        StaffCalendar calendar,
        DateTimeOffset startUtc,
        int? requestedMinutes,
        string visitorName,
        string visitorPhone,
        string? topic,
        string channel,
        string? sourceRef,
        string? serviceName,
        string? answersJson,
        CancellationToken ct,
        string? callerPhone = null)
    {
        visitorName = (visitorName ?? string.Empty).Trim();
        visitorPhone = (visitorPhone ?? string.Empty).Trim();

        if (visitorName.Length < 2)
            return BookOutcome.Invalid("A name is required before an appointment can be made.");
        if (visitorPhone.Count(char.IsDigit) < 7)
            return BookOutcome.Invalid("A reachable phone number is required before an appointment can be made.");

        var rules = RulesOf(calendar);

        // A named service decides the length — a filling is an hour whatever the default slot is.
        // The caller's own request still applies when no service matched, and the calendar's
        // ceiling applies to both.
        var script = BookingScript.Parse(calendar.BookingScriptJson);
        var service = script.FindService(serviceName);
        var minutes = SlotEngine.ClampDuration(rules, service?.Minutes ?? requestedMinutes);
        var zone = TenantService.ZoneOf(tenant);
        var now = DateTimeOffset.UtcNow;
        // The wire may deliver "+05:00"; everything below stores and compares in UTC.
        startUtc = startUtc.ToUniversalTime();

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var busy = await LoadBusyAsync(calendar.Id, now.AddDays(-1), startUtc.AddDays(1), ct);
            var windows = await LoadWindowsAsync(calendar, now.AddDays(-1), now.AddDays(calendar.HorizonDays + 1), callerPhone, ct);

            if (!SlotEngine.IsOfferable(rules, zone, busy, now, startUtc, minutes, windows))
            {
                var alternatives = SlotEngine
                    .GetSlots(rules, zone, busy, now, Math.Min(7, calendar.HorizonDays), minutes, maxResults: 3, windows: windows);
                return BookOutcome.Taken(
                    "That time is no longer available.",
                    alternatives);
            }

            var appointment = new Appointment
            {
                CalendarId = calendar.Id,
                TenantId = tenant.TenantId,
                StartsAt = startUtc,
                EndsAt = startUtc.AddMinutes(minutes),
                VisitorName = visitorName,
                VisitorPhone = visitorPhone,
                Topic = string.IsNullOrWhiteSpace(topic) ? null : topic!.Trim(),
                ServiceName = service?.Name,
                AnswersJson = answersJson,
                Channel = channel,
                SourceRef = sourceRef,
                Status = "confirmed",
            };
            _db.Appointments.Add(appointment);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // After the commit, never before: the sync must see the row it is about to push.
            _scheduler.Nudge(calendar.Id);

            _logger.LogInformation(
                "[Booking] {Tenant}/{Calendar}: {Visitor} booked {Start:u} for {Minutes} min via {Channel}.",
                tenant.TenantId, calendar.Label, visitorName, startUtc, minutes, channel);
            return BookOutcome.Ok(appointment);
        }
        catch (DbUpdateException ex)
        {
            // A serialization failure is the database telling us somebody else won the race — the
            // same answer as a taken slot, so the caller gets the same helpful reply.
            await SafeRollback(tx, ct);
            _logger.LogInformation(ex, "[Booking] {Tenant}: lost a race for {Start:u}.", tenant.TenantId, startUtc);
            var busy = await LoadBusyAsync(calendar.Id, now.AddDays(-1), startUtc.AddDays(1), ct);
            var windows = await LoadWindowsAsync(calendar, now.AddDays(-1), now.AddDays(calendar.HorizonDays + 1), callerPhone, ct);
            var alternatives = SlotEngine.GetSlots(rules, zone, busy, now, Math.Min(7, calendar.HorizonDays), minutes, maxResults: 3, windows: windows);
            return BookOutcome.Taken("That time was taken a moment ago.", alternatives);
        }
        catch (Exception)
        {
            await SafeRollback(tx, ct);
            throw;
        }
    }

    /// <summary>
    /// The caller's answers, as the JSON kept on the appointment: only pairs with something in
    /// both halves, trimmed, capped — a model can send anything, and a column is not a bin.
    /// </summary>
    public static string? AnswersToJson(IEnumerable<(string? Question, string? Answer)>? answers)
    {
        if (answers is null) return null;
        var clean = answers
            .Where(a => !string.IsNullOrWhiteSpace(a.Question) && !string.IsNullOrWhiteSpace(a.Answer))
            .Select(a => new BookingAnswer(Clip(a.Question!.Trim(), 200), Clip(a.Answer!.Trim(), 500)))
            .Take(BookingScript.MaxQuestions)
            .ToList();
        if (clean.Count == 0) return null;
        var json = System.Text.Json.JsonSerializer.Serialize(clean, AnswersJsonOptions);
        return json.Length > 4000 ? null : json;
    }

    public static IReadOnlyList<BookingAnswer> ParseAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<BookingAnswer>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<BookingAnswer>>(json, AnswersJsonOptions)
                   ?? (IReadOnlyList<BookingAnswer>)Array.Empty<BookingAnswer>();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<BookingAnswer>();
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions AnswersJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;

    /// <summary>Everything that occupies the calendar: blocked time and appointments alike. The
    /// slot engine does not care which is which, and neither should a visitor.</summary>
    /// <summary>
    /// The "Bookable" / "Bookable staff" windows the person marked in their own calendar (mirrored
    /// blocks whose title is exactly one of those), narrowed to what THIS caller may see: public
    /// windows always, staff windows only when the call comes from a number on the calendar's
    /// staff list. While any remain, the slot engine offers only them.
    /// </summary>
    public async Task<List<Interval>> LoadWindowsAsync(StaffCalendar calendar, DateTimeOffset fromUtc, DateTimeOffset toUtc, string? callerPhone, CancellationToken ct)
    {
        fromUtc = fromUtc.ToUniversalTime();
        toUtc = toUtc.ToUniversalTime();
        var blocks = await _db.BusyBlocks.AsNoTracking()
            .Where(b => b.CalendarId == calendar.Id && b.EndsAt > fromUtc && b.StartsAt < toUtc && b.Reason != null)
            .Select(b => new { b.StartsAt, b.EndsAt, b.Reason })
            .ToListAsync(ct);
        var windows = blocks
            .Select(b => (Kind: BookableWindows.KindOf(b.Reason), b.StartsAt, b.EndsAt))
            .Where(b => b.Kind != null)
            .Select(b => new BookableWindow(new Interval(b.StartsAt, b.EndsAt), b.Kind!.Value))
            .ToList();
        var isStaff = BookableWindows.IsStaffCaller(callerPhone, StaffCallers.Phones(calendar.StaffCallersJson));
        return BookableWindows.Visible(windows, isStaff);
    }

    public async Task<List<Interval>> LoadBusyAsync(Guid calendarId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        // Belt and braces: a caller may hand us an instant carrying a local offset (a time that
        // came back out of the API and went round again), and Postgres will not accept one as a
        // timestamptz parameter however correct the instant is.
        fromUtc = fromUtc.ToUniversalTime();
        toUtc = toUtc.ToUniversalTime();

        // A "Bookable" / "Bookable staff" block is a window, not taken time — LoadWindowsAsync
        // reads those. Classified in memory, so the rule lives in one place (BookableWindows).
        var blocks = (await _db.BusyBlocks.AsNoTracking()
            .Where(b => b.CalendarId == calendarId && b.EndsAt > fromUtc && b.StartsAt < toUtc)
            .Select(b => new { b.StartsAt, b.EndsAt, b.Reason })
            .ToListAsync(ct))
            .Where(b => !BookableWindows.IsBookable(b.Reason))
            .ToList();

        var appointments = await _db.Appointments.AsNoTracking()
            .Where(a => a.CalendarId == calendarId && a.Status == "confirmed" && a.EndsAt > fromUtc && a.StartsAt < toUtc)
            .Select(a => new { a.StartsAt, a.EndsAt })
            .ToListAsync(ct);

        return blocks.Select(b => new Interval(b.StartsAt, b.EndsAt))
            .Concat(appointments.Select(a => new Interval(a.StartsAt, a.EndsAt)))
            .ToList();
    }

    private static async Task SafeRollback(IDisposable tx, CancellationToken ct)
    {
        if (tx is Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction t)
        {
            try { await t.RollbackAsync(ct); } catch { /* the transaction is already gone */ }
        }
    }
}
