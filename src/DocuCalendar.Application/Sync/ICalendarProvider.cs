namespace DocuCalendar.Application.Sync;

/// <summary>An event as the remote calendar reports it, reduced to what scheduling needs.</summary>
public sealed record RemoteEvent(
    string Id,
    string? Subject,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsAllDay,
    /// <summary>False for events marked free / transparent, or ones the person declined — they
    /// sit in the calendar without occupying it.</summary>
    bool IsBusy,
    bool IsCancelled);

public sealed record TokenSet(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc);

public sealed record RemoteAccount(string Email, string? DisplayName);

/// <param name="ShowAsFree">A bookable WINDOW, not an appointment: it must not make the person look busy to colleagues.</param>
public sealed record RemoteEventDraft(string Subject, string Body, DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool ShowAsFree = false);

/// <summary>The provider no longer accepts our credentials — the person has to sign in again.
/// Distinct from a passing failure so the sync can stop retrying something that will never work.</summary>
public sealed class ProviderAuthException : Exception
{
    public ProviderAuthException(string message) : base(message) { }
}

/// <summary>
/// What a calendar provider must be able to do for the sync. Two implementations, one shape, so the
/// sync itself never knows which it is talking to.
/// </summary>
public interface ICalendarProvider
{
    /// <summary>"microsoft" | "google" — stored on connections and mirrored blocks.</summary>
    string Key { get; }

    /// <summary>"Outlook 365" | "Google Calendar" — what people see.</summary>
    string DisplayName { get; }

    /// <summary>False when this server has no client credentials for the provider.</summary>
    bool Configured { get; }

    string BuildAuthorizeUrl(string state, string redirectUri);
    Task<TokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct);
    Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct);
    Task<RemoteAccount> GetAccountAsync(string accessToken, CancellationToken ct);

    /// <summary>Every event overlapping the window, recurrences expanded, all pages — or an exception.
    /// A partial list must never be returned: the caller deletes what it does not see.</summary>
    Task<IReadOnlyList<RemoteEvent>> ListEventsAsync(
        string accessToken, DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeZoneInfo zone, CancellationToken ct);

    /// <summary>Creates the event and returns its remote id.</summary>
    Task<string> CreateEventAsync(string accessToken, RemoteEventDraft draft, CancellationToken ct);

    /// <summary>True when the event is gone afterwards — deleted now, or already deleted.</summary>
    Task<bool> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct);

    /// <summary>Best effort; not every provider offers one.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken ct);
}
