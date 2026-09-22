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

    /// <summary>
    /// The opposite direction: when a bookable window is created HERE, the sync puts a matching
    /// event in the person's own Outlook or Google (named "Bookable", marked free) and remembers
    /// its id here. That id also tells the pull to skip the event, or we would mirror our own
    /// window back in as a second block. Null for everything that came the other way.
    /// </summary>
    public string? PushedEventId { get; set; }
    public string? PushedProvider { get; set; }
    public DateTimeOffset? PushedAt { get; set; }

    /// <summary>
    /// Shared by every occurrence of a repeating entry — "every Friday, 14:00–17:00" is written
    /// as one row per Friday, all carrying this id.
    ///
    /// Real rows rather than a rule expanded at read time, deliberately: the slot engine, the
    /// grids and the push into the person's own Outlook already understand a row, and none of
    /// them should have to learn recurrence to keep working. The id is what lets the whole series
    /// be shown, and removed, as one thing.
    /// </summary>
    public Guid? SeriesId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
