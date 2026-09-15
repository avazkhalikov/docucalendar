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
  slotMinutes: number;
  maxMinutes: number;
  bufferMinutes: number;
  minLeadMinutes: number;
  horizonDays: number;
  weeklyAvailability: string;
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
}

export interface AppointmentRow {
  id: string;
  startsAtUtc: string;
  endsAtUtc: string;
  local: string;
  minutes: number;
  visitorName: string;
  visitorPhone: string;
  topic: string | null;
  /** The calendar's service that was booked, when it has any. */
  serviceName?: string | null;
  /** What the caller answered to the calendar's questions. */
  answers?: Array<{ question: string; answer: string }>;
  channel: string;
  status: string;
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
  createCalendar: (body: { label: string; ownerUserId?: string }) =>
    call<{ id: string }>('/calendars', { method: 'POST', body: JSON.stringify(body) }),
  updateCalendar: (id: string, body: Record<string, unknown>) =>
    call<{ saved: boolean }>(`/calendars/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateCalendar: (id: string) =>
    call<{ deactivated: boolean }>(`/calendars/${id}`, { method: 'DELETE' }),
  makeDefaultCalendar: (id: string) =>
    call<{ saved: boolean; routesMoved: number }>(`/calendars/${id}/default`, { method: 'PUT' }),
  setContextDefault: (body: { tenantContextId: string | null; calendarId: string | null }) =>
    call<{ saved?: boolean; cleared?: boolean }>('/calendars/context-default', {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  week: (calendarId: string, fromIso: string, days: number) =>
    call<WeekResponse>(`/schedule/${calendarId}?from=${encodeURIComponent(fromIso)}&days=${days}`),
  addBusy: (calendarId: string, body: { startsAtUtc: string; endsAtUtc: string; reason?: string }) =>
    call<{ id: string }>(`/schedule/${calendarId}/busy`, { method: 'POST', body: JSON.stringify(body) }),
  removeBusy: (id: string) => call<{ removed: boolean }>(`/schedule/busy/${id}`, { method: 'DELETE' }),
  addAppointment: (
    calendarId: string,
    body: { startsAtUtc: string; minutes?: number; visitorName: string; visitorPhone: string; topic?: string },
  ) => call<{ id: string }>(`/schedule/${calendarId}/appointments`, { method: 'POST', body: JSON.stringify(body) }),
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
