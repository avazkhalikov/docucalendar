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

/** The Monday on or before the day. Weeks start on Monday here; the working week is set that way. */
export function startOfWeek(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const dow = new Date(Date.UTC(y, m - 1, d)).getUTCDay(); // 0 = Sunday
  return addDays(date, dow === 0 ? -6 : 1 - dow);
}

export function startOfMonth(date: string): string {
  return `${date.slice(0, 7)}-01`;
}

export function addMonths(date: string, months: number): string {
  const [y, m] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1 + months, 1)).toISOString().slice(0, 10);
}

/** Whole days from one yyyy-MM-dd to another (negative when b is earlier). */
export function daysBetween(a: string, b: string): number {
  return Math.round((Date.parse(`${b}T00:00:00Z`) - Date.parse(`${a}T00:00:00Z`)) / 86_400_000);
}

/**
 * The part of [startIso, endIso) that falls on one local day, as minutes since that day's
 * midnight — what a grid needs to draw a block. Null when none of it does. Multi-day blocks
 * (a conference, an all-day "out of office") get one piece per day they touch, and the day
 * boundaries are real instants in the account's zone, so daylight-saving days come out right.
 */
export function clipToDay(
  startIso: string,
  endIso: string,
  day: string,
  timeZone: string,
): { start: number; end: number } | null {
  const dayStart = Date.parse(zonedToUtcIso(day, '00:00', timeZone));
  const dayEnd = Date.parse(zonedToUtcIso(addDays(day, 1), '00:00', timeZone));
  const s = Math.max(Date.parse(startIso), dayStart);
  const e = Math.min(Date.parse(endIso), dayEnd);
  if (Number.isNaN(s) || Number.isNaN(e) || e <= s) return null;
  return { start: (s - dayStart) / 60_000, end: (e - dayStart) / 60_000 };
}

/** Minutes since local midnight, right now, in the account's zone — for the "now" line. */
export function nowMinutesInZone(timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-GB', { timeZone, hour12: false, hour: '2-digit', minute: '2-digit' })
    .formatToParts(new Date());
  const hour = Number(parts.find((p) => p.type === 'hour')?.value ?? 0) % 24;
  const minute = Number(parts.find((p) => p.type === 'minute')?.value ?? 0);
  return hour * 60 + minute;
}

/** "14 – 20 Sept 2026" for a week, "September 2026" for a month. Pure dates: no zone involved. */
export function formatRange(from: string, to: string): string {
  const a = new Date(`${from}T12:00:00Z`);
  const b = new Date(`${to}T12:00:00Z`);
  const sameMonth = from.slice(0, 7) === to.slice(0, 7);
  const left = a.toLocaleDateString('en-GB', sameMonth ? { day: 'numeric' } : { day: 'numeric', month: 'short' });
  const right = b.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
  return `${left} – ${right}`;
}

export function formatMonth(monthStart: string): string {
  return new Date(`${monthStart}T12:00:00Z`).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' });
}

export function formatDay(day: string, opts: Intl.DateTimeFormatOptions = { weekday: 'short', day: 'numeric', month: 'short' }): string {
  return new Date(`${day}T12:00:00Z`).toLocaleDateString('en-GB', opts);
}

export function isWeekend(day: string): boolean {
  const dow = new Date(`${day}T12:00:00Z`).getUTCDay();
  return dow === 0 || dow === 6;
}
