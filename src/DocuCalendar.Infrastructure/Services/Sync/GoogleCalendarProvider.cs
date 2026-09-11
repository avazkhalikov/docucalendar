using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocuCalendar.Application.Sync;
using DocuCalendar.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuCalendar.Infrastructure.Services.Sync;

/// <summary>
/// Google Calendar over the Calendar API v3, primary calendar only. The scope asked for is
/// calendar.events — read and write events, nothing about the account beyond the calendar itself.
/// </summary>
public sealed class GoogleCalendarProvider : ICalendarProvider
{
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private const string ApiBase = "https://www.googleapis.com/calendar/v3";
    private const string Scope = "https://www.googleapis.com/auth/calendar.events";

    private readonly IHttpClientFactory _httpFactory;
    private readonly GoogleSyncApp _app;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(IHttpClientFactory httpFactory, IOptions<SyncOptions> options, ILogger<GoogleCalendarProvider> logger)
    {
        _httpFactory = httpFactory;
        _app = options.Value.Google;
        _logger = logger;
    }

    public string Key => "google";
    public string DisplayName => "Google Calendar";
    public bool Configured => !string.IsNullOrWhiteSpace(_app.ClientId) && !string.IsNullOrWhiteSpace(_app.ClientSecret);

    public string BuildAuthorizeUrl(string state, string redirectUri) =>
        $"{AuthorizeEndpoint}" +
        $"?client_id={Uri.EscapeDataString(_app.ClientId ?? string.Empty)}" +
        "&response_type=code" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&scope={Uri.EscapeDataString(Scope)}" +
        "&access_type=offline" +
        "&include_granted_scopes=true" +
        // consent is what guarantees a refresh token on every connection, not just the first;
        // select_account is what stops the browser's current Google account being bound by accident.
        "&prompt=select_account%20consent" +
        $"&state={Uri.EscapeDataString(state)}";

