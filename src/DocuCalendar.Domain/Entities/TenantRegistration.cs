namespace DocuCalendar.Domain.Entities;

/// <summary>
/// One Docurest account, known here by the same tenant id it carries there. Created by Docurest
/// through the provisioning endpoint, never by hand: this service has no user registration of its
/// own, because a calendar that could exist without an account behind it would be an orphan
/// nobody can manage.
/// </summary>
public class TenantRegistration
{
    /// <summary>The Docurest tenant id, verbatim — the join key between the two systems.</summary>
    public string TenantId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the tenant's API key. The key itself is returned ONCE at provisioning and never
    /// stored: a stolen database must not hand over the ability to book on every calendar.
    /// </summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>
    /// The same key, protected, kept for one purpose: signing the webhooks the background sync
    /// sends when Outlook or Google changes an appointment. Those runs have no request to take
    /// the key from, so it is captured — protected — the first time Docurest presents it.
    /// </summary>
    public string? ApiKeyProtected { get; set; }

    /// <summary>
    /// The account's clock, copied from Docurest at provisioning (IANA, e.g. "Asia/Tashkent").
    /// Every hour a human sees or says — availability windows, offered slots, the appointment the
    /// AI reads back to a caller — is rendered in this zone. The database stores UTC only.
    /// </summary>
    public string TimeZoneId { get; set; } = "Asia/Tashkent";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
