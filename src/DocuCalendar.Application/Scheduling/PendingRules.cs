namespace DocuCalendar.Application.Scheduling;

/// <summary>
/// When a booking is a request rather than a done deal, and when an undecided request lapses.
///
/// A rector's "Bookable" hours are his own promise to be available, so a booking is normally
/// final. Some calendars want to see who is coming first: with "Ask me before confirming" on,
/// the booking is a request — but only if the caller can be TOLD the answer. Docurest says
/// whether it can (an SMS service the account owner set up); without one, a request nobody could
/// answer is worse than an instant booking, so the calendar books as usual.
/// </summary>
public static class PendingRules
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Declined = "declined";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";

    /// <summary>A request nobody has decided on lapses this long before its start.</summary>
    public static readonly TimeSpan ExpiryLead = TimeSpan.FromHours(1);

    public static string InitialStatus(bool requiresConfirmation, bool callerCanBeTold) =>
        requiresConfirmation && callerCanBeTold ? Pending : Confirmed;

    /// <summary>Whether a request that starts at <paramref name="startsAtUtc"/> has lapsed by <paramref name="nowUtc"/>.</summary>
    public static bool HasLapsed(DateTimeOffset startsAtUtc, DateTimeOffset nowUtc) =>
        nowUtc >= startsAtUtc - ExpiryLead;

    /// <summary>Statuses that hold the time: a request keeps its slot while it waits.</summary>
    public static bool OccupiesSlot(string status) => status is Confirmed or Pending;
}
