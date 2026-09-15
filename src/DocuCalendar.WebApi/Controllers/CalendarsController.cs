using DocuCalendar.Application.Scheduling;
using DocuCalendar.Application.Sync;
using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using DocuCalendar.Infrastructure.Services.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// Setting up who can be booked and when: the calendars themselves, their working week and rules,
/// and which of them the AI uses for each knowledge context.
/// </summary>
[ApiController]
[Route("api/calendars")]
public sealed class CalendarsController : StaffControllerBase
{
    private readonly CalendarDbContext _db;
    private readonly IEnumerable<ICalendarProvider> _providers;
    private readonly TokenVault _vault;
    private readonly ILogger<CalendarsController> _logger;

    public CalendarsController(
        CalendarDbContext db,
        IEnumerable<ICalendarProvider> providers,
        TokenVault vault,
        ILogger<CalendarsController> logger)
    {
        _db = db;
        _providers = providers;
        _vault = vault;
        _logger = logger;
    }

    /// <summary>Everyone on the account sees the roster — knowing a colleague has a calendar is
    /// not the same as being able to edit it, and hiding it would make "who can I book?" unanswerable.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var calendars = await _db.Calendars.AsNoTracking()
            .Where(c => c.TenantId == TenantId)
            .OrderByDescending(c => c.Active).ThenBy(c => c.Label)
            .ToListAsync(ct);

        var defaults = await _db.ContextDefaults.AsNoTracking()
            .Where(d => d.TenantId == TenantId)
            .ToListAsync(ct);

        // Whether removing a calendar would delete it or merely retire it — the page says which
        // before anybody clicks, rather than surprising them afterwards.
        var withAppointments = (await _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == TenantId)
            .Select(a => a.CalendarId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        return Ok(new
        {
            canManageAll = IsOwner,
            me = UserId,
            calendars = calendars.Select(c => new
            {
                id = c.Id,
                label = c.Label,
                ownerUserId = c.OwnerUserId,
                mine = c.OwnerUserId == UserId,
                canEdit = CanManage(c),
                hasAppointments = withAppointments.Contains(c.Id),
                c.SlotMinutes,
                c.MaxMinutes,
                c.BufferMinutes,
                c.MinLeadMinutes,
                c.HorizonDays,
                weeklyAvailability = c.WeeklyAvailabilityJson,
                bookingScript = c.BookingScriptJson,
                c.Active,
                c.IsDefault,
            }),
            contextDefaults = defaults.Select(d => new { d.TenantContextId, d.CalendarId }),
        });
    }

    /// <summary>
    /// Anyone may create their own calendar — an operator connecting their Outlook should not
    /// have to ask the owner to make a calendar for them first. Creating one for SOMEBODY ELSE
    /// is the owner's job: "set up calendars for all staff" is one screen, and an operator
    /// minting calendars for other people is not a thing this product needs.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CalendarRequest body, CancellationToken ct)
    {
        if (!IsOwner && body.OwnerUserId is { } someoneElse && someoneElse != UserId)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Only the account owner can create calendars for other people." });
        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { message = "A label is required — it is what a caller will ask for." });

        var calendar = new StaffCalendar
        {
            TenantId = TenantId,
            OwnerUserId = body.OwnerUserId ?? UserId,
            Label = body.Label!.Trim(),
        };
        Apply(body, calendar, out var error);
        if (error != null) return BadRequest(new { message = error });

        // A person's first calendar is their default — there is nothing else it could be, and it
        // means the star is never missing from an account with one calendar per person.
        calendar.IsDefault = !await _db.Calendars
            .AnyAsync(c => c.TenantId == TenantId && c.OwnerUserId == calendar.OwnerUserId && c.Active && c.IsDefault, ct);

