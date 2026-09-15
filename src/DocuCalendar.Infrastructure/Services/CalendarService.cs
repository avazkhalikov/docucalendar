using DocuCalendar.Domain.Entities;
using DocuCalendar.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.Infrastructure.Services;

/// <summary>
/// Finds the calendar a booking belongs on, and answers the question the AI tools are gated by:
/// can this context book at all?
/// </summary>
public sealed class CalendarService
{
    private readonly CalendarDbContext _db;

    public CalendarService(CalendarDbContext db) => _db = db;

    /// <summary>
    /// What a booking request resolved to. <see cref="Calendar"/> is where a booking would go;
    /// <see cref="UnknownStaff"/> says the visitor asked for somebody by name and nobody of that
    /// name takes appointments here — in which case the calendar is deliberately NOT the default.
    /// Booking a caller who asked for "Professor Karimov" into the owner's diary and calling it
    /// a meeting with the professor is worse than declining: it is a promise nobody will keep.
    /// </summary>
    public sealed record Resolution(StaffCalendar? Calendar, bool UnknownStaff, IReadOnlyList<string> BookableLabels);

    /// <summary>
    /// Resolution order, most specific first:
    /// 1. the visitor named someone ("the admissions officer") and a calendar label matches;
    /// 2. the visitor named someone and NOTHING matches → refused, with who can be booked instead;
    /// 3. the context's own default;
    /// 4. the account-wide fallback.
    /// A null calendar with no unknown-staff flag means nothing is set up — exactly when the AI
    /// must not offer booking at all.
    /// </summary>
    public async Task<Resolution> ResolveAsync(string tenantId, Guid? contextId, string? hint, CancellationToken ct)
    {
        var active = await _db.Calendars.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Active)
            .ToListAsync(ct);
        var labels = active.OrderBy(c => c.Label).Select(c => c.Label).ToList();
        if (active.Count == 0) return new Resolution(null, false, labels);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            var matched = MatchByLabel(active, hint!);
            if (matched != null) return new Resolution(matched, false, labels);
            // Only people and desks with calendars here can be booked. A name from a document,
            // an email address, a lecturer the caller once met — none of those is a calendar.
            return new Resolution(null, true, labels);
        }

        var defaults = await _db.ContextDefaults.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .ToListAsync(ct);

        var forContext = contextId is { } ctx
            ? defaults.FirstOrDefault(d => d.TenantContextId == ctx)
            : null;
        var fallback = defaults.FirstOrDefault(d => d.TenantContextId == null);

        var chosen = forContext ?? fallback;
        return new Resolution(chosen == null ? null : active.FirstOrDefault(c => c.Id == chosen.CalendarId), false, labels);
    }

    /// <summary>
    /// Matches a spoken name or department against calendar labels. Deliberately forgiving in the
    /// direction of NOT matching: a wrong match books a stranger into the wrong person's day,
    /// whereas no match simply falls through to the context default.
    /// </summary>
    public static StaffCalendar? MatchByLabel(IReadOnlyList<StaffCalendar> calendars, string hint)
    {
        var needle = Normalise(hint);
        if (needle.Length < 3) return null;

        // Everything the name could mean, exact matches included: "avaz" means both "Avaz" and
        // "Avaz Outlook", and which of those to book is decided by whose they are, not by which
        // string happened to match first.
        var contains = calendars.Where(c =>
        {
            var label = Normalise(c.Label);
            return label.Contains(needle, StringComparison.Ordinal) || needle.Contains(label, StringComparison.Ordinal);
        }).ToList();
        if (contains.Count > 0) return Choose(WithNameSiblings(contains, calendars), needle);

        // Word overlap: "admissions officer" finds "Aziza — Admissions".
        var words = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4).ToArray();
        if (words.Length == 0) return null;

        var byWord = calendars.Where(c =>
        {
            var label = Normalise(c.Label);
            return words.Any(w => label.Contains(w, StringComparison.Ordinal));
        }).ToList();
        return Choose(WithNameSiblings(byWord, calendars), needle);
    }

    /// <summary>
    /// Adds the other calendars of the same person whose label is a variant of a matched one:
    /// "Avaz Khalikov" string-matches only "Avaz", but "Avaz Outlook" is the same diary and may be
    /// the starred one. Seen live: the full name booked the un-starred Google calendar because
    /// only the bare "Avaz" label survived the substring test and a single match is honoured as
    /// named. Siblings whose labels do NOT contain each other are left alone — the owner's
    /// "Admissions" desk is not a variant of their personal diary.
    /// </summary>
    private static List<StaffCalendar> WithNameSiblings(List<StaffCalendar> matched, IReadOnlyList<StaffCalendar> all)
    {
        var result = new List<StaffCalendar>(matched);
        foreach (var m in matched)
        {
            var label = Normalise(m.Label);
            foreach (var c in all)
            {
                if (c.OwnerUserId != m.OwnerUserId || result.Contains(c)) continue;
                var other = Normalise(c.Label);
                if (other.Contains(label, StringComparison.Ordinal) || label.Contains(other, StringComparison.Ordinal))
                    result.Add(c);
            }
        }
        return result;
    }

    /// <summary>
    /// One calendar from what a name could mean, or null.
    ///
    /// Several candidates that all belong to ONE person are not an ambiguity — "Avaz" and
    /// "Avaz Outlook" are the same diary seen through two providers, and the person's starred
    /// default says which to book. A single match is honoured as named, whoever owns it: the
    /// owner's "Admissions" desk must not be redirected to the owner's personal diary.
    /// Candidates for different people mean the visitor must be asked, not guessed at — unless
    /// one of them is the full name they said.
    /// </summary>
    public static StaffCalendar? Choose(IReadOnlyList<StaffCalendar> candidates, string needle)
    {
        if (candidates.Count == 0) return null;
        var normalised = Normalise(needle);
        var exact = candidates.Where(c => Normalise(c.Label) == normalised).ToList();

        var onePerson = candidates.All(c => c.OwnerUserId == candidates[0].OwnerUserId);
        if (onePerson)
        {
            if (candidates.Count == 1) return candidates[0];
            return candidates.FirstOrDefault(c => c.IsDefault) ?? (exact.Count == 1 ? exact[0] : candidates[0]);
        }

        return exact.Count == 1 ? exact[0] : null;
    }

    private static string Normalise(string s)
    {
        var cleaned = new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
