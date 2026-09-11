using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocuCalendar.Application.Sync;
using DocuCalendar.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuCalendar.Infrastructure.Services.Sync;

/// <summary>
/// Outlook 365 over Microsoft Graph, hand-rolled REST. Delegated auth: each person signs in as
/// themselves and consents to Calendars.ReadWrite on their own mailbox — no admin, no tenant-wide
/// grant, nothing this service can read that the person could not.
/// </summary>
public sealed class MicrosoftCalendarProvider : ICalendarProvider
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private static readonly string[] Scopes =
    {
        "offline_access",
        "https://graph.microsoft.com/Calendars.ReadWrite",
        "https://graph.microsoft.com/User.Read",
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly MicrosoftSyncApp _app;
    private readonly ILogger<MicrosoftCalendarProvider> _logger;

    public MicrosoftCalendarProvider(IHttpClientFactory httpFactory, IOptions<SyncOptions> options, ILogger<MicrosoftCalendarProvider> logger)
    {
        _httpFactory = httpFactory;
        _app = options.Value.Microsoft;
        _logger = logger;
    }

    public string Key => "microsoft";
    public string DisplayName => "Outlook 365";
    public bool Configured => !string.IsNullOrWhiteSpace(_app.ClientId) && !string.IsNullOrWhiteSpace(_app.ClientSecret);

    private string Authority => _app.Authority.TrimEnd('/');

    public string BuildAuthorizeUrl(string state, string redirectUri) =>
        $"{Authority}/oauth2/v2.0/authorize" +
        $"?client_id={Uri.EscapeDataString(_app.ClientId ?? string.Empty)}" +
        "&response_type=code" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        "&response_mode=query" +
        $"&scope={Uri.EscapeDataString(string.Join(' ', Scopes))}" +
        // Always ask WHICH account. Without this Microsoft reuses whatever the browser is signed
        // into, and the calendar that gets bound is whoever happened to be there — with no picker
        // and no confirmation. select_account shows the picker without forcing a password.
        "&prompt=select_account" +
        $"&state={Uri.EscapeDataString(state)}";

    public Task<TokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _app.ClientId ?? string.Empty,
            ["client_secret"] = _app.ClientSecret ?? string.Empty,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', Scopes),
        }, ct);

    public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _app.ClientId ?? string.Empty,
            ["client_secret"] = _app.ClientSecret ?? string.Empty,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = string.Join(' ', Scopes),
        }, ct);

    private async Task<TokenSet> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("sync");
        using var resp = await http.PostAsync($"{Authority}/oauth2/v2.0/token", new FormUrlEncodedContent(form), ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            // invalid_grant is the provider saying the refresh token is dead — consent withdrawn,
            // password changed, or the token simply aged out. Retrying it will never help.
            if (json.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                throw new ProviderAuthException("Microsoft no longer accepts this connection — please reconnect.");
            throw new InvalidOperationException($"Microsoft token request failed ({(int)resp.StatusCode}): {Truncate(json)}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access))
            throw new InvalidOperationException("Microsoft returned no access token.");
        return new TokenSet(
            access,
            root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60)));
    }

    public async Task<RemoteAccount> GetAccountAsync(string accessToken, CancellationToken ct)
    {
        var http = Graph(accessToken);
        using var resp = await http.GetAsync($"{GraphBase}/me?$select=mail,userPrincipalName,displayName", ct);
        await ThrowIfFailedAsync(resp, "read the signed-in account", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var email = Str(root, "mail") ?? Str(root, "userPrincipalName") ?? "unknown";
        return new RemoteAccount(email, Str(root, "displayName"));
    }

    public async Task<IReadOnlyList<RemoteEvent>> ListEventsAsync(
        string accessToken, DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeZoneInfo zone, CancellationToken ct)
    {
        var http = Graph(accessToken);
        // Times come back in the account's own zone, which is what makes an all-day event land
        // on the right date rather than the evening before in UTC.
        http.DefaultRequestHeaders.Add("Prefer", $"outlook.timezone=\"{zone.Id}\"");

        var url = $"{GraphBase}/me/calendarView" +
                  $"?startDateTime={Uri.EscapeDataString(fromUtc.ToUniversalTime().ToString("o"))}" +
                  $"&endDateTime={Uri.EscapeDataString(toUtc.ToUniversalTime().ToString("o"))}" +
                  "&$select=id,subject,start,end,isAllDay,showAs,isCancelled,responseStatus" +
                  "&$top=100";

        var events = new List<RemoteEvent>();
        for (var page = 0; url != null && page < 50; page++)
        {
            using var resp = await http.GetAsync(url, ct);
            await ThrowIfFailedAsync(resp, "list calendar events", ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            events.AddRange(ParseEvents(doc.RootElement, zone));
            url = Str(doc.RootElement, "@odata.nextLink");
        }
        return events;
    }

    /// <summary>Static so the shape Graph sends can be tested from captured JSON.</summary>
    public static List<RemoteEvent> ParseEvents(JsonElement root, TimeZoneInfo zone)
    {
        var list = new List<RemoteEvent>();
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in value.EnumerateArray())
        {
            var id = Str(item, "id");
            if (string.IsNullOrEmpty(id)) continue;
            if (!item.TryGetProperty("start", out var start) || !item.TryGetProperty("end", out var end)) continue;

            var isAllDay = item.TryGetProperty("isAllDay", out var ad) && ad.ValueKind == JsonValueKind.True;
            var startUtc = ReadInstant(start, zone, isAllDay);
            var endUtc = ReadInstant(end, zone, isAllDay);
            if (startUtc == null || endUtc == null) continue;

            var showAs = Str(item, "showAs") ?? "busy";
            var declined = item.TryGetProperty("responseStatus", out var rs)
                           && string.Equals(Str(rs, "response"), "declined", StringComparison.OrdinalIgnoreCase);
            var cancelled = item.TryGetProperty("isCancelled", out var c) && c.ValueKind == JsonValueKind.True;

            list.Add(new RemoteEvent(
                id,
                Str(item, "subject"),
                startUtc.Value,
                endUtc.Value,
                isAllDay,
                IsBusy: !string.Equals(showAs, "free", StringComparison.OrdinalIgnoreCase) && !declined,
                IsCancelled: cancelled));
        }
        return list;
    }

    private static DateTimeOffset? ReadInstant(JsonElement dateTimeTimeZone, TimeZoneInfo zone, bool isAllDay)
    {
        var text = Str(dateTimeTimeZone, "dateTime");
        if (string.IsNullOrEmpty(text)) return null;
        // Graph renders seven fractional digits; DateTime.Parse copes, DateTimeOffset needs a kind.
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return null;
        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);

        if (isAllDay)
        {
            // An all-day event is a date, not an instant: midnight to midnight where the person is.
            var day = local.Date;
            return new DateTimeOffset(day, zone.GetUtcOffset(day)).ToUniversalTime();
        }

        var tz = Str(dateTimeTimeZone, "timeZone");
        if (string.Equals(tz, "UTC", StringComparison.OrdinalIgnoreCase))
            return new DateTimeOffset(local, TimeSpan.Zero);

        var effective = zone;
        if (!string.IsNullOrEmpty(tz) && !string.Equals(tz, zone.Id, StringComparison.OrdinalIgnoreCase))
        {
            try { effective = TimeZoneInfo.FindSystemTimeZoneById(tz); } catch { /* keep the account's zone */ }
        }
        return new DateTimeOffset(local, effective.GetUtcOffset(local)).ToUniversalTime();
    }

    public async Task<string> CreateEventAsync(string accessToken, RemoteEventDraft draft, CancellationToken ct)
    {
        var http = Graph(accessToken);
        var body = new
        {
            subject = draft.Subject,
            body = new { contentType = "text", content = draft.Body },
            start = new { dateTime = draft.StartUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
            end = new { dateTime = draft.EndUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
            showAs = "busy",
            isReminderOn = true,
            reminderMinutesBeforeStart = 15,
        };
        using var resp = await http.PostAsJsonAsync($"{GraphBase}/me/events", body, ct);
        await ThrowIfFailedAsync(resp, "create the event", ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return Str(doc.RootElement, "id") ?? throw new InvalidOperationException("Microsoft created the event but returned no id.");
    }

    public async Task<bool> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
    {
        var http = Graph(accessToken);
        using var resp = await http.DeleteAsync($"{GraphBase}/me/events/{Uri.EscapeDataString(eventId)}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return true; // already gone is the outcome we wanted
        await ThrowIfFailedAsync(resp, "delete the event", ct);
        return true;
    }

    /// <summary>Microsoft has no token-revocation endpoint for delegated refresh tokens; dropping
    /// the row is all this service can do, and the person can revoke the app from their account.</summary>
    public Task RevokeAsync(string refreshToken, CancellationToken ct) => Task.CompletedTask;

    private HttpClient Graph(string accessToken)
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
            throw new ProviderAuthException("Microsoft rejected the access token — please reconnect.");
        throw new InvalidOperationException($"Microsoft could not {what} ({(int)resp.StatusCode}): {Truncate(body)}");
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string Truncate(string s) => s.Length > 400 ? s[..400] + "…" : s;
}
