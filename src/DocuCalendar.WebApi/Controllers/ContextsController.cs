using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using DocuCalendar.WebApi.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// The account's knowledge contexts, kept here only so the owner can map calendars to them by
/// name. Docurest pushes the list; this service never reaches back for it.
/// </summary>
[ApiController]
[Route("api/contexts")]
public sealed class ContextsController : ControllerBase
{
    private readonly CalendarDbContext _db;

    public ContextsController(CalendarDbContext db) => _db = db;

    /// <summary>Replaces the known list — Docurest is the authority on which contexts exist.</summary>
    [HttpPut]
    [RequireApiKey]
    public async Task<IActionResult> Replace([FromBody] List<ContextDto> body, CancellationToken ct)
    {
        var tenant = HttpContext.Tenant();
        var existing = await _db.KnownContexts.Where(k => k.TenantId == tenant.TenantId).ToListAsync(ct);
        _db.KnownContexts.RemoveRange(existing);

        foreach (var item in body.Where(i => i.TenantContextId != Guid.Empty))
        {
            _db.KnownContexts.Add(new KnownContext
            {
                TenantId = tenant.TenantId,
                TenantContextId = item.TenantContextId,
                Domain = string.IsNullOrWhiteSpace(item.Domain) ? item.TenantContextId.ToString() : item.Domain!.Trim(),
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { count = body.Count });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = User.FindFirst("tenantId")?.Value ?? string.Empty;
        var contexts = await _db.KnownContexts.AsNoTracking()
            .Where(k => k.TenantId == tenantId)
            .OrderBy(k => k.Domain)
            .Select(k => new { k.TenantContextId, k.Domain })
            .ToListAsync(ct);
        return Ok(new { contexts });
    }

    public sealed class ContextDto
    {
        public Guid TenantContextId { get; set; }
        public string? Domain { get; set; }
    }
}
