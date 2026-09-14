using System.Security.Claims;
using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using DocuCalendar.WebApi.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// Arriving from Docurest, and knowing who you are once here. There is no password on this site
/// by design: the only way in is a signed, short-lived token minted by Docurest for a user it has
/// already authenticated.
/// </summary>
[ApiController]
[Route("api/session")]
public sealed class SessionController : ControllerBase
{
    private readonly CalendarDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<SessionController> _logger;

    public SessionController(CalendarDbContext db, IConfiguration config, ILogger<SessionController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// The door. Verifies the token, remembers the account if this is its first visit, issues the
    /// session cookie, and sends the browser to the app. A failure lands on the SPA's own error
    /// route rather than showing raw JSON to someone who just clicked a link.
    /// </summary>
    [HttpGet("sso")]
    [AllowAnonymous]
    public async Task<IActionResult> Sso([FromQuery] string? t, [FromQuery] string? next, CancellationToken ct)
    {
        var secret = _config["Docurest:SsoSecret"] ?? string.Empty;
        var identity = SsoToken.Verify(t, secret, DateTimeOffset.UtcNow);
        if (identity == null)
        {
            _logger.LogWarning("[SSO] Rejected a sign-in token.");
            return Redirect(UiPaths.Ui("/?sso=failed"));
        }

        // First arrival from an account nobody has provisioned yet: register it now so the person
        // can start setting up calendars instead of hitting a wall they cannot fix themselves.
        var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.TenantId == identity.TenantId, ct);
        if (tenant == null)
        {
            tenant = new TenantRegistration
            {
                TenantId = identity.TenantId,
                Name = identity.AccountName ?? identity.TenantId,
                ApiKeyHash = string.Empty, // no booking key until Docurest provisions one
                TimeZoneId = string.IsNullOrWhiteSpace(identity.TimeZoneId) ? "Asia/Tashkent" : identity.TimeZoneId!,
            };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("[SSO] Registered account {Tenant} on first sign-in.", identity.TenantId);
        }
        else if (!string.IsNullOrWhiteSpace(identity.TimeZoneId) && tenant.TimeZoneId != identity.TimeZoneId)
        {
            // Docurest owns the clock; if it changed there, it changes here.
            tenant.TimeZoneId = identity.TimeZoneId!;
            tenant.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var claims = new List<Claim>
        {
            new("tenantId", identity.TenantId),
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(ClaimTypes.Name, identity.Name),
            new(ClaimTypes.Role, identity.IsOwner ? "owner" : "operator"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        // A local path only: an open redirect on the sign-in door would be a phishing kit. The
        // path is one of ours ("/calendars?connected=google"), so it is placed under the UI
        // prefix on whichever host this request arrived at.
        var landing = next is { Length: > 1 and < 400 } && next.StartsWith('/') && !next.StartsWith("//") && !next.Contains('\\')
            ? next
            : "/";
        return Redirect(UiPaths.Ui(landing));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var tenantId = User.FindFirst("tenantId")?.Value ?? string.Empty;
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        return Ok(new
        {
            tenantId,
            accountName = tenant?.Name ?? tenantId,
            timeZoneId = tenant?.TimeZoneId ?? "Asia/Tashkent",
            userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            name = User.Identity?.Name,
            role = User.FindFirst(ClaimTypes.Role)?.Value ?? "operator",
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { signedOut = true });
    }
}
