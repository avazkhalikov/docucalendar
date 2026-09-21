using DocuCalendar.Infrastructure.Data;
using DocuCalendar.Infrastructure.Services;
using DocuCalendar.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// The two calls the AI makes: what times are free, and take one. Shaped for a model on a phone
/// line — every time comes back pre-rendered in the account's own words ("Thu 10 Sep, 12:00"), and
/// a refusal always arrives with alternatives so the assistant can keep the conversation moving.
/// </summary>
[ApiController]
[Route("api/booking")]
[RequireApiKey]
public sealed class BookingController : ControllerBase
{
    private readonly CalendarDbContext _db;
    private readonly CalendarService _calendars;
    private readonly BookingService _booking;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BookingController> _logger;

    public BookingController(
        CalendarDbContext db, CalendarService calendars, BookingService booking,
        IServiceScopeFactory scopes, ILogger<BookingController> logger)
    {
        _db = db;
        _calendars = calendars;
        _booking = booking;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>
    /// Free times, newest constraints applied. `noCalendar` is a first-class answer: it is how
    /// Docurest knows to keep the booking tools switched off for a context nobody has set up.
    /// </summary>
    [HttpGet("slots")]
    public async Task<IActionResult> Slots(
        [FromQuery] Guid? contextId,
        [FromQuery] string? calendarHint,
        [FromQuery] int days = 7,
        [FromQuery] int? minutes = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] int perDay = 0,
        [FromQuery] string? callerPhone = null,
        CancellationToken ct = default)
    {
        var tenant = HttpContext.Tenant();
        var resolved = await _calendars.ResolveAsync(tenant.TenantId, contextId, calendarHint, ct);
        if (resolved.UnknownStaff)
        {
            // Not a fallback to the default: the caller asked for somebody who does not take
            // appointments here, and the assistant must say so rather than book them elsewhere.
            _logger.LogInformation("[Booking] {Tenant}: nobody called \"{Hint}\" has a calendar; bookable: {Labels}.",
                tenant.TenantId, calendarHint, string.Join(", ", resolved.BookableLabels));
            return Ok(new { unknownStaff = true, requested = calendarHint, bookable = resolved.BookableLabels });
        }
        var calendar = resolved.Calendar;
        if (calendar == null)
            return Ok(new { noCalendar = true, message = "No calendar is set up for this context." });

        // The number the call came from decides whether "Bookable staff" hours are shown; a
        // website visitor has none and sees public hours only.
        var slots = await _booking.GetSlotsAsync(tenant, calendar, days, minutes, ct, from: from, perDay: Math.Clamp(perDay, 0, 50), callerPhone: callerPhone);
        // The calendar's own way of taking appointments travels with its times, so the assistant
        // adapts even when the caller named a person whose script differs from the line's default.
        var script = Application.Scheduling.BookingScript.Parse(calendar.BookingScriptJson);
        return Ok(new
        {
            calendarId = calendar.Id,
            calendarLabel = calendar.Label,
            timeZone = tenant.TimeZoneId,
            slotMinutes = calendar.SlotMinutes,
            maxMinutes = calendar.MaxMinutes,
            slots = slots.Select(s => new { startsAtUtc = s.StartsAtUtc, local = s.Local }),
            script = new
            {
                instructions = script.Instructions,
                questions = script.Questions.Select(q => new { ask = q.Ask, required = q.Required }),
                services = script.Services.Select(s => new { name = s.Name, minutes = s.Minutes }),
            },
        });
    }

    [HttpPost("book")]
    public async Task<IActionResult> Book([FromBody] BookRequest body, CancellationToken ct)
    {
        var tenant = HttpContext.Tenant();
        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == body.CalendarId && c.TenantId == tenant.TenantId && c.Active, ct);
        if (calendar == null) return NotFound(new { message = "No such calendar on this account." });

        var outcome = await _booking.BookAsync(
            tenant, calendar, body.StartsAtUtc, body.Minutes,
            body.VisitorName ?? string.Empty, body.VisitorPhone ?? string.Empty,
            body.Topic, string.IsNullOrWhiteSpace(body.Channel) ? "chat" : body.Channel!, body.SourceRef,
            body.ServiceName, BookingService.AnswersToJson(body.Answers?.Select(a => (a.Question, a.Answer))), ct,
            callerPhone: body.CallerPhone, canNotifyCaller: body.CanNotifyCaller);

