using System.Security.Claims;
using DocuCalendar.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocuCalendar.WebApi.Controllers;

/// <summary>
/// Shared ground for everything a signed-in staff member does.
///
/// One rule, stated once: the account owner manages every calendar on the account; an operator
/// manages their own and nobody else's. It is the same shape Docurest uses for operators —
/// their own things yes, the account's plumbing no — and stating it in one place is what keeps a
/// new endpoint from quietly opening a colleague's day to editing.
/// </summary>
[Authorize]
public abstract class StaffControllerBase : ControllerBase
{
    protected string TenantId => User.FindFirst("tenantId")?.Value ?? string.Empty;
    protected Guid UserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;
    protected string DisplayName => User.Identity?.Name ?? "Someone";
    protected bool IsOwner => string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, "owner", StringComparison.OrdinalIgnoreCase);

    protected bool CanManage(StaffCalendar calendar) => IsOwner || calendar.OwnerUserId == UserId;

    protected IActionResult NotYours() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = "That calendar belongs to someone else." });
}
