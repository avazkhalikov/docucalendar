import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { CalendarClock, Loader2, Phone, PhoneIncoming, Search, User, MessageSquare, X, AlertTriangle } from 'lucide-react';
import { api, type AppointmentListRow, type AppointmentsResponse, type Me } from '../api';

/**
 * Every appointment on the account, in one list.
 *
 * The Schedule answers "what does Tuesday look like for Aziza". This answers the question an
 * owner actually asks out loud: who is coming, across everybody, and whose number do I ring when
 * something changes. Hence both phone numbers on every row — the one the visitor gave, and the
 * one the call came from, which are not always the same number.
 */

const STATUS_STYLE: Record<string, string> = {
  confirmed: 'bg-blue-50 dark:bg-blue-950/50 text-blue-700 dark:text-blue-300',
  pending: 'bg-amber-50 dark:bg-amber-950/50 text-amber-700 dark:text-amber-300',
  cancelled: 'bg-slate-100 dark:bg-slate-800 text-slate-500',
  declined: 'bg-slate-100 dark:bg-slate-800 text-slate-500',
  expired: 'bg-slate-100 dark:bg-slate-800 text-slate-500',
};

const STATUS_LABEL: Record<string, string> = {
  confirmed: 'confirmed',
  pending: 'awaiting your answer',
  cancelled: 'cancelled',
  declined: 'declined',
  expired: 'lapsed',
};

function today(): string {
  return new Date().toISOString().slice(0, 10);
}
function inDays(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return d.toISOString().slice(0, 10);
}

