namespace DocuCalendar.Domain.Entities;

/// <summary>
/// Which calendar the AI books into for one Docurest knowledge context — and, when
/// <see cref="TenantContextId"/> is null, for the whole account as a fallback.
///
/// This mapping lives HERE rather than in Docurest on purpose: it means enabling AI booking for a
/// context requires no schema change and no deploy on the Docurest side. The booking tools appear
/// for a context exactly when a row here resolves for it, and disappear when the owner removes it.
/// </summary>
public class ContextDefault
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Null = the account-wide fallback, used when a context has no row of its own.</summary>
    public Guid? TenantContextId { get; set; }

    public Guid CalendarId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
