namespace DocuCalendar.Domain.Entities;

/// <summary>
/// One staff calendar hooked up to the person's real calendar — Outlook 365 or Google. From the
/// moment this row exists, the two are kept in step every few minutes: what is busy there is busy
/// here, and what the assistant books here appears there.
///
/// Tokens are stored protected, never plain. A stolen database must not hand over the ability to
/// read anyone's mail calendar.
/// </summary>
public class ExternalConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The staff calendar this feeds. One connection per calendar.</summary>
    public Guid CalendarId { get; set; }

    /// <summary>"microsoft" | "google" — the provider key, which is also what mirrored busy blocks
    /// carry in <see cref="BusyBlock.Source"/>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Which account signed in. Shown in the UI so a wrong-account connection is obvious.</summary>
    public string AccountEmail { get; set; } = string.Empty;
    public string? AccountName { get; set; }

    public string RefreshTokenProtected { get; set; } = string.Empty;
    public string? AccessTokenProtected { get; set; }
    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// "connected" — syncing normally.
    /// "reconnect" — the refresh token is dead (access revoked, or aged out); the person must sign in again.
    /// "error" — the last run failed for a reason that may pass; retried next tick.
    /// </summary>
    public string Status { get; set; } = "connected";

    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }
    public int LastPulled { get; set; }
    public int LastPushed { get; set; }

    public Guid ConnectedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
