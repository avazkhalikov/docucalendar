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
        CancellationToken ct = default)
    {
        var tenant = HttpContext.Tenant();
        var calendar = await _calendars.ResolveAsync(tenant.TenantId, contextId, calendarHint, ct);
        if (calendar == null)
            return Ok(new { noCalendar = true, message = "No calendar is set up for this context." });

        var slots = await _booking.GetSlotsAsync(tenant, calendar, days, minutes, ct);
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
            body.ServiceName, BookingService.AnswersToJson(body.Answers?.Select(a => (a.Question, a.Answer))), ct);

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
        });
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
}
