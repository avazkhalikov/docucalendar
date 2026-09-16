using DocuCalendar.Application.Scheduling;
using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using DocuCalendar.Infrastructure.Services.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocuCalendar.Infrastructure.Services;

/// <summary>
/// What happens to a request after it is made: the owner confirms or declines it, or nobody
/// does and it lapses an hour before its time. Each outcome is told to Docurest, which texts
/// the caller. A confirmed request is pushed to the person's Outlook or Google like any booking;
/// a declined or lapsed one frees its slot and stays as the record of what was asked.
/// </summary>
public sealed class AppointmentDecisions
{
    private readonly CalendarDbContext _db;
    private readonly SyncScheduler _scheduler;
    private readonly DocurestWebhookSender _webhooks;
    private readonly TokenVault _vault;
    private readonly ILogger<AppointmentDecisions> _logger;

    public AppointmentDecisions(
        CalendarDbContext db,
        SyncScheduler scheduler,
        DocurestWebhookSender webhooks,
        TokenVault vault,
        ILogger<AppointmentDecisions> logger)
    {
        _db = db;
        _scheduler = scheduler;
        _webhooks = webhooks;
        _vault = vault;
        _logger = logger;
    }

    public sealed record Decision(bool Found, bool WasPending, string Status, Appointment? Appointment, StaffCalendar? Calendar);

    /// <summary>Confirms or declines a pending request. Deciding twice, or deciding a booking that was never a request, changes nothing.</summary>
    public async Task<Decision> DecideAsync(string tenantId, Guid appointmentId, bool confirm, string? byName, CancellationToken ct)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenantId, ct);
        if (appointment == null) return new Decision(false, false, string.Empty, null, null);
        var calendar = await _db.Calendars.AsNoTracking().FirstOrDefaultAsync(c => c.Id == appointment.CalendarId, ct);
        if (appointment.Status != PendingRules.Pending || calendar == null)
            return new Decision(true, false, appointment.Status, appointment, calendar);

        appointment.Status = confirm ? PendingRules.Confirmed : PendingRules.Declined;
        appointment.DecidedAt = DateTimeOffset.UtcNow;
        appointment.DecidedByName = string.IsNullOrWhiteSpace(byName) ? null : byName!.Trim();
        await _db.SaveChangesAsync(ct);

        // A confirmed request is a booking now: into the person's real calendar within seconds.
        if (confirm) _scheduler.Nudge(appointment.CalendarId);

        _logger.LogInformation("[Requests] {Tenant}: {Visitor} at {Start:u} on \"{Label}\" {Outcome} by {By}.",
            tenantId, appointment.VisitorName, appointment.StartsAt, calendar.Label, appointment.Status, appointment.DecidedByName ?? "-");

        await TellDocurestAsync(tenantId, calendar, appointment, appointment.Status, ct);
        return new Decision(true, true, appointment.Status, appointment, calendar);
    }

    /// <summary>Requests nobody decided on in time. Run every minute by the sync worker.</summary>
    public async Task<int> ExpireAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc + PendingRules.ExpiryLead;
        var lapsed = await _db.Appointments
            .Where(a => a.Status == PendingRules.Pending && a.StartsAt <= cutoff)
            .ToListAsync(ct);
        if (lapsed.Count == 0) return 0;

        foreach (var appointment in lapsed)
        {
            appointment.Status = PendingRules.Expired;
            appointment.DecidedAt = nowUtc;
        }
        await _db.SaveChangesAsync(ct);

        foreach (var appointment in lapsed)
        {
            var calendar = await _db.Calendars.AsNoTracking().FirstOrDefaultAsync(c => c.Id == appointment.CalendarId, ct);
            if (calendar == null) continue;
            _logger.LogInformation("[Requests] {Tenant}: request from {Visitor} for {Start:u} on \"{Label}\" lapsed undecided.",
                appointment.TenantId, appointment.VisitorName, appointment.StartsAt, calendar.Label);
            await TellDocurestAsync(appointment.TenantId, calendar, appointment, PendingRules.Expired, ct);
        }
        return lapsed.Count;
    }

    private async Task TellDocurestAsync(string tenantId, StaffCalendar calendar, Appointment appointment, string eventName, CancellationToken ct)
    {
        try
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
            var signingKey = tenant == null ? null : _vault.Unprotect(tenant.ApiKeyProtected);
            if (tenant == null || signingKey == null)
            {
                _logger.LogWarning("[Requests] {Tenant}: no signing key held — Docurest was not told that a request was {Event}.", tenantId, eventName);
                return;
            }
            await _webhooks.SendAppointmentEventAsync(tenant, calendar, appointment, signingKey, eventName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[Requests] {Tenant}: could not tell Docurest that a request was {Event}.", tenantId, eventName);
        }
    }
}