    public Task<TokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _app.ClientId ?? string.Empty,
            ["client_secret"] = _app.ClientSecret ?? string.Empty,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        }, ct);

    public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _app.ClientId ?? string.Empty,
            ["client_secret"] = _app.ClientSecret ?? string.Empty,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);

    private async Task<TokenSet> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("sync");
        using var resp = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            if (json.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                throw new ProviderAuthException("Google no longer accepts this connection — please reconnect.");
            throw new InvalidOperationException($"Google token request failed ({(int)resp.StatusCode}): {Truncate(json)}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access))
            throw new InvalidOperationException("Google returned no access token.");
        return new TokenSet(
            access,
            root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60)));
    }

    public async Task<RemoteAccount> GetAccountAsync(string accessToken, CancellationToken ct)
    {
        // The primary calendar's id IS the account's email — no profile scope needed.
        var http = Api(accessToken);
        using var resp = await http.GetAsync($"{ApiBase}/calendars/primary", ct);
        await ThrowIfFailedAsync(resp, "read the primary calendar", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return new RemoteAccount(Str(doc.RootElement, "id") ?? "unknown", Str(doc.RootElement, "summary"));
    }

    public async Task<IReadOnlyList<RemoteEvent>> ListEventsAsync(
        string accessToken, DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeZoneInfo zone, CancellationToken ct)
    {
        var http = Api(accessToken);
        var events = new List<RemoteEvent>();
        string? pageToken = null;

        for (var page = 0; page < 50; page++)
        {
            var url = $"{ApiBase}/calendars/primary/events" +
                      $"?timeMin={Uri.EscapeDataString(fromUtc.ToUniversalTime().ToString("o"))}" +
                      $"&timeMax={Uri.EscapeDataString(toUtc.ToUniversalTime().ToString("o"))}" +
                      "&singleEvents=true&showDeleted=true&maxResults=250&orderBy=startTime" +
                      (pageToken == null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            using var resp = await http.GetAsync(url, ct);
            await ThrowIfFailedAsync(resp, "list calendar events", ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            events.AddRange(ParseEvents(doc.RootElement, zone));
            pageToken = Str(doc.RootElement, "nextPageToken");
            if (pageToken == null) break;
        }
        return events;
    }

    /// <summary>Static so the shape Google sends can be tested from captured JSON.</summary>
    public static List<RemoteEvent> ParseEvents(JsonElement root, TimeZoneInfo zone)
    {
        var list = new List<RemoteEvent>();
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in items.EnumerateArray())
        {
            var id = Str(item, "id");
            if (string.IsNullOrEmpty(id)) continue;

            var cancelled = string.Equals(Str(item, "status"), "cancelled", StringComparison.OrdinalIgnoreCase);
            if (!item.TryGetProperty("start", out var start) || !item.TryGetProperty("end", out var end))
            {
                // A cancelled instance may come back with no times at all; still worth reporting.
                if (cancelled) list.Add(new RemoteEvent(id, Str(item, "summary"), DateTimeOffset.MinValue, DateTimeOffset.MinValue, false, false, true));
                continue;
            }

            var isAllDay = start.TryGetProperty("date", out _);
            var startUtc = ReadInstant(start, zone);
            var endUtc = ReadInstant(end, zone);
            if (startUtc == null || endUtc == null) continue;

            var transparent = string.Equals(Str(item, "transparency"), "transparent", StringComparison.OrdinalIgnoreCase);
            var declined = false;
            if (item.TryGetProperty("attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array)
            {
                foreach (var att in attendees.EnumerateArray())
                {
                    if (att.TryGetProperty("self", out var self) && self.ValueKind == JsonValueKind.True
                        && string.Equals(Str(att, "responseStatus"), "declined", StringComparison.OrdinalIgnoreCase))
                        declined = true;
                }
            }

            list.Add(new RemoteEvent(
                id,
                Str(item, "summary"),
                startUtc.Value,
                endUtc.Value,
                isAllDay,
                IsBusy: !transparent && !declined,
                IsCancelled: cancelled));
        }
        return list;
    }

    private static DateTimeOffset? ReadInstant(JsonElement when, TimeZoneInfo zone)
    {
        if (Str(when, "dateTime") is { } dateTime
            && DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            return instant.ToUniversalTime();

        // All-day: a date where the person is, from midnight. Google's end date is exclusive, which
        // is exactly the "until midnight" this needs.
        if (Str(when, "date") is { } date
            && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return new DateTimeOffset(day, zone.GetUtcOffset(day)).ToUniversalTime();

        return null;
    }

    public async Task<string> CreateEventAsync(string accessToken, RemoteEventDraft draft, CancellationToken ct)
    {
        var http = Api(accessToken);
        var body = new
        {
            summary = draft.Subject,
            description = draft.Body,
            start = new { dateTime = draft.StartUtc.ToUniversalTime().ToString("o") },
            end = new { dateTime = draft.EndUtc.ToUniversalTime().ToString("o") },
            transparency = "opaque",
            extendedProperties = new { @private = new { docucalendar = "1" } },
        };
        using var resp = await http.PostAsJsonAsync($"{ApiBase}/calendars/primary/events", body, ct);
        await ThrowIfFailedAsync(resp, "create the event", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return Str(doc.RootElement, "id") ?? throw new InvalidOperationException("Google created the event but returned no id.");
    }

    public async Task<bool> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
    {
        var http = Api(accessToken);
        using var resp = await http.DeleteAsync($"{ApiBase}/calendars/primary/events/{Uri.EscapeDataString(eventId)}", ct);
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return true;
        await ThrowIfFailedAsync(resp, "delete the event", ct);
        return true;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient("sync");
            using var resp = await http.PostAsync($"{RevokeEndpoint}?token={Uri.EscapeDataString(refreshToken)}", null, ct);
            _logger.LogInformation("[Sync] Google token revoked ({Status}).", (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Sync] Google token revocation failed; the row is dropped regardless.");
        }
    }

    private HttpClient Api(string accessToken)
    {
        var http = _httpFactory.CreateClient("sync");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage resp, string what, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new ProviderAuthException("Google rejected the access token — please reconnect.");
        throw new InvalidOperationException($"Google could not {what} ({(int)resp.StatusCode}): {Truncate(body)}");
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string Truncate(string s) => s.Length > 400 ? s[..400] + "…" : s;
}
