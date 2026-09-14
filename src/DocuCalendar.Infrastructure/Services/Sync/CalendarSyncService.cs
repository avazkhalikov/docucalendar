using DocuCalendar.Application.Sync;
using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocuCalendar.Infrastructure.Services.Sync;

public sealed record SyncRunResult(bool Ok, string? Error, int Pulled, int Pushed, int Moved, int Cancelled, string Status)
{
    public static SyncRunResult Skipped(string why, string status) => new(false, why, 0, 0, 0, 0, status);
}

/// <summary>
/// One sync run for one connected calendar: refresh the token, pull the remote window, plan the
/// changes, apply them, push what is new. The planning is elsewhere and pure; this class is the
/// part that talks to the database and the provider, and records honestly how it went.
/// </summary>
public sealed class CalendarSyncService
{
    private readonly CalendarDbContext _db;
    private readonly IEnumerable<ICalendarProvider> _providers;
    private readonly TokenVault _vault;
    private readonly SyncScheduler _scheduler;
    private readonly DocurestWebhookSender _webhooks;
    private readonly ILogger<CalendarSyncService> _logger;

    public CalendarSyncService(
        CalendarDbContext db,
        IEnumerable<ICalendarProvider> providers,
        TokenVault vault,
        SyncScheduler scheduler,
        DocurestWebhookSender webhooks,
        ILogger<CalendarSyncService> logger)
    {
        _db = db;
        _providers = providers;
        _vault = vault;
        _scheduler = scheduler;
        _webhooks = webhooks;
        _logger = logger;
    }

    public ICalendarProvider? ProviderFor(string key) =>
        _providers.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Calendars worth visiting on the timer: connected or in a passing error, never
    /// those waiting for a person to reconnect.</summary>
    public Task<List<Guid>> ListSyncableCalendarsAsync(CancellationToken ct) =>
        _db.ExternalConnections.AsNoTracking()
            .Where(c => c.Status != "reconnect")
            .Select(c => c.CalendarId)
            .ToListAsync(ct);