        if (!outcome.Success)
        {
            // 409 with alternatives is the interesting failure; a bad name or phone is a 400.
            var status = outcome.Alternatives.Count > 0 || outcome.Error!.Contains("available") || outcome.Error.Contains("taken")
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            return StatusCode(status, new
            {
                message = outcome.Error,
                alternatives = outcome.Alternatives.Select(s => new { startsAtUtc = s.StartsAtUtc, local = s.Local }),
            });
        }

        var appointment = outcome.Appointment!;
        var zone = TenantService.ZoneOf(tenant);
        var apiKey = HttpContext.ApiKey();

        // Notifying Docurest is not the visitor's problem: detached, with its own scope because
        // this request's DbContext is gone the moment the response is written.
        _ = Task.Run(async () =>
        {
            using var scope = _scopes.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<DocurestWebhookSender>();
            await sender.SendBookedAsync(tenant, calendar, appointment, apiKey);
        });

        return StatusCode(StatusCodes.Status201Created, new
        {
            appointmentId = appointment.Id,
            startsAtUtc = appointment.StartsAt,
            endsAtUtc = appointment.EndsAt,
            local = Application.Scheduling.SlotEngine.FormatLocal(appointment.StartsAt, zone),
            minutes = (int)(appointment.EndsAt - appointment.StartsAt).TotalMinutes,
            calendarLabel = calendar.Label,
            serviceName = appointment.ServiceName,
            // "pending" means a request the owner still has to accept; "confirmed" is a booking.
            status = appointment.Status,
            // A colleague from the staff list: the answer also goes to this address.
            notifyEmail = appointment.NotifyEmail,
        });
    }

    /// <summary>
    /// Docurest accepting or declining a request on the owner's behalf — the buttons under the
    /// Telegram message. The same rules as the Schedule page: only a pending request changes.
    /// </summary>
    [HttpPost("appointments/{appointmentId:guid}/decide")]
    public async Task<IActionResult> Decide(Guid appointmentId, [FromBody] DecideRequest body, CancellationToken ct)
    {
        var tenant = HttpContext.Tenant();
        var decisions = HttpContext.RequestServices.GetRequiredService<AppointmentDecisions>();
        var result = await decisions.DecideAsync(tenant.TenantId, appointmentId, body.Confirm, body.ByName, ct);
        if (!result.Found) return NotFound(new { message = "No such appointment on this account." });
        return Ok(new { status = result.Status, changed = result.WasPending });
    }

    public sealed class DecideRequest
    {
        public bool Confirm { get; set; }
        public string? ByName { get; set; }
    }

    /// <summary>Cancelling from the Docurest side (a caller who rings back to call it off).</summary>
    [HttpPost("cancel/{appointmentId:guid}")]
    public async Task<IActionResult> Cancel(Guid appointmentId, [FromBody] CancelRequest? body, CancellationToken ct)
    {
        var tenant = HttpContext.Tenant();
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenant.TenantId, ct);
        if (appointment == null) return NotFound();
        if (appointment.Status == "cancelled") return Ok(new { cancelled = true });

        appointment.Status = "cancelled";
        appointment.CancelledAt = DateTimeOffset.UtcNow;
        appointment.CancelledByName = body?.By ?? "the caller";
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[Booking] {Tenant}: appointment {Id} cancelled.", tenant.TenantId, appointmentId);
        return Ok(new { cancelled = true });
    }

    public sealed class BookRequest
    {
        public Guid CalendarId { get; set; }
        public DateTimeOffset StartsAtUtc { get; set; }
        public int? Minutes { get; set; }
        public string? VisitorName { get; set; }
        public string? VisitorPhone { get; set; }
        public string? Topic { get; set; }
        public string? Channel { get; set; }
        public string? SourceRef { get; set; }
        /// <summary>One of the calendar's services, by name. Sets the length; unknown names are ignored.</summary>
        public string? ServiceName { get; set; }
        /// <summary>The caller's answers to the calendar's intake questions.</summary>
        public List<AnswerDto>? Answers { get; set; }
        /// <summary>The number the call came from (caller ID), not the number the caller gave: it unlocks "Bookable staff" hours.</summary>
        public string? CallerPhone { get; set; }
        /// <summary>Whether the caller can be told the outcome later (Docurest has an SMS service for this account). Without it, a calendar that asks for confirmation books instantly.</summary>
        public bool CanNotifyCaller { get; set; }
    }

    public sealed class AnswerDto
    {
        public string? Question { get; set; }
        public string? Answer { get; set; }
    }

    public sealed class CancelRequest
    {
        public string? By { get; set; }
    }

    /// <summary>
    /// The owner's day and month at a glance, for Docurest's CEO pages: today's agenda across
    /// every calendar on the account, the week's counts, and how many of this and last month's
    /// visits the AGENT booked (any channel that is not "manual"). Same key as booking — the
    /// data belongs to the account the key belongs to.
    /// </summary>
    [HttpGet("agenda")]
    public async Task<IActionResult> Agenda([FromQuery] DateOnly? date, CancellationToken ct = default)
    {
        var tenant = HttpContext.Tenant();
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZoneId); } catch { zone = TimeZoneInfo.Utc; }

        var todayLocal = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        DateTimeOffset LocalStart(DateOnly d)
        {
            var dt = d.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(dt, zone.GetUtcOffset(dt));
        }
        var mondayOffset = ((int)todayLocal.DayOfWeek + 6) % 7;
        var dayFrom = LocalStart(todayLocal);
        var dayTo = LocalStart(todayLocal.AddDays(1));
        var weekEnd = LocalStart(todayLocal.AddDays(7 - mondayOffset));
        var monthStart = LocalStart(new DateOnly(todayLocal.Year, todayLocal.Month, 1));
        var prevMonthStart = LocalStart(new DateOnly(todayLocal.Year, todayLocal.Month, 1).AddMonths(-1));

        var window = await _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.Status != "cancelled" && a.Status != "declined"
                     && a.StartsAt >= prevMonthStart && a.StartsAt < weekEnd)
            .Select(a => new { a.StartsAt, a.EndsAt, a.VisitorName, a.ServiceName, a.Topic, a.Channel, a.CreatedAt })
            .ToListAsync(ct);

        var today = window.Where(a => a.StartsAt >= dayFrom && a.StartsAt < dayTo)
            .OrderBy(a => a.StartsAt)
            .Select(a => new
            {
                startsAtUtc = a.StartsAt.ToUniversalTime(),
                minutes = (int)Math.Max(5, (a.EndsAt - a.StartsAt).TotalMinutes),
                who = a.VisitorName,
                what = string.IsNullOrWhiteSpace(a.ServiceName) ? (a.Topic ?? "Visit") : a.ServiceName!,
                channel = a.Channel,
                createdAtUtc = a.CreatedAt.ToUniversalTime(),
            }).ToList();

        var week = Enumerable.Range(0, 7).Select(i =>
        {
            var d = todayLocal.AddDays(i - mondayOffset);
            var from = LocalStart(d);
            var to = LocalStart(d.AddDays(1));
            var of = window.Where(a => a.StartsAt >= from && a.StartsAt < to).ToList();
            return new
            {
                day = d.DayOfWeek.ToString()[..3],
                date = d.Day,
                total = of.Count,
                byAgent = of.Count(a => a.Channel != "manual"),
                isToday = d == todayLocal,
            };
        }).ToList();

        int BookedByAgent(DateTimeOffset from, DateTimeOffset to) =>
            window.Count(a => a.StartsAt >= from && a.StartsAt < to && a.Channel != "manual");

        return Ok(new
        {
            timeZone = tenant.TimeZoneId,
            date = todayLocal.ToString("yyyy-MM-dd"),
            today,
            todayByAgent = today.Count(t => t.channel != "manual"),
            week,
            monthBooked = BookedByAgent(monthStart, dayTo),
            prevMonthBooked = BookedByAgent(prevMonthStart, monthStart),
        });
    }
}
