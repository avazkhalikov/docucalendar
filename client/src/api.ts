/**
 * Everything this app knows how to ask the server.
 *
 * The session is a cookie the API set when Docurest handed us in, so every call carries
 * credentials and a 401 means exactly one thing: come back through the front door.
 */

export interface Me {
  tenantId: string;
  accountName: string;
  timeZoneId: string;
  userId: string;
  name: string;
  role: 'owner' | 'operator';
}

export interface CalendarRow {
  id: string;
  label: string;
  ownerUserId: string;
  mine: boolean;
  canEdit: boolean;
  /** True once anything was ever booked here: removing it then retires rather than deletes. */
  hasAppointments: boolean;
  /** "Bookable" events found in the linked Outlook/Google calendar, still ahead. While any windows exist only those hours are offered. */
  bookableWindows: number;
  /** "Bookable staff" events ahead — offered only to callers on the staff list. */
  staffWindows: number;
  /** The staff list as JSON [{name, phone}], or null. */
  staffCallers: string | null;
  /** "Ask me before confirming": bookings become requests the owner decides on, when the caller can be texted. */
  requiresConfirmation: boolean;
  slotMinutes: number;
  maxMinutes: number;
  bufferMinutes: number;
  minLeadMinutes: number;
  horizonDays: number;
  weeklyAvailability: string;
  /** The knowledge bases this calendar serves. Empty = offered on no line (bookable only by name). */
  contextIds: string[];
  /** How this calendar takes appointments beyond name + phone, as JSON; null = the standard way. */
  bookingScript: string | null;
  active: boolean;
  /** The one of this person's calendars the assistant books into. */
  isDefault: boolean;
}

/** The shape inside CalendarRow.bookingScript. */
export interface BookingScript {
  instructions?: string | null;
  questions: Array<{ ask: string; required: boolean }>;
  services: Array<{ name: string; minutes: number }>;
}

/** A ready-made way of booking for one kind of business, with the lengths that suit it. */
export interface BookingTemplate {
  key: string;
  name: string;
  blurb: string;
  slotMinutes: number;
  maxMinutes: number;
  script: BookingScript;
}

export const EMPTY_SCRIPT: BookingScript = { instructions: '', questions: [], services: [] };

export function parseBookingScript(json: string | null | undefined): BookingScript {
  if (!json) return { ...EMPTY_SCRIPT, questions: [], services: [] };
  try {
    const raw = JSON.parse(json) as Partial<BookingScript>;
    return {
      instructions: raw.instructions ?? '',
      questions: Array.isArray(raw.questions) ? raw.questions.map((q) => ({ ask: q.ask ?? '', required: !!q.required })) : [],
      services: Array.isArray(raw.services) ? raw.services.map((s) => ({ name: s.name ?? '', minutes: Number(s.minutes) || 0 })) : [],
    };
  } catch {
    return { ...EMPTY_SCRIPT, questions: [], services: [] };
  }
}

/** Serialises for saving; an all-empty script becomes '' so the server clears it. */
/** A colleague allowed into the "Bookable staff" hours, by the number they call from. */
export interface StaffCaller {
  name: string;
  phone: string;
  /** Optional: their bookings, confirmations and cancellations are e-mailed here too. */
  email?: string;
}

export function parseStaffCallers(json: string | null | undefined): StaffCaller[] {
  if (!json) return [];
  try {
    const raw = JSON.parse(json) as Array<Partial<StaffCaller>>;
    return Array.isArray(raw) ? raw.map((s) => ({ name: s.name ?? '', phone: s.phone ?? '', email: s.email ?? '' })) : [];
  } catch {
    return [];
  }
}

/** Blank rows are dropped; an empty list is the empty string, which clears it on the server. */
export function serialiseStaffCallers(list: StaffCaller[]): string {
  const rows = list
    .filter((s) => s.name.trim() || s.phone.trim() || (s.email ?? '').trim())
    .map((s) => ({ name: s.name.trim(), phone: s.phone.trim(), email: (s.email ?? '').trim() || undefined }));
  return rows.length === 0 ? '' : JSON.stringify(rows);
}

export function serialiseBookingScript(script: BookingScript): string {
  const questions = script.questions.filter((q) => q.ask.trim());
  const services = script.services.filter((s) => s.name.trim() && s.minutes > 0);
  const instructions = (script.instructions ?? '').trim();
  if (!instructions && questions.length === 0 && services.length === 0) return '';
  return JSON.stringify({ instructions: instructions || null, questions, services });
}

export interface CalendarsResponse {
  canManageAll: boolean;
  me: string;
  calendars: CalendarRow[];
  contextDefaults: Array<{ tenantContextId: string | null; calendarId: string }>;
}

export interface BusyRow {
  id: string;
  startsAtUtc: string;
  endsAtUtc: string;
  local: string;
  reason: string | null;
  source: string;
  /** "busy" blocks the time; "bookable" / "bookable-staff" OPEN it for the assistant. */
  kind?: string;
  /** The provider this window was copied to, when it was created here. */
  pushedTo?: string | null;
  /** Set when this is one occurrence of a repeat — shared by every occurrence of it. */
  seriesId?: string | null;
}

