using DocuCalendar.Application.Scheduling;
using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using DocuCalendar.Infrastructure.Services;
using DocuCalendar.Infrastructure.Services.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// The week as staff actually work it: time marked unavailable, and the appointments people have
/// booked. Everything here is read and written in the account's timezone at the edges and stored
/// as instants underneath.
/// </summary>
[ApiController]
[Route("api/schedule")]
public sealed class ScheduleController : StaffControllerBase
{
    private readonly CalendarDbContext _db;
    private readonly BookingService _booking;
    private readonly SyncScheduler _scheduler;
    private readonly ILogger<ScheduleController> _logger;

    public ScheduleController(CalendarDbContext db, BookingService booking, SyncScheduler scheduler, ILogger<ScheduleController> logger)
    {
        _db = db;
        _booking = booking;
        _scheduler = scheduler;
        _logger = logger;
    }

    private static string SourceName(string source) => source switch
    {
        "microsoft" => "Outlook 365",
        "google" => "Google Calendar",
        _ => source,
    };

    /// <summary>One calendar's week: busy time, appointments, and the free slots that remain —
    /// the same slots the AI would offer, so what staff see is what a caller gets.</summary>
    [HttpGet("{calendarId:guid}")]
    public async Task<IActionResult> Week(
        Guid calendarId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] int days = 7,
        CancellationToken ct = default)
    {
        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == calendarId && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();

        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == TenantId, ct);
        var zone = TenantService.ZoneOf(tenant);
        var start = (from ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var end = start.AddDays(Math.Clamp(days, 1, 62));

        var blocks = await _db.BusyBlocks.AsNoTracking()
            .Where(b => b.CalendarId == calendarId && b.EndsAt > start && b.StartsAt < end)
            .OrderBy(b => b.StartsAt)
            .ToListAsync(ct);

        var appointments = await _db.Appointments.AsNoTracking()
            .Where(a => a.CalendarId == calendarId && a.EndsAt > start && a.StartsAt < end)
            .OrderBy(a => a.StartsAt)
            .ToListAsync(ct);

        // Uncapped: this drives the "N free" count on every day of the week, and a cap makes the
        // last days of the range read as fully booked when nothing is booked at all. The engine
        // scans forward from NOW, not from the range's start — so ask it for enough days to reach
        // the end of the range (a next-month view would otherwise stop where next week does), and
        // keep only what falls inside. Its own horizon still applies: days a caller cannot book
        // yet show no free time, which is the truth.
        var slotDays = (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalDays) + 1;
        var slots = (await _booking.GetSlotsAsync(tenant, calendar, Math.Clamp(slotDays, 1, 400), null, ct, maxResults: int.MaxValue))
            .Where(s => s.StartsAtUtc >= start && s.StartsAtUtc < end)
            .ToList();

        // What a mirrored block is called — "Dentist", "1:1 with the dean" — is the person's own
        // business. Their calendar's owner sees it; everyone else, the account owner included,
        // sees only that the time is taken. Reasons typed in here by hand were always for staff.
        var seesMirroredTitles = calendar.OwnerUserId == UserId;

        return Ok(new
        {
            calendar = new { calendar.Id, calendar.Label, canEdit = CanManage(calendar), calendar.SlotMinutes, calendar.MaxMinutes },
            timeZone = tenant.TimeZoneId,
            busy = blocks.Select(b => new
            {
                b.Id,
                startsAtUtc = b.StartsAt,
                endsAtUtc = b.EndsAt,
                local = SlotEngine.FormatLocal(b.StartsAt, zone),
                reason = b.Source == "manual" || seesMirroredTitles ? b.Reason : null,
                b.Source,
            }),
            appointments = appointments.Select(a => new
            {
                a.Id,
                startsAtUtc = a.StartsAt,
                endsAtUtc = a.EndsAt,
                local = SlotEngine.FormatLocal(a.StartsAt, zone),
                minutes = (int)(a.EndsAt - a.StartsAt).TotalMinutes,
                a.VisitorName,
                a.VisitorPhone,
                a.Topic,
                a.ServiceName,
                answers = BookingService.ParseAnswers(a.AnswersJson).Select(x => new { x.Question, x.Answer }),
                a.Channel,
                a.Status,
            }),
            freeSlots = slots.Select(s => new { startsAtUtc = s.StartsAtUtc, local = s.Local }),
        });
    }

    [HttpPost("{calendarId:guid}/busy")]
    public async Task<IActionResult> AddBusy(Guid calendarId, [FromBody] BusyRequest body, CancellationToken ct)
    {
        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == calendarId && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (!CanManage(calendar)) return NotYours();
        if (body.EndsAtUtc <= body.StartsAtUtc)
            return BadRequest(new { message = "The end of a busy period must come after its start." });

        var block = new BusyBlock
        {
            CalendarId = calendarId,
            StartsAt = body.StartsAtUtc.ToUniversalTime(),
            EndsAt = body.EndsAtUtc.ToUniversalTime(),
            Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason!.Trim(),
            Source = "manual",
        };
        _db.BusyBlocks.Add(block);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[Schedule] {Tenant}: {User} marked {Start:u}–{End:u} busy on \"{Label}\".",
            TenantId, DisplayName, block.StartsAt, block.EndsAt, calendar.Label);
        return Ok(new { id = block.Id });
    }

    [HttpDelete("busy/{id:guid}")]
    public async Task<IActionResult> RemoveBusy(Guid id, CancellationToken ct)
    {
        var block = await _db.BusyBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block == null) return NotFound();

        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == block.CalendarId && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (!CanManage(calendar)) return NotYours();
        // Time mirrored from an external calendar is not ours to delete — it would reappear on the
        // next sync and the person would rightly wonder what they had done wrong.
        if (block.Source != "manual")
            return BadRequest(new { message = $"This time comes from {SourceName(block.Source)} — change it there." });

        _db.BusyBlocks.Remove(block);
        await _db.SaveChangesAsync(ct);
        return Ok(new { removed = true });
    }

    /// <summary>A staff member booking somebody in by hand — the same rules as the AI, deliberately:
    /// if a time cannot be offered to a caller it should not be quietly bookable from inside either.</summary>
    [HttpPost("{calendarId:guid}/appointments")]
    public async Task<IActionResult> AddAppointment(Guid calendarId, [FromBody] ManualBookingRequest body, CancellationToken ct)
    {
        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == calendarId && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (!CanManage(calendar)) return NotYours();

        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == TenantId, ct);
        var outcome = await _booking.BookAsync(
            tenant, calendar, body.StartsAtUtc.ToUniversalTime(), body.Minutes,
            body.VisitorName ?? string.Empty, body.VisitorPhone ?? string.Empty,
            body.Topic, "manual", null, body.ServiceName, null, ct);

        if (!outcome.Success)
            return BadRequest(new
            {
                message = outcome.Error,
                alternatives = outcome.Alternatives.Select(s => new { startsAtUtc = s.StartsAtUtc, local = s.Local }),
            });

        return Ok(new { id = outcome.Appointment!.Id });
    }

    [HttpPost("appointments/{id:guid}/cancel")]
    public async Task<IActionResult> CancelAppointment(Guid id, CancellationToken ct)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.TenantId == TenantId, ct);
        if (appointment == null) return NotFound();

        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == appointment.CalendarId, ct);
        if (calendar == null || !CanManage(calendar)) return NotYours();

        appointment.Status = "cancelled";
        appointment.CancelledAt = DateTimeOffset.UtcNow;
        appointment.CancelledByName = DisplayName;
        await _db.SaveChangesAsync(ct);
        // So the copy in the person's Outlook or Google calendar goes within seconds, not minutes.
        _scheduler.Nudge(appointment.CalendarId);
        _logger.LogInformation("[Schedule] {Tenant}: {User} cancelled appointment {Id}.", TenantId, DisplayName, id);
        return Ok(new { cancelled = true });
    }

    public sealed class BusyRequest
    {
        public DateTimeOffset StartsAtUtc { get; set; }
        public DateTimeOffset EndsAtUtc { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class ManualBookingRequest
    {
        public DateTimeOffset StartsAtUtc { get; set; }
        public int? Minutes { get; set; }
        public string? VisitorName { get; set; }
        public string? VisitorPhone { get; set; }
        public string? Topic { get; set; }
        public string? ServiceName { get; set; }
    }
}