export default function AppointmentsPage({ me }: { me: Me }) {
  const [data, setData] = useState<AppointmentsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [from, setFrom] = useState(today());
  const [to, setTo] = useState(inDays(30));
  const [calendarId, setCalendarId] = useState('');
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setData(await api.appointments({ from, to, calendarId, status, search, page, pageSize: 50 }));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load the appointments.');
    } finally {
      setLoading(false);
    }
  }, [from, to, calendarId, status, search, page]);

  useEffect(() => { void load(); }, [load]);
  // Any filter change starts again at the first page, or page 3 of the old filter shows as empty.
  useEffect(() => { setPage(1); }, [from, to, calendarId, status, search]);

  const cancel = async (a: AppointmentListRow) => {
    if (!window.confirm(`Cancel ${a.visitorName}'s appointment on ${a.local}?`)) return;
    try {
      await api.cancelAppointment(a.id);
      setNotice('Appointment cancelled.');
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not cancel.');
    }
  };

  const decide = async (a: AppointmentListRow, confirm: boolean) => {
    try {
      await api.decideAppointment(a.id, confirm);
      setNotice(confirm ? 'Request accepted — the visitor is being told.' : 'Request declined — the visitor is being told.');
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not decide on that request.');
    }
  };

  const field = 'px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm';
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold">Appointments</h1>
        <p className="mt-1 text-sm text-slate-500 dark:text-slate-400 max-w-3xl">
          {me.role === 'owner'
            ? 'Everything booked across the account, soonest first.'
            : 'Everything booked in your calendars, soonest first.'}{' '}
          Each row carries both numbers: the one the visitor gave, and the one they actually called from. Times are in {me.timeZoneId}.
        </p>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 dark:border-red-900 bg-red-50 dark:bg-red-950/40 px-4 py-3 text-sm text-red-700 dark:text-red-300 flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" /> {error}
        </div>
      )}
      {notice && (
        <div className="rounded-lg border border-emerald-200 dark:border-emerald-900 bg-emerald-50 dark:bg-emerald-950/40 px-4 py-3 text-sm text-emerald-700 dark:text-emerald-300">
          {notice}
        </div>
      )}

      <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-3">
        <div className="flex flex-wrap items-end gap-2">
          <label className="block">
            <span className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">From</span>
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={field} />
          </label>
          <label className="block">
            <span className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">To</span>
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={field} />
          </label>
          <label className="block">
            <span className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">Calendar</span>
            <select value={calendarId} onChange={(e) => setCalendarId(e.target.value)} className={`${field} min-w-[12rem]`}>
              <option value="">All calendars</option>
              {(data?.calendars ?? []).map((c) => (
                <option key={c.id} value={c.id}>{c.label}{c.active ? '' : ' (retired)'}</option>
              ))}
            </select>
          </label>
          <label className="block">
            <span className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">Status</span>
            <select value={status} onChange={(e) => setStatus(e.target.value)} className={field}>
              <option value="">Upcoming &amp; confirmed</option>
              <option value="pending">Awaiting an answer</option>
              <option value="confirmed">Confirmed only</option>
              <option value="cancelled">Cancelled</option>
              <option value="all">Everything</option>
            </select>
          </label>
          <label className="block flex-1 min-w-[14rem]">
            <span className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">Search</span>
            <div className="relative">
              <Search className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Name, either number, or topic"
                className={`${field} w-full pl-8`}
              />
            </div>
          </label>
        </div>
      </div>

      {loading && !data ? (
        <div className="py-10 flex justify-center text-slate-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : (data?.appointments.length ?? 0) === 0 ? (
        <div className="rounded-xl border border-dashed border-slate-300 dark:border-slate-700 py-10 px-6 text-center text-sm text-slate-500 dark:text-slate-400">
          Nothing booked in that period.{' '}
          <Link to="/schedule" className="text-blue-600 dark:text-blue-400 hover:underline">Open the Schedule</Link> to add one by hand.
        </div>
      ) : (
        <div className="space-y-2">
          {data!.appointments.map((a) => (
            <AppointmentCard key={a.id} a={a} onCancel={cancel} onDecide={decide} />
          ))}
        </div>
      )}

      {data && data.total > data.pageSize && (
        <div className="flex items-center justify-between text-[12px] text-slate-500 dark:text-slate-400">
          <span>{data.total} appointments · page {data.page} of {pages}</span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={data.page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="px-3 py-1.5 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40"
            >
              Previous
            </button>
            <button
              type="button"
              disabled={data.page >= pages}
              onClick={() => setPage((p) => p + 1)}
              className="px-3 py-1.5 rounded-lg border border-slate-300 dark:border-slate-700 disabled:opacity-40"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function AppointmentCard({
  a, onCancel, onDecide,
}: {
  a: AppointmentListRow;
  onCancel: (a: AppointmentListRow) => void;
  onDecide: (a: AppointmentListRow, confirm: boolean) => void;
}) {
  const done = ['cancelled', 'declined', 'expired'].includes(a.status);
  return (
    <div className={`rounded-xl border p-3 ${
      a.status === 'pending'
        ? 'border-amber-200 dark:border-amber-900 bg-amber-50/50 dark:bg-amber-950/20'
        : done
          ? 'border-slate-200 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/40 opacity-70'
          : 'border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016]'
    }`}>
      <div className="flex items-start gap-3 flex-wrap">
        <div className="min-w-[11rem]">
          <div className="text-sm font-medium tabular-nums">{a.local}</div>
          <div className="text-[11px] text-slate-500 dark:text-slate-400">{a.minutes} min · {a.calendarLabel}</div>
        </div>

        <div className="min-w-[13rem] flex-1">
          <div className="text-sm text-slate-800 dark:text-slate-100 inline-flex items-center gap-1.5">
            <User className="w-3.5 h-3.5 text-slate-400" /> {a.visitorName}
            {a.serviceName && <span className="text-slate-400">· {a.serviceName}</span>}
          </div>
          {/* Both numbers, always labelled. They agree most of the time, and when they do not, that
              difference is the useful part — somebody booked from a different line than they gave. */}
          <div className="mt-1 space-y-0.5">
            <div className="text-[12px] inline-flex items-center gap-1.5">
              <Phone className="w-3 h-3 text-slate-400" />
              <span className="text-slate-400">gave</span>
              <a href={`tel:${a.visitorPhone}`} className="text-blue-600 dark:text-blue-400 tabular-nums">{a.visitorPhone}</a>
            </div>
            <div className="text-[12px] inline-flex items-center gap-1.5">
              <PhoneIncoming className="w-3 h-3 text-slate-400" />
              <span className="text-slate-400">called from</span>
              {a.callerPhone
                ? <a href={`tel:${a.callerPhone}`} className="text-blue-600 dark:text-blue-400 tabular-nums">{a.callerPhone}</a>
                : <span className="text-slate-400 italic">not known ({a.channel === 'phone' ? 'withheld' : a.channel})</span>}
            </div>
            {a.notifyEmail && <div className="text-[11px] text-slate-500 dark:text-slate-400">{a.notifyEmail}</div>}
          </div>
        </div>

        <div className="min-w-[12rem] flex-1">
          {a.topic && (
            <div className="text-[12px] text-slate-600 dark:text-slate-300 inline-flex items-start gap-1.5">
              <MessageSquare className="w-3 h-3 mt-0.5 text-slate-400 shrink-0" /> {a.topic}
            </div>
          )}
          {a.answers?.map((ans, i) => (
            <div key={i} className="text-[11px] text-slate-500 dark:text-slate-400">
              <span className="text-slate-400">{ans.question}</span> {ans.answer}
            </div>
          ))}
        </div>

        <div className="flex flex-col items-end gap-1.5">
          <span className={`px-1.5 py-0.5 rounded text-[11px] ${STATUS_STYLE[a.status] ?? STATUS_STYLE.confirmed}`}>
            {STATUS_LABEL[a.status] ?? a.status}
          </span>
          {a.status === 'pending' && (
            <div className="flex gap-1">
              <button type="button" onClick={() => onDecide(a, true)} className="px-2 py-0.5 rounded-md bg-emerald-600 hover:bg-emerald-700 text-white text-[11px] font-medium">Accept</button>
              <button type="button" onClick={() => onDecide(a, false)} className="px-2 py-0.5 rounded-md border border-slate-300 dark:border-slate-700 text-[11px] hover:text-red-500">Decline</button>
            </div>
          )}
          {a.status === 'confirmed' && (
            <button type="button" title="Cancel this appointment" onClick={() => onCancel(a)} className="text-slate-400 hover:text-red-500">
              <X className="w-3.5 h-3.5" />
            </button>
          )}
          <Link to={`/schedule/${a.calendarId}`} className="text-[11px] text-blue-600 dark:text-blue-400 hover:underline inline-flex items-center gap-1">
            <CalendarClock className="w-3 h-3" /> Schedule
          </Link>
        </div>
      </div>
    </div>
  );
}
