using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocuCalendar.Application.Scheduling;

/// <summary>One colleague allowed into the calendar's "Bookable staff" hours, by the number they call from.</summary>
public sealed record StaffCaller(string Name, string Phone);

/// <summary>
/// The calendar's staff list: the people whose calls unlock its "Bookable staff" windows. Kept
/// as JSON on the calendar, like the booking script, and read the same way — a broken document
/// means "no staff", never a failed booking.
/// </summary>
public static class StaffCallers
{
    public const int MaxEntries = 200;
    public const int MaxName = 80;

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
                .Select(r => new StaffCaller((r.Name ?? string.Empty).Trim(), r.Phone!.Trim()))
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
            if (name.Length == 0 && phone.Length == 0) continue; // a blank row the form left behind
            if (name.Length > MaxName) return $"A staff caller's name must be at most {MaxName} characters.";
            if (BookableWindows.NormalisePhone(phone).Length < 7) return $"\"{(name.Length > 0 ? name : phone)}\" needs a phone number with at least seven digits.";
            result.Add(new StaffCaller(name, phone));
        }
        list = result;
        return null;
    }

    public static string? ToJson(IReadOnlyList<StaffCaller> list) =>
        list.Count == 0 ? null : JsonSerializer.Serialize(list.Select(s => new Dto { Name = s.Name, Phone = s.Phone }), Json);

    public static IReadOnlyList<string> Phones(string? json) => Parse(json).Select(s => s.Phone).ToList();
}
