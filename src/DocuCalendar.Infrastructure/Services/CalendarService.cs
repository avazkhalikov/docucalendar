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
    /// Resolution order, most specific first:
    /// 1. the visitor named someone ("the admissions officer") and a calendar label matches;
    /// 2. the context's own default;
    /// 3. the account-wide fallback.
    /// Null when nothing resolves — which is exactly when the AI must not offer booking at all.
    /// </summary>
    public async Task<StaffCalendar?> ResolveAsync(string tenantId, Guid? contextId, string? hint, CancellationToken ct)
    {
        var active = await _db.Calendars.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Active)
            .ToListAsync(ct);
        if (active.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(hint))
        {
            var matched = MatchByLabel(active, hint!);
            if (matched != null) return matched;
        }

        var defaults = await _db.ContextDefaults.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .ToListAsync(ct);

        var forContext = contextId is { } ctx
            ? defaults.FirstOrDefault(d => d.TenantContextId == ctx)
            : null;
        var fallback = defaults.FirstOrDefault(d => d.TenantContextId == null);

        var chosen = forContext ?? fallback;
        return chosen == null ? null : active.FirstOrDefault(c => c.Id == chosen.CalendarId);
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

        var exact = calendars.FirstOrDefault(c => Normalise(c.Label) == needle);
        if (exact != null) return exact;

        var contains = calendars.Where(c =>
        {
            var label = Normalise(c.Label);
            return label.Contains(needle, StringComparison.Ordinal) || needle.Contains(label, StringComparison.Ordinal);
        }).ToList();
        if (contains.Count == 1) return contains[0];

        // Word overlap: "admissions officer" finds "Aziza — Admissions". Only when exactly one
        // calendar matches; two candidates mean the visitor must be asked, not guessed at.
        var words = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4).ToArray();
        if (words.Length == 0) return null;

        var byWord = calendars.Where(c =>
        {
            var label = Normalise(c.Label);
            return words.Any(w => label.Contains(w, StringComparison.Ordinal));
        }).ToList();
        return byWord.Count == 1 ? byWord[0] : null;
    }

    private static string Normalise(string s)
    {
        var cleaned = new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
