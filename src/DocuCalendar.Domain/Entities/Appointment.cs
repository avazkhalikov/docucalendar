namespace DocuCalendar.Domain.Entities;

/// <summary>
/// A booked meeting. Written by the AI on a caller's behalf, or by staff by hand; either way it
/// occupies the calendar exactly as a busy block does, so the next visitor is never offered a time
/// somebody already took.
/// </summary>
public class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CalendarId { get; set; }
    public string TenantId { get; set; } = string.Empty;

    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Who is coming. The AI must collect this before booking — an anonymous appointment
    /// is a meeting nobody can confirm, chase, or cancel.</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>How to reach them. Read back to the caller for confirmation before the booking.</summary>
    public string VisitorPhone { get; set; } = string.Empty;

    public string? Topic { get; set; }

    /// <summary>"phone" | "chat" | "manual" — where the booking came from.</summary>
    public string Channel { get; set; } = "manual";

    /// <summary>The call's trace id or the conversation id, so an appointment can be traced back
    /// to the exact conversation that produced it.</summary>
    public string? SourceRef { get; set; }

    /// <summary>"confirmed" | "cancelled". Cancelled rows stay: they are the record that the slot
    /// was taken and released, and deleting them would erase a visitor's history.</summary>
    public string Status { get; set; } = "confirmed";

    public string? CancelledByName { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>
    /// The copy of this appointment in the person's Outlook or Google calendar, once the sync has
    /// pushed it. Null until then, and cleared again when the remote copy is deleted — so "has a
    /// remote id" is exactly "exists over there".
    /// </summary>
    public string? ExternalProvider { get; set; }
    public string? ExternalEventId { get; set; }
    public DateTimeOffset? ExternalSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
