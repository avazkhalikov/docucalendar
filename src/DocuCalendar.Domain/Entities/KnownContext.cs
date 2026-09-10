namespace DocuCalendar.Domain.Entities;

/// <summary>
/// A Docurest knowledge context, as this service knows it: an id and the domain a person would
/// recognise ("intranet.wiut.uz").
///
/// Pushed here by Docurest rather than fetched from it — a push needs no credentials pointing back
/// the other way, and the owner mapping calendars to contexts sees names instead of pasting GUIDs.
/// The list is a convenience for the UI; nothing in booking depends on it being fresh.
/// </summary>
public class KnownContext
{
    public string TenantId { get; set; } = string.Empty;
    public Guid TenantContextId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