export interface AppointmentRow {
  id: string;
  startsAtUtc: string;
  endsAtUtc: string;
  local: string;
  minutes: number;
  visitorName: string;
  /** The number the visitor GAVE. */
  visitorPhone: string;
  /** The number the call actually came from. Null for web, chat, manual, or a withheld caller ID. */
  callerPhone?: string | null;
  topic: string | null;
  /** The calendar's service that was booked, when it has any. */
  serviceName?: string | null;
  /** What the caller answered to the calendar's questions. */
  answers?: Array<{ question: string; answer: string }>;
  channel: string;
  status: string;
}

/** One row of the account-wide Appointments list: an appointment plus which calendar it is on. */
export interface AppointmentListRow extends AppointmentRow {
  calendarId: string;
  calendarLabel: string;
  notifyEmail?: string | null;
  cancelledByName?: string | null;
  decidedByName?: string | null;
  createdAt: string;
}

export interface AppointmentsResponse {
  timeZone: string;
  total: number;
  page: number;
  pageSize: number;
  from: string;
  to: string;
  calendars: Array<{ id: string; label: string; active: boolean }>;
  appointments: AppointmentListRow[];
}

export interface WeekResponse {
  calendar: { id: string; label: string; canEdit: boolean; slotMinutes: number; maxMinutes: number };
  timeZone: string;
  busy: BusyRow[];
  appointments: AppointmentRow[];
  freeSlots: Array<{ startsAtUtc: string; local: string }>;
}

export class ApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

/**
 * Where this app's API lives, on whichever host is serving it: always under our own path prefix.
 * That is what makes the session cookie work inside Docurest's frame on a white-label portal —
 * the calendar and the page framing it are the same origin.
 */
