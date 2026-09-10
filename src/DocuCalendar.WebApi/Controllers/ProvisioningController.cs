using DocuCalendar.Infrastructure.Services;
using DocuCalendar.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// How a Docurest account comes to exist here. Called by Docurest with the platform master key,
/// once per account (and again whenever a key needs rotating).
/// </summary>
[ApiController]
[Route("api/tenants")]
[RequireMasterKey]
public sealed class ProvisioningController : ControllerBase
{
    private readonly TenantService _tenants;
    private readonly ILogger<ProvisioningController> _logger;

    public ProvisioningController(TenantService tenants, ILogger<ProvisioningController> logger)
    {
        _tenants = tenants;
        _logger = logger;
    }

    [HttpPost("provision")]
    public async Task<IActionResult> Provision([FromBody] ProvisionRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.TenantId))
            return BadRequest(new { message = "tenantId is required." });

        var apiKey = await _tenants.ProvisionAsync(body.TenantId.Trim(), body.Name ?? body.TenantId, body.TimeZoneId, ct);
        _logger.LogInformation("[Provision] Tenant {Tenant} provisioned (key rotated).", body.TenantId);

        // The key is returned exactly once; only its hash is kept.
        return Ok(new { tenantId = body.TenantId.Trim(), apiKey });
    }

    public sealed class ProvisionRequest
    {
        public string? TenantId { get; set; }
        public string? Name { get; set; }
        public string? TimeZoneId { get; set; }
    }
}
