using DocuCalendar.Application.Scheduling;
using DocuCalendar.Infrastructure.Data;
using DocuCalendar.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// Every appointment on the account, in one list. The Schedule shows one calendar's week, which
/// answers "what does Tuesday look like for Aziza"; this answers the other question an owner
/// actually asks — "who is coming, across everybody, and who do I ring if something changes".
///
/// The owner sees the whole account; anyone else sees their own calendars, the same rule the
/// Schedule already follows.
/// </summary>
[ApiController]
[Route("api/appointments")]
public sealed class AppointmentsController : StaffControllerBase
{
    private const int MaxPageSize = 200;

    private readonly CalendarDbContext _db;

    public AppointmentsController(CalendarDbContext db) => _db = db;

    /// <param name="from">Local date, inclusive. Default: today.</param>
    /// <param name="to">Local date, inclusive. Default: 30 days after <paramref name="from"/>.</param>
    /// <param name="calendarId">One calendar, or all the ones this person may see.</param>
    /// <param name="status">"confirmed", "pending", … or "all" (the default is everything but cancelled ones).</param>
    /// <param name="search">Matches a name, either phone number, or the topic.</param>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? calendarId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == TenantId, ct);
        if (tenant == null) return NotFound();
        var zone = TenantService.ZoneOf(tenant);

        var calendars = await _db.Calendars.AsNoTracking()
            .Where(c => c.TenantId == TenantId)
            .ToListAsync(ct);
        var mine = calendars.Where(CanManage).Select(c => c.Id).ToHashSet();
        if (calendarId is { } one && !mine.Contains(one)) return NotYours();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var fromDate = from ?? today;
        var toDate = to ?? fromDate.AddDays(30);
        if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);

        // Local days, in the account's own zone: "16 September" means that day where the office is.
        var fromUtc = ToUtc(fromDate, zone);
        var toUtc = ToUtc(toDate.AddDays(1), zone);

        var query = _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == TenantId && a.StartsAt >= fromUtc && a.StartsAt < toUtc);

        query = calendarId is { } pick
            ? query.Where(a => a.CalendarId == pick)
            : query.Where(a => mine.Contains(a.CalendarId));

        var wanted = (status ?? string.Empty).Trim().ToLowerInvariant();
        query = wanted switch
        {
            "all" => query,
            { Length: > 0 } => query.Where(a => a.Status == wanted),
            // By default a cancelled appointment is history, not a thing to prepare for.
            _ => query.Where(a => a.Status != PendingRules.Cancelled && a.Status != PendingRules.Declined && a.Status != PendingRules.Expired),
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            // Digits only for the phone match, so "90 189" finds "+998901892620".
            var digits = new string(needle.Where(char.IsDigit).ToArray());
            query = query.Where(a =>
                EF.Functions.ILike(a.VisitorName, $"%{needle}%")
                || (a.Topic != null && EF.Functions.ILike(a.Topic, $"%{needle}%"))
                || (digits.Length >= 3 && (a.VisitorPhone.Contains(digits) || (a.CallerPhone != null && a.CallerPhone.Contains(digits)))));
        }

        var total = await query.CountAsync(ct);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var rows = await query
            .OrderBy(a => a.StartsAt)
            .Skip(Math.Max(0, page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var labels = calendars.ToDictionary(c => c.Id, c => c.Label);
        return Ok(new
        {
            timeZone = tenant.TimeZoneId,
            total,
            page = Math.Max(1, page),
            pageSize = size,
            from = fromDate,
            to = toDate,
            calendars = calendars.Where(c => mine.Contains(c.Id)).OrderBy(c => c.Label)
                .Select(c => new { c.Id, c.Label, c.Active }),
            appointments = rows.Select(a => new
            {
                a.Id,
                calendarId = a.CalendarId,
                calendarLabel = labels.GetValueOrDefault(a.CalendarId, "—"),
                startsAtUtc = a.StartsAt,
                endsAtUtc = a.EndsAt,
                local = SlotEngine.FormatLocal(a.StartsAt, zone),
                minutes = (int)(a.EndsAt - a.StartsAt).TotalMinutes,
                a.VisitorName,
                // Both numbers, deliberately: what they SAID, and what actually rang.
                a.VisitorPhone,
                a.CallerPhone,
                a.NotifyEmail,
                a.Topic,
                a.ServiceName,
                answers = BookingService.ParseAnswers(a.AnswersJson).Select(x => new { x.Question, x.Answer }),
                a.Channel,
                a.Status,
                a.CancelledByName,
                a.DecidedByName,
                createdAt = a.CreatedAt,
            }),
        });
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