    /// <summary>
    /// Runs one sync, under two locks. The in-process one keeps a nudge and a tick in this
    /// process from colliding. The database one matters more: the blue and green slots BOTH run
    /// this worker against the same database, and without it each would push the same new
    /// appointment to Outlook once — two events for one visitor. A Postgres advisory lock is held
    /// for the length of the run and released by the server itself if the process dies.
    /// <paramref name="waitForLock"/> is for the "Sync now" button: a person pressing it deserves
    /// a result, not "try again", so it waits briefly for a running sync to finish.
    /// </summary>
    public async Task<SyncRunResult> SyncCalendarAsync(Guid calendarId, CancellationToken ct, bool waitForLock = false)
    {
        var gate = _scheduler.LockFor(calendarId);
        await gate.WaitAsync(ct);
        try
        {
            // One connection pinned for the whole run: a session-level advisory lock is only held
            // by the session that took it, and EF would otherwise hand each query to the pool.
            await _db.Database.OpenConnectionAsync(ct);
            try
            {
                var key = AdvisoryKey(calendarId);
                var deadline = DateTimeOffset.UtcNow.AddSeconds(waitForLock ? 15 : 0);
                while (!await TryAdvisoryLockAsync(key, ct))
                {
                    if (DateTimeOffset.UtcNow >= deadline)
                        return SyncRunResult.Skipped("Another sync of this calendar is already running — try again in a moment.", "busy");
                    await Task.Delay(500, ct);
                }

                try
                {
                    return await SyncLockedAsync(calendarId, ct);
                }
                finally
                {
                    await ReleaseAdvisoryLockAsync(key);
                }
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static long AdvisoryKey(Guid calendarId)
    {
        var bytes = calendarId.ToByteArray();
        return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
    }

    private async Task<bool> TryAdvisoryLockAsync(long key, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
        var p = cmd.CreateParameter();
        p.ParameterName = "key";
        p.Value = key;
        cmd.Parameters.Add(p);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    private async Task ReleaseAdvisoryLockAsync(long key)
    {
        try
        {
            var connection = _db.Database.GetDbConnection();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_advisory_unlock(@key)";
            var p = cmd.CreateParameter();
            p.ParameterName = "key";
            p.Value = key;
            cmd.Parameters.Add(p);
            await cmd.ExecuteScalarAsync();
        }
        catch (Exception ex)
        {
            // Closing the connection releases it anyway; this only tidies up early.
            _logger.LogDebug(ex, "[Sync] Advisory unlock failed; the connection close will release it.");
        }
    }

    private async Task<SyncRunResult> SyncLockedAsync(Guid calendarId, CancellationToken ct)
    {
        var conn = await _db.ExternalConnections.FirstOrDefaultAsync(c => c.CalendarId == calendarId, ct);
        if (conn == null) return SyncRunResult.Skipped("Not connected.", "none");

        var calendar = await _db.Calendars.AsNoTracking().FirstOrDefaultAsync(c => c.Id == calendarId, ct);
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == conn.TenantId, ct);
        if (calendar == null || tenant == null) return SyncRunResult.Skipped("Calendar not found.", conn.Status);

        var provider = ProviderFor(conn.Provider);
        if (provider == null || !provider.Configured)
            return await FailAsync(conn, "error", $"{conn.Provider} is not set up on this server.", ct);

        if (conn.Status == "reconnect")
            return SyncRunResult.Skipped(conn.LastSyncError ?? "Please reconnect.", "reconnect");

        var moved = new List<(Appointment Appointment, DateTimeOffset PreviousStart)>();
        var cancelled = new List<Appointment>();
        int pulled = 0, pushed = 0;

        try
        {
            var access = await EnsureAccessTokenAsync(conn, provider, ct);
            var zone = TenantService.ZoneOf(tenant);
            var now = DateTimeOffset.UtcNow;
            var from = now.AddDays(-1);
            var to = now.AddDays(calendar.HorizonDays + 1);

            // ---- Pull ----
            var remote = await provider.ListEventsAsync(access, from, to, zone, ct);

            var blocks = await _db.BusyBlocks
                .Where(b => b.CalendarId == calendarId && b.Source == provider.Key && b.EndsAt > from && b.StartsAt < to)
                .ToListAsync(ct);
            var linked = await _db.Appointments
                .Where(a => a.CalendarId == calendarId && a.Status == "confirmed" && a.ExternalEventId != null
                            && a.ExternalProvider == provider.Key && a.EndsAt > from && a.StartsAt < to)
                .ToListAsync(ct);

            // "Busy mirrored" as the UI reports it: the person's own events, not the copies we put
            // there ourselves — those came back in the listing too and were being counted.
            var ours = linked.Select(a => a.ExternalEventId!).ToHashSet(StringComparer.Ordinal);
            pulled = remote.Count(e => e.IsBusy && !e.IsCancelled && !ours.Contains(e.Id));

            var plan = SyncPlanner.Plan(
                remote,
                blocks.Select(b => new MirroredBlock(b.Id, b.ExternalId ?? string.Empty, b.StartsAt, b.EndsAt, b.Reason)).ToList(),
                linked.Select(a => new LinkedAppointment(a.Id, a.ExternalEventId!, a.StartsAt, a.EndsAt, a.ExternalSyncedAt)).ToList(),
                now);

            // ---- Apply ----
            foreach (var e in plan.BlocksToAdd)
            {
                _db.BusyBlocks.Add(new BusyBlock
                {
                    CalendarId = calendarId,
                    StartsAt = e.StartUtc,
                    EndsAt = e.EndUtc,
                    Reason = Clip(e.Subject, 300),
                    Source = provider.Key,
                    ExternalId = e.Id,
                });
            }
            var blocksById = blocks.ToDictionary(b => b.Id);
            foreach (var u in plan.BlocksToUpdate)
            {
                if (!blocksById.TryGetValue(u.BlockId, out var block)) continue;
                block.StartsAt = u.Event.StartUtc;
                block.EndsAt = u.Event.EndUtc;
                block.Reason = Clip(u.Event.Subject, 300);
            }
            foreach (var id in plan.BlocksToRemove)
                if (blocksById.TryGetValue(id, out var block)) _db.BusyBlocks.Remove(block);

            var linkedById = linked.ToDictionary(a => a.Id);
            foreach (var m in plan.AppointmentsToMove)
            {
                if (!linkedById.TryGetValue(m.AppointmentId, out var a)) continue;
                var previous = a.StartsAt;
                a.StartsAt = m.NewStartUtc;
                a.EndsAt = m.NewEndUtc;
                a.ExternalSyncedAt = now;
                moved.Add((a, previous));
            }
            foreach (var id in plan.AppointmentsToCancel)
            {
                if (!linkedById.TryGetValue(id, out var a)) continue;
                a.Status = "cancelled";
                a.CancelledAt = now;
                a.CancelledByName = $"{provider.DisplayName} ({conn.AccountEmail})";
                a.ExternalEventId = null; // already gone over there; nothing to delete later
                cancelled.Add(a);
            }
            await _db.SaveChangesAsync(ct);

            // ---- Push ----
            var toPush = await _db.Appointments
                .Where(a => a.CalendarId == calendarId && a.Status == "confirmed" && a.ExternalEventId == null
                            && a.StartsAt > now.AddHours(-1))
                .OrderBy(a => a.StartsAt)
                .ToListAsync(ct);
            foreach (var a in toPush)
            {
                var draft = new RemoteEventDraft(
                    Subject: $"Appointment: {a.VisitorName}",
                    Body: BuildBody(a, calendar),
                    StartUtc: a.StartsAt,
                    EndUtc: a.EndsAt);
                var remoteId = await provider.CreateEventAsync(access, draft, ct);
                a.ExternalProvider = provider.Key;
                a.ExternalEventId = remoteId;
                a.ExternalSyncedAt = DateTimeOffset.UtcNow;
                pushed++;
                await _db.SaveChangesAsync(ct); // one at a time: a failure mid-list must not forget the ids already minted
            }

            var toDelete = await _db.Appointments
                .Where(a => a.CalendarId == calendarId && a.Status == "cancelled" && a.ExternalEventId != null
                            && a.ExternalProvider == provider.Key)
                .ToListAsync(ct);
            foreach (var a in toDelete)
            {
                if (await provider.DeleteEventAsync(access, a.ExternalEventId!, ct))
                {
                    a.ExternalEventId = null;
                    await _db.SaveChangesAsync(ct);
                }
            }

            conn.Status = "connected";
            conn.LastSyncAt = DateTimeOffset.UtcNow;
            conn.LastSyncError = null;
            conn.LastPulled = pulled;
            conn.LastPushed = pushed;
            conn.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            if (!plan.IsEmpty || pushed > 0)
                _logger.LogInformation(
                    "[Sync] {Tenant}/{Calendar} via {Provider}: +{Added} ~{Updated} -{Removed} busy, {Moved} moved, {Cancelled} cancelled, {Pushed} pushed.",
                    conn.TenantId, calendar.Label, provider.Key, plan.BlocksToAdd.Count, plan.BlocksToUpdate.Count,
                    plan.BlocksToRemove.Count, moved.Count, cancelled.Count, pushed);
        }
        catch (ProviderAuthException ex)
        {
            return await FailAsync(conn, "reconnect", ex.Message, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Sync] {Tenant}/{Calendar} via {Provider} failed.", conn.TenantId, calendar.Label, conn.Provider);
            return await FailAsync(conn, "error", Clip(ex.Message, 500) ?? "Sync failed.", ct);
        }

        // Announce after the commit — a notification about a change that then rolled back would
        // be worse than silence.
        var via = $"{provider.DisplayName} ({conn.AccountEmail})";
        var signingKey = _vault.Unprotect(tenant.ApiKeyProtected);
        if (signingKey == null && (moved.Count > 0 || cancelled.Count > 0))
            _logger.LogWarning("[Sync] {Tenant}: appointments changed from {Provider} but no signing key is held yet — Docurest was not told. It is captured on the next booking call.",
                conn.TenantId, provider.Key);
        if (signingKey != null)
        {
            foreach (var (appointment, previous) in moved)
                await _webhooks.SendAppointmentEventAsync(tenant, calendar, appointment, signingKey, "moved", previous, via);
            foreach (var appointment in cancelled)
                await _webhooks.SendAppointmentEventAsync(tenant, calendar, appointment, signingKey, "cancelled", null, via);
        }

        return new SyncRunResult(true, null, pulled, pushed, moved.Count, cancelled.Count, "connected");
    }

    private async Task<string> EnsureAccessTokenAsync(ExternalConnection conn, ICalendarProvider provider, CancellationToken ct)
    {
        var access = _vault.Unprotect(conn.AccessTokenProtected);
        if (access != null && conn.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return access;

        var refresh = _vault.Unprotect(conn.RefreshTokenProtected)
                      ?? throw new ProviderAuthException("The stored connection could not be read — please reconnect.");

        var tokens = await provider.RefreshAsync(refresh, ct);
        conn.AccessTokenProtected = _vault.Protect(tokens.AccessToken);
        conn.AccessTokenExpiresAt = tokens.ExpiresAtUtc;
        // Some providers rotate the refresh token on every use; keep whichever is newest.
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
            conn.RefreshTokenProtected = _vault.Protect(tokens.RefreshToken);
        conn.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return tokens.AccessToken;
    }

    private async Task<SyncRunResult> FailAsync(ExternalConnection conn, string status, string error, CancellationToken ct)
    {
        conn.Status = status;
        conn.LastSyncError = error;
        conn.LastSyncAt = DateTimeOffset.UtcNow;
        conn.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new SyncRunResult(false, error, 0, 0, 0, 0, status);
    }

    private static string BuildBody(Appointment a, StaffCalendar calendar)
    {
        var lines = new List<string>
        {
            $"Visitor: {a.VisitorName}",
            $"Phone: {a.VisitorPhone}",
        };
        if (!string.IsNullOrWhiteSpace(a.Topic)) lines.Add($"About: {a.Topic}");
        lines.Add($"Calendar: {calendar.Label}");
        lines.Add(a.Channel switch
        {
            "phone" => "Booked by the assistant on the phone.",
            "chat" => "Booked by the assistant in chat.",
            _ => "Booked by a member of staff.",
        });
        lines.Add("");
        lines.Add("Move or delete this event and the appointment in DocuCalendar follows.");
        return string.Join('\n', lines);
    }

    private static string? Clip(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length > max ? s[..max] : s);
}
