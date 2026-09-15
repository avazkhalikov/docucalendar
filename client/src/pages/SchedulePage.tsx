import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Loader2, Plus, Ban, X, Phone, MessageSquare, User, ChevronLeft, ChevronRight,
  CalendarRange, CalendarDays, List, MousePointerClick,
} from 'lucide-react';
import {
  api, dayInZone, timeInZone,
  type AppointmentRow, type BusyRow, type Me, type SyncConnection, type WeekResponse,
} from '../api';
import {
  addDays, addMonths, formatDay, formatMonth, formatRange, startOfMonth, startOfWeek, todayInZone, zonedToUtcIso,
} from '../time';
import WeekGrid from '../components/WeekGrid';
import MonthGrid from '../components/MonthGrid';
import SyncBadge from '../components/SyncBadge';

type View = 'week' | 'month' | 'list';
const VIEW_KEY = 'docucalendar.schedule.view';

function readView(): View {
  try {
    const v = localStorage.getItem(VIEW_KEY);
    return v === 'month' || v === 'list' ? v : 'week';
  } catch {
    return 'week';
  }
}

function sourceName(source: string): string {
  return source === 'microsoft' ? 'Outlook 365' : source === 'google' ? 'Google Calendar' : source;
}

export default function SchedulePage({ me }: { me: Me }) {
  const { calendarId } = useParams<{ calendarId: string }>();
  const navigate = useNavigate();
  const today = todayInZone(me.timeZoneId);

  const [calendars, setCalendars] = useState<Array<{ id: string; label: string; canEdit: boolean; mine: boolean }>>([]);
  const [view, setView] = useState<View>(readView);
  const [anchor, setAnchor] = useState(today);
  const [week, setWeek] = useState<WeekResponse | null>(null);
  const [connections, setConnections] = useState<SyncConnection[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pick, setPick] = useState<{ day: string; time: string } | null>(null);

  useEffect(() => {
    try { localStorage.setItem(VIEW_KEY, view); } catch { /* a private window forgets; fine */ }
  }, [view]);

  useEffect(() => {
    void (async () => {
      try {
        const list = await api.calendars();
        setCalendars(list.calendars.filter((c) => c.active).map((c) => ({ id: c.id, label: c.label, canEdit: c.canEdit, mine: c.mine })));
        if (!calendarId && list.calendars.length > 0) {
          const mine = list.calendars.find((c) => c.mine && c.active) ?? list.calendars.find((c) => c.active);
          if (mine) navigate(`/schedule/${mine.id}`, { replace: true });
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Could not load the calendars.');
      }
    })();
  }, [calendarId, navigate]);

  const loadSync = useCallback(async () => {
    try {
      setConnections((await api.syncConnections()).connections);
    } catch {
      /* the badge simply shows nothing */
    }
  }, []);
  useEffect(() => {
    void loadSync();
  }, [loadSync]);

  // What the view needs from the server: the visible range, in the account's zone.
  const range = useMemo(() => {
    if (view === 'month') {
      const monthStart = startOfMonth(anchor);
      const gridStart = startOfWeek(monthStart);
      return { from: gridStart, days: 42, monthStart, gridStart, label: formatMonth(monthStart) };
    }
    if (view === 'week') {
      const from = startOfWeek(anchor);
      return { from, days: 7, monthStart: '', gridStart: from, label: formatRange(from, addDays(from, 6)) };
    }
    return { from: anchor, days: 7, monthStart: '', gridStart: anchor, label: formatRange(anchor, addDays(anchor, 6)) };
  }, [view, anchor]);

  const load = useCallback(async () => {
    if (!calendarId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    try {
      // The window starts at 00:00 of the first visible day in the ACCOUNT's zone.
      const fromIso = zonedToUtcIso(range.from, '00:00', me.timeZoneId);
      setWeek(await api.week(calendarId, fromIso, range.days));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load the schedule.');
    } finally {
      setLoading(false);
    }
  }, [calendarId, range.from, range.days, me.timeZoneId]);

  useEffect(() => {
    void load();
  }, [load]);

  const step = (direction: -1 | 1) => {
    setPick(null);
    setAnchor(view === 'month' ? addMonths(startOfMonth(anchor), direction) : addDays(anchor, 7 * direction));
  };

  const days = useMemo(
    () => Array.from({ length: view === 'month' ? 42 : 7 }, (_, i) => addDays(range.from, i)),
    [range.from, view],
  );

  const byDay = useMemo(() => {
    const map = new Map<string, { busy: BusyRow[]; appointments: AppointmentRow[]; free: number }>();
    for (const day of days) map.set(day, { busy: [], appointments: [], free: 0 });
    if (week) {
      for (const b of week.busy) map.get(dayInZone(b.startsAtUtc, week.timeZone))?.busy.push(b);
      for (const a of week.appointments) map.get(dayInZone(a.startsAtUtc, week.timeZone))?.appointments.push(a);
      for (const s of week.freeSlots) {
        const entry = map.get(dayInZone(s.startsAtUtc, week.timeZone));
        if (entry) entry.free += 1;
      }
    }
    return map;
  }, [days, week]);

  const canEdit = week?.calendar.canEdit ?? false;
  const current = calendars.find((c) => c.id === calendarId);
  const connection = connections.find((c) => c.calendarId === calendarId);

  const cancelAppointment = async (a: AppointmentRow) => {
    if (!window.confirm(`Cancel ${a.visitorName}'s appointment?`)) return;
    try {
      await api.cancelAppointment(a.id);
      setNotice('Appointment cancelled.');
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not cancel.');
    }
  };

  const removeBusy = async (b: BusyRow) => {
    try {
      await api.removeBusy(b.id);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not free that time.');
    }
  };

  const viewButton = (v: View, icon: React.ReactNode, label: string) => (
    <button
      type="button"
      onClick={() => { setView(v); setPick(null); }}
      className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-sm rounded-lg transition-colors ${
        view === v
          ? 'bg-white dark:bg-[#101016] text-slate-900 dark:text-white shadow-sm'
          : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'
      }`}
    >
      {icon} {label}
    </button>
  );

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div>
          <h1 className="text-xl font-semibold">Schedule</h1>
          <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
            Busy time is never offered to a caller. Everything below is {me.timeZoneId} time.
          </p>
        </div>
        <div className="flex items-center gap-2 flex-wrap justify-end">
          {calendarId && (
            <SyncBadge
              calendarId={calendarId}
              canEdit={canEdit}
              mine={current?.mine ?? false}
              connection={connection}
              onChanged={async () => { await loadSync(); await load(); }}
              onError={setError}
            />
          )}
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
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 dark:border-red-900 bg-red-50 dark:bg-red-950/40 px-4 py-3 text-sm text-red-700 dark:text-red-300">
          {error}
        </div>
      )}
      {notice && <div className="text-sm text-emerald-600 dark:text-emerald-400">{notice}</div>}

      {/* Toolbar: where in time, and how to look at it. */}
      <div className="flex items-center gap-2 flex-wrap">
        <div className="inline-flex items-center rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden">
          <button type="button" onClick={() => step(-1)} className="p-2 text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800" title="Previous">
            <ChevronLeft className="w-4 h-4" />
          </button>
          <button
            type="button"
            onClick={() => { setAnchor(today); setPick(null); }}
            className="px-3 py-1.5 text-sm text-blue-600 dark:text-blue-400 hover:bg-slate-100 dark:hover:bg-slate-800 border-x border-slate-200 dark:border-slate-800"
          >
            today
          </button>
          <button type="button" onClick={() => step(1)} className="p-2 text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800" title="Next">
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
        <span className="text-sm font-medium text-slate-800 dark:text-slate-100 tabular-nums">{range.label}</span>
        <input
          type="date"
          value={anchor}
          onChange={(e) => { if (e.target.value) { setAnchor(e.target.value); setPick(null); } }}
          className="px-2 py-1.5 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
        />
        <div className="ml-auto inline-flex items-center gap-0.5 p-0.5 rounded-xl bg-slate-100 dark:bg-slate-900 border border-slate-200 dark:border-slate-800">
          {viewButton('week', <CalendarRange className="w-4 h-4" />, 'Week')}
          {viewButton('month', <CalendarDays className="w-4 h-4" />, 'Month')}
          {viewButton('list', <List className="w-4 h-4" />, 'List')}
        </div>
      </div>

      {loading && !week ? (
        <div className="py-10 flex justify-center text-slate-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : !calendarId || !week ? (
        <div className="rounded-xl border border-dashed border-slate-300 dark:border-slate-700 py-10 text-center text-sm text-slate-500">
          No calendar to show yet.
        </div>
      ) : (
        <div className={loading ? 'opacity-60 transition-opacity' : 'transition-opacity'}>
          {view === 'week' && (
            <WeekGrid
              days={days}
              today={today}
              timeZone={week.timeZone}
              data={week}
              canEdit={canEdit}
              onPick={(day, time) => setPick({ day, time })}
              onCancelAppointment={cancelAppointment}
              onRemoveBusy={removeBusy}
            />
          )}
          {view === 'month' && (
            <MonthGrid
              gridStart={range.gridStart}
              monthStart={range.monthStart}
              today={today}
              timeZone={week.timeZone}
              data={week}
              onOpenDay={(day) => { setAnchor(day); setView('week'); }}
            />
          )}
          {view === 'list' && (
            <DayList days={days} byDay={byDay} timeZone={week.timeZone} canEdit={canEdit} onCancel={cancelAppointment} onRemoveBusy={removeBusy} />
          )}
        </div>
      )}

      {canEdit && calendarId && (
        <>
          {pick && (
            <div className="flex items-center gap-2 text-sm text-slate-600 dark:text-slate-300">
              <MousePointerClick className="w-4 h-4 text-blue-500" />
              Picked <span className="font-medium">{formatDay(pick.day)} {pick.time}</span> — block it or book someone in below.
              <button type="button" onClick={() => setPick(null)} className="text-slate-400 hover:text-slate-600" title="Clear">
                <X className="w-3.5 h-3.5" />
              </button>
            </div>
          )}
          <div className="grid gap-3 md:grid-cols-2">
            <BusyForm
              timeZone={me.timeZoneId}
              defaultDay={pick?.day ?? (view === 'month' ? today : anchor)}
              defaultStart={pick?.time}
              onSubmit={async (body) => {
                await api.addBusy(calendarId, body);
                setNotice('That time is now blocked — the assistant will not offer it.');
                setPick(null);
                await load();
              }}
              onError={setError}
            />
            <ManualBookingForm
              timeZone={me.timeZoneId}
              defaultDay={pick?.day ?? (view === 'month' ? today : anchor)}
              defaultStart={pick?.time}
              defaultMinutes={week?.calendar.slotMinutes ?? 20}
              onSubmit={async (body) => {
                await api.addAppointment(calendarId, body);
                setNotice('Appointment added.');
                setPick(null);
                await load();
              }}
              onError={setError}
            />
          </div>
        </>
      )}
    </div>
  );
}

/** The original day-by-day cards, kept as a third way of looking at the same week. */
function DayList({
  days, byDay, timeZone, canEdit, onCancel, onRemoveBusy,
}: {
  days: string[];
  byDay: Map<string, { busy: BusyRow[]; appointments: AppointmentRow[]; free: number }>;
  timeZone: string;
  canEdit: boolean;
  onCancel: (a: AppointmentRow) => void;
  onRemoveBusy: (b: BusyRow) => void;
}) {
  return (
    <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
      {days.map((day) => {
        const entry = byDay.get(day)!;
        return (
          <div key={day} className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-3">
            <div className="flex items-center justify-between">
              <span className="text-sm font-medium">{formatDay(day)}</span>
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
                      {timeInZone(b.startsAtUtc, timeZone)}–{timeInZone(b.endsAtUtc, timeZone)}
                    </div>
                    {b.source === 'manual' ? (
                      b.reason && <div className="text-[11px] text-slate-400 truncate">{b.reason}</div>
                    ) : (
                      <div className="text-[11px] text-slate-400 truncate">
                        {b.reason ?? 'Busy'} <span className="text-[10px]">· {sourceName(b.source)}</span>
                      </div>
                    )}
                  </div>
                  {canEdit && b.source === 'manual' && (
                    <button type="button" title="Free this time up" onClick={() => onRemoveBusy(b)} className="text-slate-400 hover:text-red-500">
                      <X className="w-3.5 h-3.5" />
                    </button>
                  )}
                </div>
              ))}

              {entry.appointments.map((a) => (
                <div
                  key={a.id}
                  className={`rounded-lg px-2 py-1.5 ${a.status === 'cancelled' ? 'bg-slate-50 dark:bg-slate-900/40 opacity-60' : 'bg-blue-50 dark:bg-blue-950/40'}`}
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
                        {timeInZone(a.startsAtUtc, timeZone)} · {a.minutes} min
                        {a.status === 'cancelled' && <span className="ml-1 text-[10px] uppercase text-slate-400">cancelled</span>}
                      </div>
                      <div className="text-[12px] text-slate-700 dark:text-slate-200 truncate">
                        {a.visitorName}
                        {a.serviceName && <span className="text-slate-400"> · {a.serviceName}</span>}
                      </div>
                      <a href={`tel:${a.visitorPhone}`} className="text-[11px] text-blue-600 dark:text-blue-400">{a.visitorPhone}</a>
                      {a.topic && <div className="text-[11px] text-slate-500 dark:text-slate-400 truncate">{a.topic}</div>}
                      {a.answers?.map((ans, i) => (
                        // What the caller said before booking — the reason the person keeping this
                        // appointment opens it at all.
                        <div key={i} className="text-[11px] text-slate-500 dark:text-slate-400">
                          <span className="text-slate-400">{ans.question}</span> {ans.answer}
                        </div>
                      ))}
                    </div>
                    {canEdit && a.status !== 'cancelled' && (
                      <button type="button" title="Cancel this appointment" onClick={() => onCancel(a)} className="text-slate-400 hover:text-red-500">
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
  );
}

function addMinutes(time: string, minutes: number): string {
  const [h, m] = time.split(':').map(Number);
  const total = Math.min(23 * 60 + 59, h * 60 + m + minutes);
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}

function BusyForm({
  timeZone, defaultDay, defaultStart, onSubmit, onError,
}: {
  timeZone: string;
  defaultDay: string;
  defaultStart?: string;
  onSubmit: (body: { startsAtUtc: string; endsAtUtc: string; reason?: string }) => Promise<void>;
  onError: (message: string) => void;
}) {
  const [day, setDay] = useState(defaultDay);
  const [start, setStart] = useState(defaultStart ?? '09:00');
  const [end, setEnd] = useState(defaultStart ? addMinutes(defaultStart, 60) : '12:00');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => setDay(defaultDay), [defaultDay]);
  useEffect(() => {
    if (defaultStart) {
      setStart(defaultStart);
      setEnd(addMinutes(defaultStart, 60));
    }
  }, [defaultStart]);

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
  timeZone, defaultDay, defaultStart, defaultMinutes, onSubmit, onError,
}: {
  timeZone: string;
  defaultDay: string;
  defaultStart?: string;
  defaultMinutes: number;
  onSubmit: (body: { startsAtUtc: string; minutes: number; visitorName: string; visitorPhone: string; topic?: string }) => Promise<void>;
  onError: (message: string) => void;
}) {
  const [day, setDay] = useState(defaultDay);
  const [start, setStart] = useState(defaultStart ?? '10:00');
  const [minutes, setMinutes] = useState(defaultMinutes);
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [topic, setTopic] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => setDay(defaultDay), [defaultDay]);
  useEffect(() => { if (defaultStart) setStart(defaultStart); }, [defaultStart]);
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
