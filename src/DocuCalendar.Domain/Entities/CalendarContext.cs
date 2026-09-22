namespace DocuCalendar.Domain.Entities;

/// <summary>
/// One calendar serving one Docurest knowledge context. A calendar may have several of these
/// rows, and a context may be served by several calendars.
///
/// This replaces the old one-default-per-context arrangement, which could only ever name a single
/// diary for a whole knowledge base — so an account with a reception desk, a call centre and a
/// director offered all three's callers the same person, and the assistant, asked who could be
/// seen, had exactly one name to give. Booking is offered for a context precisely when at least
/// one row here exists for it, and stops when the last one is removed.
///
/// A calendar with no rows at all is bookable by name but offered to nobody automatically: that
/// is a state the UI should show plainly rather than a fallback to guess around.
/// </summary>
public class CalendarContext
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;

    public Guid CalendarId { get; set; }

    /// <summary>The Docurest knowledge context (never null — "everywhere" is not a context).</summary>
    public Guid TenantContextId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
