namespace DocuCalendar.Application.Scheduling;

/// <summary>Who a window is for.</summary>
public enum BookableKind
{
    /// <summary>"Bookable": anyone may book these hours.</summary>
    Public,
    /// <summary>"Bookable staff": only callers on the calendar's staff list see these hours.</summary>
    Staff,
}

/// <summary>A window from the person's own calendar, with who it is for.</summary>
public sealed record BookableWindow(Interval Interval, BookableKind Kind);

/// <summary>
/// The words a person puts in their own Outlook or Google calendar to say "book me here".
///
/// A rector who takes appointments 10–11 and 16–18 lives in Outlook, not on this site, so the
/// hours are set where he already is: an event named exactly "Bookable" over each window,
/// recurring or one-off, up to the booking horizon. "Bookable staff" marks hours kept for
/// colleagues: offered only when the call comes from a number on the calendar's staff list.
/// While a calendar has any such windows, only they are offered and its working week is ignored;
/// without them, the working week applies as before. The event may be marked Free or Busy —
/// people forget — and either way it is a window, never taken time. A real meeting or
/// appointment inside a window still blocks it.
/// </summary>
public static class BookableWindows
{
    public const string Keyword = "Bookable";
    public const string StaffKeyword = "Bookable staff";

    /// <summary>
    /// Exactly the word — or the two words — in any capitalisation, surrounding whitespace ignored,
    /// and nothing else: "Bookable 9–11" or "not bookable" is an ordinary event.
    /// </summary>
    public static BookableKind? KindOf(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        var words = subject.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 1 && words[0] == "bookable") return BookableKind.Public;
        if (words.Length == 2 && words[0] == "bookable" && words[1] == "staff") return BookableKind.Staff;
        return null;
    }

    public static bool IsBookable(string? subject) => KindOf(subject) != null;

    /// <summary>
    /// The windows a caller may see: public ones always, staff ones only for a staff caller.
    /// </summary>
    public static List<Interval> Visible(IEnumerable<BookableWindow> windows, bool isStaffCaller) =>
        windows.Where(w => w.Kind == BookableKind.Public || isStaffCaller).Select(w => w.Interval).ToList();

    /// <summary>
    /// Digits only. "+998 90 123-45-67", "998901234567" and "90 123 45 67" all reduce to something
    /// comparable; the international "00" prefix is dropped.
    /// </summary>
    public static string NormalisePhone(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        return digits;
    }

    /// <summary>
    /// Whether two numbers are the same line. Written with and without a country code they still
    /// match — the shorter must be at least nine digits and be the tail of the longer, so a short
    /// extension can never pass for a phone number.
    /// </summary>
    public static bool SamePhone(string? a, string? b)
    {
        var x = NormalisePhone(a);
        var y = NormalisePhone(b);
        if (x.Length < 7 || y.Length < 7) return false;
        if (x == y) return true;
        var (shorter, longer) = x.Length <= y.Length ? (x, y) : (y, x);
        return shorter.Length >= 9 && longer.EndsWith(shorter, StringComparison.Ordinal);
    }

    /// <summary>Is the number the call came from on the staff list? A missing caller ID never is.</summary>
    public static bool IsStaffCaller(string? callerPhone, IEnumerable<string> staffPhones) =>
        NormalisePhone(callerPhone).Length >= 7 && staffPhones.Any(p => SamePhone(callerPhone, p));
}
