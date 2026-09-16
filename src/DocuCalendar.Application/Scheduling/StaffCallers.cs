using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocuCalendar.Application.Scheduling;

/// <summary>
/// One colleague allowed into the calendar's "Bookable staff" hours, by the number they call
/// from — and, when known, the e-mail their appointments are confirmed to. Internal bookings
/// often outnumber outside ones, and a colleague has an inbox the platform can write to for
/// nothing, whether or not the account has an SMS service.
/// </summary>
public sealed record StaffCaller(string Name, string Phone, string? Email = null);

/// <summary>
/// The calendar's staff list: the people whose calls unlock its "Bookable staff" windows. Kept
/// as JSON on the calendar, like the booking script, and read the same way — a broken document
/// means "no staff", never a failed booking.
/// </summary>
public static class StaffCallers
{
    public const int MaxEntries = 200;
    public const int MaxName = 80;
    public const int MaxEmail = 120;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class Dto
    {
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
    }

    /// <summary>Never throws: an unreadable list is an empty one.</summary>
    public static IReadOnlyList<StaffCaller> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<StaffCaller>();
        try
        {
            var rows = JsonSerializer.Deserialize<List<Dto>>(json, Json) ?? new List<Dto>();
            return rows
                .Where(r => BookableWindows.NormalisePhone(r.Phone).Length >= 7)
                .Select(r => new StaffCaller((r.Name ?? string.Empty).Trim(), r.Phone!.Trim(), CleanEmail(r.Email)))
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<StaffCaller>();
        }
    }

    /// <summary>Strict on the way in: the form must not save what the phone cannot use.</summary>
    public static string? Validate(string? json, out IReadOnlyList<StaffCaller> list)
    {
        list = Array.Empty<StaffCaller>();
        if (string.IsNullOrWhiteSpace(json)) return null;

        List<Dto> rows;
        try { rows = JsonSerializer.Deserialize<List<Dto>>(json, Json) ?? new List<Dto>(); }
        catch (JsonException) { return "The staff list could not be read."; }

        if (rows.Count > MaxEntries) return $"At most {MaxEntries} staff callers.";
        var result = new List<StaffCaller>();
        foreach (var r in rows)
        {
            var name = (r.Name ?? string.Empty).Trim();
            var phone = (r.Phone ?? string.Empty).Trim();
            var email = (r.Email ?? string.Empty).Trim();
            if (name.Length == 0 && phone.Length == 0 && email.Length == 0) continue; // a blank row the form left behind
            if (name.Length > MaxName) return $"A staff caller's name must be at most {MaxName} characters.";
            if (BookableWindows.NormalisePhone(phone).Length < 7) return $"\"{(name.Length > 0 ? name : phone)}\" needs a phone number with at least seven digits.";
            if (email.Length > 0 && !LooksLikeEmail(email)) return $"\"{(name.Length > 0 ? name : phone)}\" has an e-mail address that does not look right.";
            result.Add(new StaffCaller(name, phone, email.Length == 0 ? null : email));
        }
        list = result;
        return null;
    }

    public static string? ToJson(IReadOnlyList<StaffCaller> list) =>
        list.Count == 0 ? null : JsonSerializer.Serialize(list.Select(s => new Dto { Name = s.Name, Phone = s.Phone, Email = s.Email }), Json);

    public static IReadOnlyList<string> Phones(string? json) => Parse(json).Select(s => s.Phone).ToList();

    /// <summary>The colleague a call came from, or null for anybody else — the same match that unlocks staff hours.</summary>
    public static StaffCaller? FindByPhone(string? json, string? callerPhone)
    {
        if (BookableWindows.NormalisePhone(callerPhone).Length < 7) return null;
        return Parse(json).FirstOrDefault(s => BookableWindows.SamePhone(callerPhone, s.Phone));
    }

    private static string? CleanEmail(string? email)
    {
        var e = (email ?? string.Empty).Trim();
        return e.Length > 0 && LooksLikeEmail(e) ? e : null;
    }

    /// <summary>Enough to catch a typo, not a validator: one "@" with something on both sides, no spaces.</summary>
    public static bool LooksLikeEmail(string email)
    {
        if (email.Length > MaxEmail || email.Any(char.IsWhiteSpace)) return false;
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 3 && email[(at + 1)..].Contains('.');
    }
}
