/**
 * Turning what a person typed into an instant.
 *
 * Everything on this site is expressed in the ACCOUNT's timezone, which is frequently not the
 * browser's — an owner in Tashkent may well be setting up a calendar from a laptop in London.
 * `new Date("2026-09-10T09:00")` would silently mean 09:00 in London, and the office would be
 * blocked at the wrong hour. These helpers convert deliberately instead.
 */

/** How far the given zone is from UTC at that instant, in milliseconds. */
function offsetMsAt(instant: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hour12: false,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
    .formatToParts(instant)
    .reduce<Record<string, string>>((acc, part) => {
      if (part.type !== 'literal') acc[part.type] = part.value;
      return acc;
    }, {});

  // Intl renders hour 24 for midnight in some locales/engines; normalise before arithmetic.
  const hour = parts.hour === '24' ? 0 : Number(parts.hour);
  const asIfUtc = Date.UTC(
    Number(parts.year),
    Number(parts.month) - 1,
    Number(parts.day),
    hour,
    Number(parts.minute),
    Number(parts.second),
  );
  return asIfUtc - instant.getTime();
}

/**
 * "2026-09-10" + "09:00" in the account's zone → the UTC instant.
 * Two passes, because the offset itself depends on the instant we are still solving for: the first
 * guess lands within an hour, the second lands on a daylight-saving boundary correctly.
 */
export function zonedToUtcIso(date: string, time: string, timeZone: string): string {
  const naive = Date.parse(`${date}T${time}:00Z`);
  if (Number.isNaN(naive)) throw new Error('Invalid date or time.');

  let instant = new Date(naive - offsetMsAt(new Date(naive), timeZone));
  instant = new Date(naive - offsetMsAt(instant, timeZone));
  return instant.toISOString();
}

/** Today in the account's zone, as yyyy-MM-dd — what a date input expects. */
export function todayInZone(timeZone: string): string {
  return new Date().toLocaleDateString('en-CA', { timeZone });
}

/** Adds days to a yyyy-MM-dd string without dragging local time into it. */
export function addDays(date: string, days: number): string {
  const [y, m, d] = date.split('-').map(Number);
  const next = new Date(Date.UTC(y, m - 1, d + days));
  return next.toISOString().slice(0, 10);
}
