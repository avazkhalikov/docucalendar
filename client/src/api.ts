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
  active: boolean;
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

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
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

export const api = {
  me: () => call<Me>('/session/me'),
  logout: () => call<{ signedOut: boolean }>('/session/logout', { method: 'POST' }),

  calendars: () => call<CalendarsResponse>('/calendars'),
  createCalendar: (body: { label: string; ownerUserId?: string }) =>
    call<{ id: string }>('/calendars', { method: 'POST', body: JSON.stringify(body) }),
  updateCalendar: (id: string, body: Record<string, unknown>) =>
    call<{ saved: boolean }>(`/calendars/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateCalendar: (id: string) =>
    call<{ deactivated: boolean }>(`/calendars/${id}`, { method: 'DELETE' }),
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