export const API_BASE = '/calendar/api';

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
  });

  if (!response.ok) {
    let message = `Request failed (${response.status}).`;
    try {
      const body = await response.json();
      if (body?.message) message = body.message;
    } catch {
      /* a response with no JSON body still has a status worth reporting */
    }
    throw new ApiError(message, response.status);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export interface Person {
  userId: string;
  name: string;
  role: 'owner' | 'operator';
}

export type SyncProviderKey = 'microsoft' | 'google';

export interface SyncProvider {
  key: SyncProviderKey;
  displayName: string;
  /** False when this server has no credentials for the provider — shown, but not offered. */
  configured: boolean;
}

export interface SyncConnection {
  calendarId: string;
  provider: SyncProviderKey;
  displayName: string;
  accountEmail: string;
  status: 'connected' | 'reconnect' | 'error';
  lastSyncAt: string | null;
  lastSyncError: string | null;
  lastPulled: number;
  lastPushed: number;
  /** How often it syncs by itself, in minutes; 0 = only when somebody presses Sync now. */
  syncEveryMinutes: number;
}

export interface SyncRunResult {
  ok: boolean;
  error: string | null;
  pulled: number;
  pushed: number;
  moved: number;
  cancelled: number;
  status: string;
}

/** A full-page navigation, not a fetch: the provider's sign-in has to own the browser. */
export function connectUrl(provider: SyncProviderKey, calendarId: string): string {
  const base = `${API_BASE}/sync/${provider}/connect?calendarId=${encodeURIComponent(calendarId)}`;
  const origin = embeddingOrigin();
  return origin ? `${base}&returnTo=${encodeURIComponent(origin)}` : base;
}

/** True when this app is running inside Docurest's page rather than on its own tab. */
export const embedded = (() => {
  try {
    return window.self !== window.top;
  } catch {
    return true;
  }
})();

/**
 * The site that embeds us, so a provider sign-in (which must leave the frame — Google and
 * Microsoft refuse to render inside one) can come back to the page the person was on.
 * Same-site frames get the parent's origin as the referrer; the server checks it against its
 * own list before honouring it.
 */
export function embeddingOrigin(): string | null {
  if (!embedded) return null;
  // Served under /calendar/ on the same host as the page framing us, the parent's location is
  // readable directly — the exact origin, no referrer policy in the way. The referrer remains
  // as a fallback for the cross-origin case (calendar.docurest.com framed by www.docurest.com).
  try {
    const top = window.top?.location.origin;
    if (top) return top;
  } catch {
    /* cross-origin parent: the referrer is all we get */
  }
  try {
    return document.referrer ? new URL(document.referrer).origin : null;
  } catch {
    return null;
  }
}

export const api = {
  me: () => call<Me>('/session/me'),
  /** The account's team, pushed here by Docurest — so a calendar is assigned by picking a name. */
  people: () => call<{ people: Person[] }>('/people'),
  logout: () => call<{ signedOut: boolean }>('/session/logout', { method: 'POST' }),

  calendars: () => call<CalendarsResponse>('/calendars'),
  /** Ready-made scripts for common kinds of business; choosing one fills the form, Save writes it. */
  bookingTemplates: () => call<{ templates: BookingTemplate[] }>('/calendars/templates'),
  createCalendar: (body: { label: string; ownerUserId?: string; contextIds?: string[] }) =>
    call<{ id: string }>('/calendars', { method: 'POST', body: JSON.stringify(body) }),
  updateCalendar: (id: string, body: Record<string, unknown>) =>
    call<{ saved: boolean }>(`/calendars/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  /** The whole list of knowledge bases this calendar serves; replaces whatever was there. */
  setCalendarContexts: (id: string, contextIds: string[]) =>
    call<{ saved: boolean; contextIds: string[] }>(`/calendars/${id}/contexts`, {
      method: 'PUT',
      body: JSON.stringify({ contextIds }),
    }),
  /** Deletes a calendar nothing was ever booked in; retires one that has appointments. */
  removeCalendar: (id: string) =>
    call<{ deleted?: boolean; retired?: boolean }>(`/calendars/${id}`, { method: 'DELETE' }),
  makeDefaultCalendar: (id: string) =>
    call<{ saved: boolean; routesMoved: number }>(`/calendars/${id}/default`, { method: 'PUT' }),
  setContextDefault: (body: { tenantContextId: string | null; calendarId: string | null }) =>
    call<{ saved?: boolean; cleared?: boolean }>('/calendars/context-default', {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  week: (calendarId: string, fromIso: string, days: number) =>
    call<WeekResponse>(`/schedule/${calendarId}?from=${encodeURIComponent(fromIso)}&days=${days}`),
  /** kind: "bookable" | "bookable-staff" makes it a window the assistant may book; anything else blocks the time. */
  addBusy: (calendarId: string, body: {
    startsAtUtc: string; endsAtUtc: string; reason?: string; kind?: string;
    /** Weekdays to repeat on, 0 = Sunday (JavaScript's getDay()). Omit for a one-off. */
    repeatWeekdays?: number[];
    /** Local date (YYYY-MM-DD) the repeat runs until, inclusive. Required with repeatWeekdays. */
    repeatUntil?: string;
  }) =>
    call<{ id: string; seriesId: string | null; occurrences: number }>(
      `/schedule/${calendarId}/busy`, { method: 'POST', body: JSON.stringify(body) }),
  removeBusy: (id: string) => call<{ removed: boolean }>(`/schedule/busy/${id}`, { method: 'DELETE' }),
  /** Removes every occurrence of a repeat that has not happened yet; the past is left alone. */
  removeBusySeries: (seriesId: string) =>
    call<{ removed: number; kept: number }>(`/schedule/busy/series/${seriesId}`, { method: 'DELETE' }),
  addAppointment: (
    calendarId: string,
    body: { startsAtUtc: string; minutes?: number; visitorName: string; visitorPhone: string; topic?: string },
  ) => call<{ id: string }>(`/schedule/${calendarId}/appointments`, { method: 'POST', body: JSON.stringify(body) }),
  /** Accept or decline a pending request; the caller is texted the answer by Docurest. */
  decideAppointment: (id: string, confirm: boolean) =>
    call<{ status: string }>(`/schedule/appointments/${id}/${confirm ? 'confirm' : 'decline'}`, { method: 'POST' }),
  /** Every appointment on the account (owner) or on one's own calendars, across calendars. */
  appointments: (params: {
    from?: string; to?: string; calendarId?: string; status?: string; search?: string; page?: number; pageSize?: number;
  }) => {
    const q = new URLSearchParams();
    Object.entries(params).forEach(([k, v]) => { if (v !== undefined && v !== null && v !== '') q.set(k, String(v)); });
    return call<AppointmentsResponse>(`/appointments?${q.toString()}`);
  },
  cancelAppointment: (id: string) =>
    call<{ cancelled: boolean }>(`/schedule/appointments/${id}/cancel`, { method: 'POST' }),

  syncProviders: () => call<{ providers: SyncProvider[] }>('/sync/providers'),
  syncConnections: () => call<{ connections: SyncConnection[] }>('/sync/connections'),
  syncNow: (calendarId: string) =>
    call<SyncRunResult>(`/sync/connections/${calendarId}/sync-now`, { method: 'POST' }),
  disconnectSync: (calendarId: string) =>
    call<{ disconnected: boolean }>(`/sync/connections/${calendarId}/disconnect`, { method: 'POST' }),
  setSyncInterval: (calendarId: string, syncEveryMinutes: number) =>
    call<{ saved: boolean }>(`/sync/connections/${calendarId}`, { method: 'PUT', body: JSON.stringify({ syncEveryMinutes }) }),
};

/** The account's own clock — every hour a person reads here is rendered in it, never in the browser's. */
export function inZone(iso: string, timeZone: string, opts?: Intl.DateTimeFormatOptions): string {
  return new Date(iso).toLocaleString('en-GB', {
    timeZone,
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    ...opts,
  });
}

export function timeInZone(iso: string, timeZone: string): string {
  return new Date(iso).toLocaleTimeString('en-GB', {
    timeZone,
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** yyyy-MM-dd for the account's day, not the browser's. */
export function dayInZone(iso: string, timeZone: string): string {
  return new Date(iso).toLocaleDateString('en-CA', { timeZone });
}
