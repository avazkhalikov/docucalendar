using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using DocuCalendar.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// The sites that may embed this account's calendar and be returned to after a provider sign-in.
/// Docurest pushes the list whenever somebody opens the calendar, because Docurest is the only
/// system that knows which white-label hosts exist.
/// </summary>
[ApiController]
[Route("api/embed-hosts")]
public sealed class EmbedHostsController : ControllerBase
{
    private readonly CalendarDbContext _db;
    private readonly ILogger<EmbedHostsController> _logger;

    public EmbedHostsController(CalendarDbContext db, ILogger<EmbedHostsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Replaces the account's list — Docurest is the authority on which hosts are ours.</summary>
    [HttpPut]
    [RequireApiKey]
    public async Task<IActionResult> Replace([FromBody] HostsRequest body, CancellationToken ct)
    {
        var tenant = HttpContext.Tenant();

        var wanted = (body.Hosts ?? new List<string>())
            .Select(Normalise)
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await _db.EmbedHosts.Where(h => h.TenantId == tenant.TenantId).ToListAsync(ct);
        _db.EmbedHosts.RemoveRange(existing);
        foreach (var host in wanted)
            _db.EmbedHosts.Add(new EmbedHost { TenantId = tenant.TenantId, Host = host });

        await _db.SaveChangesAsync(ct);
        if (!existing.Select(e => e.Host).OrderBy(h => h, StringComparer.Ordinal)
                .SequenceEqual(wanted.OrderBy(h => h, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[EmbedHosts] {Tenant}: {Count} host(s) — {Hosts}.",
                tenant.TenantId, wanted.Count, wanted.Count == 0 ? "none" : string.Join(", ", wanted));
        }
        return Ok(new { count = wanted.Count });
    }

    /// <summary>
    /// A host as the browser reports it. Accepts a full URL too, because callers are sloppy and a
    /// list half in one form and half in the other would silently fail to match.
    /// </summary>
    public static string Normalise(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)) return uri.Host.ToLowerInvariant();
        return text.TrimEnd('/').ToLowerInvariant();
    }

    public sealed class HostsRequest
    {
        public List<string>? Hosts { get; set; }
    }
}