        _db.Calendars.Add(calendar);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[Calendars] {Tenant}: created \"{Label}\".", TenantId, calendar.Label);
        return Ok(new { id = calendar.Id });
    }

    /// <summary>
    /// Makes this the calendar the assistant books into when it lands on its person. The owner
    /// may set it for anyone; an operator only for themselves. Routing entries that pointed at
    /// the person's previous default move with it — that is the whole point of a default.
    /// </summary>
    [HttpPut("{id:guid}/default")]
    public async Task<IActionResult> MakeDefault(Guid id, CancellationToken ct)
    {
        var calendar = await _db.Calendars.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (!CanManage(calendar)) return NotYours();
        if (!calendar.Active) return BadRequest(new { message = "A retired calendar cannot be the default." });

        var previous = await _db.Calendars
            .Where(c => c.TenantId == TenantId && c.OwnerUserId == calendar.OwnerUserId && c.IsDefault && c.Id != id)
            .ToListAsync(ct);
        foreach (var p in previous)
        {
            p.IsDefault = false;
            p.UpdatedAt = DateTimeOffset.UtcNow;
        }
        calendar.IsDefault = true;
        calendar.UpdatedAt = DateTimeOffset.UtcNow;

        var previousIds = previous.Select(p => p.Id).ToList();
        var moved = 0;
        if (previousIds.Count > 0)
        {
            var routes = await _db.ContextDefaults
                .Where(d => d.TenantId == TenantId && previousIds.Contains(d.CalendarId))
                .ToListAsync(ct);
            foreach (var r in routes)
            {
                r.CalendarId = id;
                r.UpdatedAt = DateTimeOffset.UtcNow;
                moved++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[Calendars] {Tenant}: \"{Label}\" is now the default for its person; {Moved} routing entr(y/ies) followed.",
            TenantId, calendar.Label, moved);
        return Ok(new { saved = true, routesMoved = moved });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CalendarRequest body, CancellationToken ct)
    {
        var calendar = await _db.Calendars.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (!CanManage(calendar)) return NotYours();

        if (!string.IsNullOrWhiteSpace(body.Label)) calendar.Label = body.Label!.Trim();
        // Only the owner may hand a calendar to a different person.
        if (IsOwner && body.OwnerUserId is { } newOwner) calendar.OwnerUserId = newOwner;
        if (IsOwner && body.Active is { } active) calendar.Active = active;

        Apply(body, calendar, out var error);
        if (error != null) return BadRequest(new { message = error });

        calendar.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { saved = true });
    }

    /// <summary>
    /// Removes a calendar. One with appointments is retired, not deleted — those appointments must
    /// keep their home, and the row stays greyed out as the record of it. One with NO appointments
    /// has nothing to keep: it is deleted outright, together with its provider link and mirrored
    /// busy time, because a mislabelled calendar added a minute ago is the common case and a
    /// permanent grey row for it is clutter, not history. Whoever may edit the calendar may remove
    /// it: an operator their own, the owner anybody's.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var calendar = await _db.Calendars.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (!CanManage(calendar)) return NotYours();

        // Either way it must stop being a booking target, or the AI keeps sending people to it.
        var defaults = await _db.ContextDefaults.Where(d => d.TenantId == TenantId && d.CalendarId == id).ToListAsync(ct);
        _db.ContextDefaults.RemoveRange(defaults);

        // And either way, losing somebody's default hands the star to their oldest remaining
        // calendar, so the person is never left without one.
        if (calendar.IsDefault)
        {
            calendar.IsDefault = false;
            var heir = await _db.Calendars
                .Where(c => c.TenantId == TenantId && c.OwnerUserId == calendar.OwnerUserId && c.Active && c.Id != id)
                .OrderBy(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (heir != null) heir.IsDefault = true;
        }

        var hasAppointments = await _db.Appointments.AnyAsync(a => a.CalendarId == id, ct);
        if (hasAppointments)
        {
            calendar.Active = false;
            calendar.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("[Calendars] {Tenant}: \"{Label}\" retired by {User}.", TenantId, calendar.Label, DisplayName);
            return Ok(new { retired = true });
        }

        // Nothing booked, so nothing to preserve. Let go of the person's real calendar first: the
        // refresh token is revoked at the provider (best effort — a provider outage must not keep
        // a calendar that its owner asked to delete), then the link and the mirrored busy time go.
        var connection = await _db.ExternalConnections.FirstOrDefaultAsync(c => c.CalendarId == id, ct);
        if (connection != null)
        {
            var refresh = _vault.Unprotect(connection.RefreshTokenProtected);
            var provider = _providers.FirstOrDefault(p => string.Equals(p.Key, connection.Provider, StringComparison.OrdinalIgnoreCase));
            if (refresh != null && provider != null)
            {
                try { await provider.RevokeAsync(refresh, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "[Calendars] {Tenant}: could not revoke the {Provider} token of \"{Label}\" on delete.", TenantId, connection.Provider, calendar.Label); }
            }
            _db.ExternalConnections.Remove(connection);
        }
        var blocks = await _db.BusyBlocks.Where(b => b.CalendarId == id).ToListAsync(ct);
        _db.BusyBlocks.RemoveRange(blocks);
        _db.Calendars.Remove(calendar);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[Calendars] {Tenant}: \"{Label}\" deleted by {User} (no appointments).", TenantId, calendar.Label, DisplayName);
        return Ok(new { deleted = true });
    }

    /// <summary>
    /// Which calendar the AI books for a context. A null contextId sets the account-wide fallback.
    /// Owner-only: this is the switch that decides whether the assistant offers appointments at
    /// all, and to whom.
    /// </summary>
    [HttpPut("context-default")]
    public async Task<IActionResult> SetContextDefault([FromBody] ContextDefaultRequest body, CancellationToken ct)
    {
        if (!IsOwner) return StatusCode(StatusCodes.Status403Forbidden,
            new { message = "Only the account owner can decide which calendar the assistant books into." });

        var existing = await _db.ContextDefaults
            .FirstOrDefaultAsync(d => d.TenantId == TenantId && d.TenantContextId == body.TenantContextId, ct);

        if (body.CalendarId is null)
        {
            if (existing != null) _db.ContextDefaults.Remove(existing);
            await _db.SaveChangesAsync(ct);
            return Ok(new { cleared = true });
        }

        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == body.CalendarId && c.TenantId == TenantId && c.Active, ct);
        if (calendar == null) return BadRequest(new { message = "That calendar is not on this account, or is retired." });

        if (existing == null)
        {
            _db.ContextDefaults.Add(new ContextDefault
            {
                TenantId = TenantId,
                TenantContextId = body.TenantContextId,
                CalendarId = body.CalendarId.Value,
            });
        }
        else
        {
            existing.CalendarId = body.CalendarId.Value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { saved = true });
    }

    /// <summary>Shared by create and update so one validation rule cannot drift between them.</summary>
    private static void Apply(CalendarRequest body, StaffCalendar calendar, out string? error)
    {
        error = null;

        if (body.SlotMinutes is { } slot)
        {
            if (slot < 5 || slot > 240) { error = "A slot must be between 5 and 240 minutes."; return; }
            calendar.SlotMinutes = slot;
        }
        if (body.MaxMinutes is { } max)
        {
            if (max < calendar.SlotMinutes || max > 480) { error = "The maximum must be at least one slot and at most 8 hours."; return; }
            calendar.MaxMinutes = max;
        }
        if (body.BufferMinutes is { } buffer)
        {
            if (buffer < 0 || buffer > 120) { error = "A buffer must be between 0 and 120 minutes."; return; }
            calendar.BufferMinutes = buffer;
        }
        if (body.MinLeadMinutes is { } lead)
        {
            if (lead < 0 || lead > 10080) { error = "Notice must be between 0 minutes and a week."; return; }
            calendar.MinLeadMinutes = lead;
        }
        if (body.HorizonDays is { } horizon)
        {
            if (horizon < 1 || horizon > 365) { error = "The booking horizon must be between 1 and 365 days."; return; }
            calendar.HorizonDays = horizon;
        }
        if (body.WeeklyAvailability != null)
        {
            // Validated on the way IN, which is what lets the slot engine treat an unreadable week
            // as "offer nothing" without that ever being a state a person can save by accident.
            if (!SlotEngine.IsValidWeek(body.WeeklyAvailability))
            {
                error = "That working week could not be read, or leaves nothing bookable.";
                return;
            }
            calendar.WeeklyAvailabilityJson = body.WeeklyAvailability;
        }
        if (body.BookingScript != null)
        {
            // Empty string clears it. Validated against the calendar's ceiling AFTER MaxMinutes
            // above, so raising both in one save works in either order.
            if (string.IsNullOrWhiteSpace(body.BookingScript))
            {
                calendar.BookingScriptJson = null;
            }
            else
            {
                var problem = BookingScript.Validate(body.BookingScript, calendar.MaxMinutes, out var script);
                if (problem != null) { error = problem; return; }
                calendar.BookingScriptJson = script.IsEmpty ? null : script.ToJson();
            }
        }
    }

    public sealed class CalendarRequest
    {
        public string? Label { get; set; }
        public Guid? OwnerUserId { get; set; }
        public int? SlotMinutes { get; set; }
        public int? MaxMinutes { get; set; }
        public int? BufferMinutes { get; set; }
        public int? MinLeadMinutes { get; set; }
        public int? HorizonDays { get; set; }
        public string? WeeklyAvailability { get; set; }
        /// <summary>The booking script as JSON; "" clears it. See BookingScript.</summary>
        public string? BookingScript { get; set; }
        public bool? Active { get; set; }
    }

    public sealed class ContextDefaultRequest
    {
        public Guid? TenantContextId { get; set; }
        public Guid? CalendarId { get; set; }
    }
}
