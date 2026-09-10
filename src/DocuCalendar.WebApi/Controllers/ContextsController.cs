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

/// <summary>
/// The people on the account, so calendars are assigned by picking a colleague rather than by
/// pasting an identifier nobody can see. Docurest pushes the list; this service never asks for it.
/// </summary>
[ApiController]
[Route("api/people")]
public sealed class PeopleController : ControllerBase
{
    private readonly CalendarDbContext _db;

    public PeopleController(CalendarDbContext db) => _db = db;

    [HttpPut]
    [RequireApiKey]
    public async Task<IActionResult> Replace([FromBody] List<PersonDto> body, CancellationToken ct)
    {
        var tenant = HttpContext.Tenant();
        var existing = await _db.KnownPeople.Where(p => p.TenantId == tenant.TenantId).ToListAsync(ct);
        _db.KnownPeople.RemoveRange(existing);

        foreach (var item in body.Where(i => i.UserId != Guid.Empty))
        {
            _db.KnownPeople.Add(new KnownPerson
            {
                TenantId = tenant.TenantId,
                UserId = item.UserId,
                Name = string.IsNullOrWhiteSpace(item.Name) ? item.UserId.ToString() : item.Name!.Trim(),
                Role = string.Equals(item.Role, "owner", StringComparison.OrdinalIgnoreCase) ? "owner" : "operator",
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
        var people = await _db.KnownPeople.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.Role == "owner")
            .ThenBy(p => p.Name)
            .Select(p => new { p.UserId, p.Name, p.Role })
            .ToListAsync(ct);
        return Ok(new { people });
    }

    public sealed class PersonDto
    {
        public Guid UserId { get; set; }
        public string? Name { get; set; }
        public string? Role { get; set; }
    }
}
