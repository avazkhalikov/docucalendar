namespace DocuCalendar.Domain.Entities;

/// <summary>
/// A site allowed to embed this calendar and to be returned to when a provider sign-in ends —
/// the account's own white-label portal ("ai-assistant.wiut.uz"), or the main app.
///
/// Pushed here by Docurest, which owns the list of white labels; this service never reaches back
/// for it. Host only, no scheme: what the browser calls the site. The rule this table exists to
/// enforce is small and important — a redirect to an arbitrary site at the end of a sign-in is a
/// phishing kit, so the return address is checked against what Docurest has said is ours.
/// </summary>
public class EmbedHost
{
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Lower-case host, no scheme, no trailing slash: "ai-assistant.wiut.uz".</summary>
    public string Host { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
