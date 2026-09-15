using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocuCalendar.Application.Scheduling;

/// <summary>
/// How one calendar wants its appointments taken — the part of the conversation that differs
/// between a dentist, a bank desk, a restaurant and a university line.
///
/// The spine is fixed and lives in the phone prompt: name, phone number read back, only real
/// slots offered, one booking per call. Everything on top of it is here, written by the person
/// whose calendar it is: what to ask before booking, which services exist and how long each
/// takes, and anything else the assistant should know or do. Stored as JSON on the calendar;
/// parsed leniently (a missing part is simply empty) and validated strictly on the way in.
/// </summary>
public sealed record BookingScript(
    string? Instructions,
    IReadOnlyList<BookingQuestion> Questions,
    IReadOnlyList<BookingServiceOption> Services)
{
    public static readonly BookingScript Empty = new(null, Array.Empty<BookingQuestion>(), Array.Empty<BookingServiceOption>());

    public const int MaxInstructions = 1500;
    public const int MaxQuestions = 8;
    public const int MaxServices = 12;
    public const int MaxQuestionLength = 200;
    public const int MaxServiceName = 80;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Instructions) && Questions.Count == 0 && Services.Count == 0;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Never throws: a calendar with an unreadable script books the standard way.</summary>
    public static BookingScript Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            var raw = JsonSerializer.Deserialize<Raw>(json, Json);
            if (raw is null) return Empty;
            return new BookingScript(
                string.IsNullOrWhiteSpace(raw.Instructions) ? null : raw.Instructions.Trim(),
                (raw.Questions ?? new()).Where(q => !string.IsNullOrWhiteSpace(q.Ask))
                    .Select(q => new BookingQuestion(q.Ask!.Trim(), q.Required ?? false)).ToList(),
                (raw.Services ?? new()).Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.Minutes > 0)
                    .Select(s => new BookingServiceOption(s.Name!.Trim(), s.Minutes)).ToList());
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(new Raw
    {
        Instructions = Instructions,
        Questions = Questions.Select(q => new RawQuestion { Ask = q.Ask, Required = q.Required }).ToList(),
        Services = Services.Select(s => new RawService { Name = s.Name, Minutes = s.Minutes }).ToList(),
    }, Json);

    /// <summary>
    /// What a person may save. Limits are about the phone: a caller will not sit through nine
    /// questions, and a service list nobody can say out loud is not a menu.
    /// </summary>
    public static string? Validate(string? json, int maxMinutes, out BookingScript script)
    {
        script = Empty;
        if (string.IsNullOrWhiteSpace(json)) return null;

        Raw? raw;
        try { raw = JsonSerializer.Deserialize<Raw>(json, Json); }
        catch (JsonException) { return "The booking script could not be read."; }
        if (raw is null) return null;

        if (raw.Instructions is { Length: > MaxInstructions })
            return $"Instructions must be {MaxInstructions} characters or fewer.";

        var questions = (raw.Questions ?? new()).Where(q => !string.IsNullOrWhiteSpace(q.Ask)).ToList();
        if (questions.Count > MaxQuestions)
            return $"At most {MaxQuestions} questions — a caller will not sit through more.";
        if (questions.Any(q => q.Ask!.Trim().Length > MaxQuestionLength))
            return $"Each question must be {MaxQuestionLength} characters or fewer.";

        var services = (raw.Services ?? new()).Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();
        if (services.Count > MaxServices)
            return $"At most {MaxServices} services.";
        if (services.Any(s => s.Name!.Trim().Length > MaxServiceName))
            return $"Each service name must be {MaxServiceName} characters or fewer.";
        if (services.Any(s => s.Minutes < 5 || s.Minutes > 480))
            return "A service must last between 5 minutes and 8 hours.";
        if (services.Any(s => s.Minutes > maxMinutes))
            return $"A service cannot be longer than the calendar's longest allowed appointment ({maxMinutes} minutes) — raise that first.";
        if (services.Select(s => s.Name!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != services.Count)
            return "Two services have the same name.";

        script = Parse(json);
        return null;
    }

    /// <summary>The service a caller meant, by name — forgiving about case and spacing, strict about the rest.</summary>
    public BookingServiceOption? FindService(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || Services.Count == 0) return null;
        var needle = Normalise(name);
        return Services.FirstOrDefault(s => Normalise(s.Name) == needle)
               ?? Services.FirstOrDefault(s => Normalise(s.Name).Contains(needle) || needle.Contains(Normalise(s.Name)));
    }

    private static string Normalise(string s) =>
        string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed class Raw
    {
        public string? Instructions { get; set; }
        public List<RawQuestion>? Questions { get; set; }
        public List<RawService>? Services { get; set; }
    }

    private sealed class RawQuestion
    {
        public string? Ask { get; set; }
        public bool? Required { get; set; }
    }

    private sealed class RawService
    {
        public string? Name { get; set; }
        public int Minutes { get; set; }
    }
}

/// <summary>Something to ask before booking. Required means the assistant must not book without an answer.</summary>
public sealed record BookingQuestion(string Ask, bool Required);

/// <summary>A named thing a caller can book, with how long it takes.</summary>
public sealed record BookingServiceOption(string Name, int Minutes);

/// <summary>An answer a caller gave to one of the calendar's questions, kept with the appointment.</summary>
public sealed record BookingAnswer(string Question, string Answer);
