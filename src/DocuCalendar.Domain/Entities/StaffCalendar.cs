namespace DocuCalendar.Domain.Entities;

/// <summary>
/// One person's bookable calendar. An account has as many as it has staff: the owner, each
/// operator, or a desk that is not a person at all ("Admissions"). Named StaffCalendar rather than
/// Calendar because <see cref="System.Globalization.Calendar"/> is in scope everywhere under
/// implicit usings, and an entity you cannot name without qualifying is a daily tax.
/// </summary>
public class StaffCalendar
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The Docurest user this calendar belongs to. Drives permission: an operator manages their
    /// own calendar and no one else's; the account owner manages every one.
    /// </summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>
    /// What a caller would call it — "Aziza — Admissions", "Reception". This is the text the AI
    /// matches when a visitor names a person or department, so it is a label with a job, not
    /// decoration.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Length of one appointment, and the grid offers are aligned to. Default 20 minutes.</summary>
    public int SlotMinutes { get; set; } = 20;

    /// <summary>The longest a visitor may book, even when they ask for more. Default 60 minutes.</summary>
    public int MaxMinutes { get; set; } = 60;

    /// <summary>Breathing room kept free either side of every appointment and busy block.</summary>
    public int BufferMinutes { get; set; }

    /// <summary>How soon from now the first bookable slot may be — no ambush ten minutes from now.</summary>
    public int MinLeadMinutes { get; set; } = 60;

    /// <summary>How far ahead booking is allowed at all.</summary>
    public int HorizonDays { get; set; } = 30;

    /// <summary>
    /// The working week, in the tenant's own timezone:
    /// {"mon":[["09:00","13:00"],["14:00","18:00"]], "sat":[], ...}. A weekday that is absent or
    /// empty is simply not bookable — which is how a weekend is expressed, with no special case.
    /// </summary>
    public string WeeklyAvailabilityJson { get; set; } = DefaultWeek;

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Mon–Fri, 09:00–18:00 with an hour at 13:00 — a starting point every office can edit.</summary>
    public const string DefaultWeek =
        """{"mon":[["09:00","13:00"],["14:00","18:00"]],"tue":[["09:00","13:00"],["14:00","18:00"]],"wed":[["09:00","13:00"],["14:00","18:00"]],"thu":[["09:00","13:00"],["14:00","18:00"]],"fri":[["09:00","13:00"],["14:00","18:00"]],"sat":[],"sun":[]}""";
}
