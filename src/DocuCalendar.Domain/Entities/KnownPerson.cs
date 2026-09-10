namespace DocuCalendar.Domain.Entities;

/// <summary>
/// Somebody on the account, as this service knows them: the id a calendar belongs to, and the name
/// a human recognises.
///
/// Pushed here by Docurest, exactly like <see cref="KnownContext"/>. It exists for one reason: the
/// owner setting up calendars for their staff should pick a colleague from a list, not paste a
/// GUID that Docurest never displays anywhere. The first version of this screen asked for the
/// identifier and was, correctly, met with "how is this supposed to work?".
/// </summary>
public class KnownPerson
{
    public string TenantId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"owner" | "operator" — shown beside the name so the list reads like the team.</summary>
    public string Role { get; set; } = "operator";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
