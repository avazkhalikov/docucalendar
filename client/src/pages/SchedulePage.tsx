import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Loader2, Plus, Ban, X, Phone, MessageSquare, User, ChevronLeft, ChevronRight } from 'lucide-react';
import { api, dayInZone, timeInZone, type AppointmentRow, type BusyRow, type Me, type WeekResponse } from '../api';
import { addDays, todayInZone, zonedToUtcIso } from '../time';

const DAY_COUNT = 7;

function sourceName(source: string): string {
  return source === 'microsoft' ? 'Outlook 365' : source === 'google' ? 'Google Calendar' : source;
}

export default function SchedulePage({ me }: { me: Me }) {
  const { calendarId } = useParams<{ calendarId: string }>();
  const navigate = useNavigate();

  const [calendars, setCalendars] = useState<Array<{ id: string; label: string; canEdit: boolean }>>([]);
  const [from, setFrom] = useState(() => todayInZone(me.timeZoneId));
  const [week, setWeek] = useState<WeekResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        const list = await api.calendars();
        setCalendars(list.calendars.filter((c) => c.active).map((c) => ({ id: c.id, label: c.label, canEdit: c.canEdit })));
        if (!calendarId && list.calendars.length > 0) {
          const mine = list.calendars.find((c) => c.mine && c.active) ?? list.calendars.find((c) => c.active);
          if (mine) navigate(`/schedule/${mine.id}`, { replace: true });
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Could not load the calendars.');
      }
    })();
  }, [calendarId, navigate]);

  const load = useCallback(async () => {
    if (!calendarId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    try {
      // The window starts at 00:00 of the chosen day in the ACCOUNT's zone.
      const fromIso = zonedToUtcIso(from, '00:00', me.timeZoneId);
      setWeek(await api.week(calendarId, fromIso, DAY_COUNT));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load the schedule.');
    } finally {
      setLoading(false);
    }
  }, [calendarId, from, me.timeZoneId]);

  useEffect(() => {
    void load();
  }, [load]);

  const days = useMemo(() => Array.from({ length: DAY_COUNT }, (_, i) => addDays(from, i)), [from]);

  const byDay = useMemo(() => {
    const map = new Map<string, { busy: BusyRow[]; appointments: AppointmentRow[]; free: number }>();
    for (const day of days) map.set(day, { busy: [], appointments: [], free: 0 });
    if (week) {
      for (const b of week.busy) {
        const key = dayInZone(b.startsAtUtc, week.timeZone);
        map.get(key)?.busy.push(b);
      }
      for (const a of week.appointments) {
        const key = dayInZone(a.startsAtUtc, week.timeZone);
        map.get(key)?.appointments.push(a);
      }
      for (const s of week.freeSlots) {
        const key = dayInZone(s.startsAtUtc, week.timeZone);
        const entry = map.get(key);
        if (entry) entry.free += 1;
      }
    }
    return map;
  }, [days, week]);

  const canEdit = week?.calendar.canEdit ?? false;

  return (
    <div className="space-y-5">
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div>
          <h1 className="text-xl font-semibold">Schedule</h1>
          <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
            Busy time is never offered to a caller. Everything below is {me.timeZoneId} time.
          </p>
        </div>
        <select
          value={calendarId ?? ''}
          onChange={(e) => navigate(`/schedule/${e.target.value}`)}
          className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
        >
          {calendars.map((c) => (
            <option key={c.id} value={c.id}>{c.label}</option>
          ))}
        </select>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 dark:border-red-900 bg-red-50 dark:bg-red-950/40 px-4 py-3 text-sm text-red-700 dark:text-red-300">
          {error}
        </div>
      )}
      {notice && <div className="text-sm text-emerald-600 dark:text-emerald-400">{notice}</div>}

      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={() => setFrom(addDays(from, -DAY_COUNT))}
          className="p-2 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-500"
        >
          <ChevronLeft className="w-4 h-4" />
        </button>
        <input
          type="date"
          value={from}
          onChange={(e) => setFrom(e.target.value)}
          className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
        />
        <button
          type="button"
          onClick={() => setFrom(addDays(from, DAY_COUNT))}
          className="p-2 rounded-lg border border-slate-200 dark:border-slate-700 text-slate-500"
        >
          <ChevronRight className="w-4 h-4" />
        </button>
        <button type="button" onClick={() => setFrom(todayInZone(me.timeZoneId))} className="text-sm text-blue-600 dark:text-blue-400 hover:underline ml-1">
          today
        </button>
      </div>

      {loading ? (
        <div className="py-10 flex justify-center text-slate-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : !calendarId ? (
        <div className="rounded-xl border border-dashed border-slate-300 dark:border-slate-700 py-10 text-center text-sm text-slate-500">
          No calendar to show yet.
        </div>
      ) : (
        <>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {days.map((day) => {
              const entry = byDay.get(day)!;
              const label = new Date(`${day}T12:00:00Z`).toLocaleDateString('en-GB', {
                weekday: 'short', day: 'numeric', month: 'short',
              });
              return (
                <div key={day} className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-3">
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-medium">{label}</span>
                    <span className="text-[11px] text-slate-400">{entry.free} free</span>
                  </div>

                  <div className="mt-2 space-y-1.5">
                    {entry.busy.length === 0 && entry.appointments.length === 0 && (
                      <p className="text-[11px] text-slate-400">Nothing scheduled.</p>
                    )}

                    {entry.busy.map((b) => (
                      <div key={b.id} className="flex items-start gap-1.5 rounded-lg bg-slate-100 dark:bg-slate-800/60 px-2 py-1.5">
                        <Ban className="w-3.5 h-3.5 text-slate-400 mt-0.5 shrink-0" />
                        <div className="min-w-0 flex-1">
                          <div className="text-[12px] text-slate-600 dark:text-slate-300 tabular-nums">
                            {timeInZone(b.startsAtUtc, week!.timeZone)}–{timeInZone(b.endsAtUtc, week!.timeZone)}
                          </div>
                          {b.source === 'manual' ? (
                            b.reason && <div className="text-[11px] text-slate-400 truncate">{b.reason}</div>
                          ) : (
                            // Mirrored from the person's own calendar. The title is theirs to see;
                            // everyone else is shown "Busy" — the server already decided which.
                            <div className="text-[11px] text-slate-400 truncate">
                              {b.reason ?? 'Busy'} <span className="text-[10px]">· {sourceName(b.source)}</span>
                            </div>
                          )}
                        </div>
                        {canEdit && b.source === 'manual' && (
                          <button
                            type="button"
                            title="Free this time up"
                            onClick={async () => {
                              await api.removeBusy(b.id);
                              await load();
                            }}
                            className="text-slate-400 hover:text-red-500"
                          >
                            <X className="w-3.5 h-3.5" />
                          </button>
                        )}
                      </div>
                    ))}

                    {entry.appointments.map((a) => (
                      <div
                        key={a.id}
                        className={`rounded-lg px-2 py-1.5 ${
                          a.status === 'cancelled'
                            ? 'bg-slate-50 dark:bg-slate-900/40 opacity-60'
                            : 'bg-blue-50 dark:bg-blue-950/40'
                        }`}
                      >
                        <div className="flex items-start gap-1.5">
                          {a.channel === 'phone' ? (
                            <Phone className="w-3.5 h-3.5 text-blue-500 mt-0.5 shrink-0" />
                          ) : a.channel === 'chat' ? (
                            <MessageSquare className="w-3.5 h-3.5 text-blue-500 mt-0.5 shrink-0" />
                          ) : (
                            <User className="w-3.5 h-3.5 text-blue-500 mt-0.5 shrink-0" />
                          )}
                          <div className="min-w-0 flex-1">
                            <div className="text-[12px] font-medium tabular-nums">
                              {timeInZone(a.startsAtUtc, week!.timeZone)} · {a.minutes} min
                              {a.status === 'cancelled' && <span className="ml-1 text-[10px] uppercase text-slate-400">cancelled</span>}
                            </div>
                            <div className="text-[12px] text-slate-700 dark:text-slate-200 truncate">{a.visitorName}</div>
                            <a href={`tel:${a.visitorPhone}`} className="text-[11px] text-blue-600 dark:text-blue-400">{a.visitorPhone}</a>
                            {a.topic && <div className="text-[11px] text-slate-500 dark:text-slate-400 truncate">{a.topic}</div>}
                          </div>
                          {canEdit && a.status !== 'cancelled' && (
                            <button
                              type="button"
                              title="Cancel this appointment"
                              onClick={async () => {
                                if (!window.confirm(`Cancel ${a.visitorName}'s appointment?`)) return;
                                await api.cancelAppointment(a.id);
                                await load();
                              }}
                              className="text-slate-400 hover:text-red-500"
                            >
                              <X className="w-3.5 h-3.5" />
                            </button>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>

          {canEdit && calendarId && (
            <div className="grid gap-3 md:grid-cols-2">
              <BusyForm
                timeZone={me.timeZoneId}
                defaultDay={from}
                onSubmit={async (body) => {
                  await api.addBusy(calendarId, body);
                  setNotice('That time is now blocked — the assistant will not offer it.');
                  await load();
                }}
                onError={setError}
              />
              <ManualBookingForm
                timeZone={me.timeZoneId}
                defaultDay={from}
                defaultMinutes={week?.calendar.slotMinutes ?? 20}
                onSubmit={async (body) => {
                  await api.addAppointment(calendarId, body);
                  setNotice('Appointment added.');
                  await load();
                }}
                onError={setError}
              />
            </div>
          )}
        </>
      )}
    </div>
  );
}

function BusyForm({
  timeZone, defaultDay, onSubmit, onError,
}: {
  timeZone: string;
  defaultDay: string;
  onSubmit: (body: { startsAtUtc: string; endsAtUtc: string; reason?: string }) => Promise<void>;
  onError: (message: string) => void;
}) {
  const [day, setDay] = useState(defaultDay);
  const [start, setStart] = useState('09:00');
  const [end, setEnd] = useState('12:00');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => setDay(defaultDay), [defaultDay]);

  return (
    <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-4">
      <h2 className="text-sm font-medium mb-2">Block time</h2>
      <div className="flex flex-wrap items-end gap-2">
        <input type="date" value={day} onChange={(e) => setDay(e.target.value)}
          className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
        <input type="time" value={start} onChange={(e) => setStart(e.target.value)}
          className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
        <span className="text-slate-400 pb-2">–</span>
        <input type="time" value={end} onChange={(e) => setEnd(e.target.value)}
          className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
        <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Reason (optional, staff only)"
          className="flex-1 min-w-[10rem] px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
        <button
          type="button"
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            try {
              await onSubmit({
                startsAtUtc: zonedToUtcIso(day, start, timeZone),
                endsAtUtc: zonedToUtcIso(day, end, timeZone),
                reason: reason.trim() || undefined,
              });
              setReason('');
            } catch (e) {
              onError(e instanceof Error ? e.message : 'Could not block that time.');
            } finally {
              setBusy(false);
            }
          }}
          className="px-4 py-2 rounded-lg bg-slate-700 hover:bg-slate-600 disabled:opacity-50 text-white text-sm font-medium inline-flex items-center gap-1.5"
        >
          {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Ban className="w-4 h-4" />} Block
        </button>
      </div>
    </div>
  );
}

function ManualBookingForm({
  timeZone, defaultDay, defaultMinutes, onSubmit, onError,
}: {
  timeZone: string;
  defaultDay: string;
  defaultMinutes: number;
  onSubmit: (body: { startsAtUtc: string; minutes: number; visitorName: string; visitorPhone: string; topic?: string }) => Promise<void>;
  onError: (message: string) => void;
}) {
  const [day, setDay] = useState(defaultDay);
  const [start, setStart] = useState('10:00');
  const [minutes, setMinutes] = useState(defaultMinutes);
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [topic, setTopic] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => setDay(defaultDay), [defaultDay]);
  useEffect(() => setMinutes(defaultMinutes), [defaultMinutes]);

  return (
    <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-4">
      <h2 className="text-sm font-medium mb-2">Book someone in</h2>
      <div className="grid gap-2 sm:grid-cols-2">
        <input type="date" value={day} onChange={(e) => setDay(e.target.value)}
          className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
        <div className="flex gap-2">
          <input type="time" value={start} onChange={(e) => setStart(e.target.value)}
            className="flex-1 px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
          <input type="number" min={5} step={5} value={minutes} onChange={(e) => setMinutes(Number(e.target.value) || defaultMinutes)}
            title="Minutes"
            className="w-20 px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
        </div>
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Visitor name"
          className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
        <input value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="Phone"
          className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
        <input value={topic} onChange={(e) => setTopic(e.target.value)} placeholder="What it is about (optional)"
          className="sm:col-span-2 px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm" />
      </div>
      <button
        type="button"
        disabled={busy || !name.trim() || !phone.trim()}
        onClick={async () => {
          setBusy(true);
          try {
            await onSubmit({
              startsAtUtc: zonedToUtcIso(day, start, timeZone),
              minutes,
              visitorName: name.trim(),
              visitorPhone: phone.trim(),
              topic: topic.trim() || undefined,
            });
            setName(''); setPhone(''); setTopic('');
          } catch (e) {
            onError(e instanceof Error ? e.message : 'Could not book that time.');
          } finally {
            setBusy(false);
          }
        }}
        className="mt-2 px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium inline-flex items-center gap-1.5"
      >
        {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />} Add appointment
      </button>
    </div>
  );
}
