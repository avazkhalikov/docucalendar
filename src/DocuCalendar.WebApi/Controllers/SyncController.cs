using System.Security.Cryptography;
using DocuCalendar.Application.Sync;
using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using DocuCalendar.Infrastructure.Options;
using DocuCalendar.Infrastructure.Services.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// Hooking a staff calendar up to the person's real one — Outlook 365 or Google — and looking
/// after that link afterwards. The OAuth round trip starts and ends here; the syncing itself
/// happens in the background and is only reported on.
/// </summary>
[ApiController]
[Route("api/sync")]
public sealed class SyncController : StaffControllerBase
{
    private readonly CalendarDbContext _db;
    private readonly IEnumerable<ICalendarProvider> _providers;
    private readonly SyncStateProtector _state;
    private readonly TokenVault _vault;
    private readonly SyncScheduler _scheduler;
    private readonly CalendarSyncService _sync;
    private readonly SyncOptions _options;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        CalendarDbContext db,
        IEnumerable<ICalendarProvider> providers,
        SyncStateProtector state,
        TokenVault vault,
        SyncScheduler scheduler,
        CalendarSyncService sync,
        IOptions<SyncOptions> options,
        ILogger<SyncController> logger)
    {
        _db = db;
        _providers = providers;
        _state = state;
        _vault = vault;
        _scheduler = scheduler;
        _sync = sync;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Which providers this server can offer. An unconfigured one is shown greyed out,
    /// not hidden — people should know the option exists and who to ask.</summary>
    [HttpGet("providers")]
    public IActionResult Providers() =>
        Ok(new
        {
            providers = _providers.Select(p => new { key = p.Key, displayName = p.DisplayName, configured = p.Configured }),
        });

    [HttpGet("connections")]
    public async Task<IActionResult> Connections(CancellationToken ct)
    {
        var rows = await _db.ExternalConnections.AsNoTracking()
            .Where(c => c.TenantId == TenantId)
            .ToListAsync(ct);

        return Ok(new
        {
            connections = rows.Select(c => new
            {
                c.CalendarId,
                c.Provider,
                displayName = _providers.FirstOrDefault(p => p.Key == c.Provider)?.DisplayName ?? c.Provider,
                c.AccountEmail,
                c.Status,
                c.LastSyncAt,
                c.LastSyncError,
                c.LastPulled,
                c.LastPushed,
            }),
        });
    }

    /// <summary>
    /// Step one of connecting: off to the provider. Only the person whose calendar it is may do
    /// this — the account that signs in at Microsoft or Google will be THEIRS, and the owner
    /// connecting on an operator's row would bind the owner's own mailbox to somebody else's day.
    /// </summary>
    [HttpGet("{provider}/connect")]
    public async Task<IActionResult> Connect(string provider, [FromQuery] Guid calendarId, [FromQuery] string? returnTo, CancellationToken ct)
    {
        var p = Find(provider);
        if (p == null) return NotFound(new { message = "Unknown calendar provider." });
        if (!p.Configured)
            return BadRequest(new { message = $"{p.DisplayName} is not set up on this server yet — ask the administrator." });

        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == calendarId && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (calendar.OwnerUserId != UserId)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = $"Only the person whose calendar this is can connect their own {p.DisplayName}. Ask them to open DocuCalendar and press Connect.",
            });

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var state = _state.Protect(new SyncState(TenantId, UserId, calendar.Id, p.Key, nonce, AllowedReturn(returnTo)));
        return Redirect(p.BuildAuthorizeUrl(state, RedirectUriFor(p.Key)));
    }

    /// <summary>The embedding site's origin, but only if it is one of ours.</summary>
    private string? AllowedReturn(string? returnTo)
    {
        if (string.IsNullOrWhiteSpace(returnTo)) return null;
        var candidate = returnTo.Trim().TrimEnd('/');
        return _options.EmbedHosts
            .Select(h => h.Trim().TrimEnd('/'))
            .FirstOrDefault(h => string.Equals(h, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Where the browser goes when the provider round trip ends. Started inside Docurest → back
    /// to Docurest's calendar page, which re-embeds this site at the given path; started here →
    /// the path itself.
    /// </summary>
    private static string Landing(SyncState? state, string localPath) =>
        state?.ReturnTo is { Length: > 0 } origin
            ? $"{origin}/app/calendar?next={Uri.EscapeDataString(localPath)}"
            : localPath;

    /// <summary>
    /// Step two: back from the provider. Anonymous by necessity — the browser may arrive without
    /// our cookie — so nothing in the query is trusted except what the protected state says.
    /// Every failure lands on the Calendars page with a reason the person can act on.
    /// </summary>
    [HttpGet("{provider}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var p = Find(provider);
        if (p == null) return Redirect("/calendars?connectError=provider");
        if (!string.IsNullOrEmpty(error)) return Redirect("/calendars?connectError=denied");

        var s = _state.Unprotect(state);
        if (s == null || !string.Equals(s.Provider, p.Key, StringComparison.Ordinal))
            return Redirect("/calendars?connectError=expired");
        if (string.IsNullOrEmpty(code)) return Redirect(Landing(s, "/calendars?connectError=denied"));

        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == s.CalendarId && c.TenantId == s.TenantId, ct);
        if (calendar == null) return Redirect(Landing(s, "/calendars?connectError=missing"));

        try
        {
            var tokens = await p.ExchangeCodeAsync(code, RedirectUriFor(p.Key), ct);
            if (string.IsNullOrEmpty(tokens.RefreshToken))
            {
                // Without a refresh token the connection would die within the hour. Google withholds
                // one when consent was not re-asked; the authorize URL asks for it every time, so
                // this is rare — but silent death is not an acceptable failure mode.
                _logger.LogWarning("[Sync] {Provider} returned no refresh token for {Tenant}/{Calendar}.", p.Key, s.TenantId, calendar.Label);
                return Redirect(Landing(s, "/calendars?connectError=norefresh"));
            }

            var account = await p.GetAccountAsync(tokens.AccessToken, ct);

            var conn = await _db.ExternalConnections.FirstOrDefaultAsync(c => c.CalendarId == calendar.Id, ct);
            if (conn != null && conn.Provider != p.Key)
            {
                // Switching provider: the old mirror is meaningless now. The events we pushed to
                // the old calendar stay — they are the person's, and deleting them uninvited is
                // exactly the kind of surprise a sync must never spring.
                await ForgetMirrorAsync(calendar.Id, conn.Provider, ct);
            }
            if (conn == null)
            {
                conn = new ExternalConnection { TenantId = s.TenantId, CalendarId = calendar.Id };
                _db.ExternalConnections.Add(conn);
            }

            conn.Provider = p.Key;
            conn.AccountEmail = account.Email;
            conn.AccountName = account.DisplayName;
            conn.RefreshTokenProtected = _vault.Protect(tokens.RefreshToken);
            conn.AccessTokenProtected = _vault.Protect(tokens.AccessToken);
            conn.AccessTokenExpiresAt = tokens.ExpiresAtUtc;
            conn.Status = "connected";
            conn.LastSyncError = null;
            conn.ConnectedByUserId = s.UserId;
            conn.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _scheduler.Nudge(calendar.Id);
            _logger.LogInformation("[Sync] {Tenant}: \"{Label}\" connected to {Provider} as {Email}.",
                s.TenantId, calendar.Label, p.Key, account.Email);
            return Redirect(Landing(s, $"/calendars?connected={p.Key}"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Sync] Connecting {Provider} failed for {Tenant}/{Calendar}.", p.Key, s.TenantId, calendar.Label);
            return Redirect(Landing(s, "/calendars?connectError=exchange"));
        }
    }

    /// <summary>Runs one sync inline and reports how it went — the "is it working?" button.</summary>
    [HttpPost("connections/{calendarId:guid}/sync-now")]
    public async Task<IActionResult> SyncNow(Guid calendarId, CancellationToken ct)
    {
        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == calendarId && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (!CanManage(calendar)) return NotYours();

        var result = await _sync.SyncCalendarAsync(calendarId, ct, waitForLock: true);
        return Ok(result);
    }

    /// <summary>
    /// Undoes the link. Mirrored busy time goes; the events we created in the person's calendar
    /// stay (they are theirs now); the appointments here simply forget their remote copies.
    /// </summary>
    [HttpPost("connections/{calendarId:guid}/disconnect")]
    public async Task<IActionResult> Disconnect(Guid calendarId, CancellationToken ct)
    {
        var calendar = await _db.Calendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == calendarId && c.TenantId == TenantId, ct);
        if (calendar == null) return NotFound();
        if (!CanManage(calendar)) return NotYours();

        var conn = await _db.ExternalConnections.FirstOrDefaultAsync(c => c.CalendarId == calendarId, ct);
        if (conn == null) return Ok(new { disconnected = true });

        await ForgetMirrorAsync(calendarId, conn.Provider, ct);

        var refresh = _vault.Unprotect(conn.RefreshTokenProtected);
        if (refresh != null && Find(conn.Provider) is { } p)
            await p.RevokeAsync(refresh, ct);

        _db.ExternalConnections.Remove(conn);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[Sync] {Tenant}: \"{Label}\" disconnected from {Provider} by {User}.",
            TenantId, calendar.Label, conn.Provider, DisplayName);
        return Ok(new { disconnected = true });
    }

    private async Task ForgetMirrorAsync(Guid calendarId, string providerKey, CancellationToken ct)
    {
        var blocks = await _db.BusyBlocks.Where(b => b.CalendarId == calendarId && b.Source == providerKey).ToListAsync(ct);
        _db.BusyBlocks.RemoveRange(blocks);

        var linked = await _db.Appointments
            .Where(a => a.CalendarId == calendarId && a.ExternalProvider == providerKey)
            .ToListAsync(ct);
        foreach (var a in linked)
        {
            a.ExternalProvider = null;
            a.ExternalEventId = null;
            a.ExternalSyncedAt = null;
        }
        await _db.SaveChangesAsync(ct);
    }

    private ICalendarProvider? Find(string key) =>
        _providers.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    private string RedirectUriFor(string providerKey)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : _options.PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}/api/sync/{providerKey}/callback";
    }
}
