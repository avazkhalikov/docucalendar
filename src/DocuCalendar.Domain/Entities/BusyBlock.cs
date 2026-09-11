namespace DocuCalendar.Domain.Entities;

/// <summary>
/// Time that is not available, for any reason the calendar's owner does not have to justify:
/// "9/10 09:00–12:00 busy". Subtracted from availability before a single slot is offered, so the
/// AI can never propose a time its owner has already spoken for.
/// </summary>
public class BusyBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CalendarId { get; set; }

    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Optional, and shown only to staff — a visitor is told a time is unavailable, never why.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// "manual" for anything a person entered here; "microsoft" or "google" for time mirrored from
    /// the person's own calendar by the sync. The slot engine does not distinguish — busy is busy.
    /// </summary>
    public string Source { get; set; } = "manual";

    /// <summary>The remote event's id, so the sync can match a mirrored block to its source.</summary>
    public string? ExternalId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
