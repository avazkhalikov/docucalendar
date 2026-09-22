import { useCallback, useEffect, useState } from 'react';
import { Loader2, Contact, CheckCircle2, Info, AlertTriangle } from 'lucide-react';
import {
  api, parseStaffCallers, serialiseStaffCallers,
  type CalendarRow, type Me, type StaffCaller,
} from '../api';
import StaffCallersEditor from '../components/StaffCallersEditor';

/**
 * The staff list, on a page of its own.
 *
 * It was always there — buried under a calendar's settings, below a paragraph about Outlook — so
 * the question "where do I put a colleague's number?" had no findable answer. The list is what
 * makes "Bookable staff" hours mean anything: a caller is matched by the number they ring FROM,
 * and only a number on the list is offered those hours.
 */
export default function StaffPage({ me }: { me: Me }) {
  const [rows, setRows] = useState<CalendarRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows((await api.calendars()).calendars.filter((c) => c.active));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load the calendars.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const mine = rows.filter((c) => c.canEdit);
  const others = rows.filter((c) => !c.canEdit);

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold flex items-center gap-2">
          <Contact className="w-5 h-5 text-blue-500" /> Staff
        </h1>
        <p className="mt-1 text-sm text-slate-500 dark:text-slate-400 max-w-3xl">
          Colleagues who may book the hours you keep for staff. The assistant recognises them by the number they
          call <strong>from</strong> — caller ID — so the number here must be the one they ring you on.
        </p>
        <p className="mt-1 text-[12px] text-slate-400 max-w-3xl">
          Each calendar keeps its own list. Somebody on it is offered that calendar's <strong>Bookable staff</strong>{' '}
          hours as well as its public ones; everybody else sees the public hours only. An e-mail is optional — add one
          and their bookings are confirmed by e-mail, which costs nothing even when the account has no SMS service.
        </p>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 dark:border-red-900 bg-red-50 dark:bg-red-950/40 px-4 py-3 text-sm text-red-700 dark:text-red-300">
          {error}
        </div>
      )}

      {loading ? (
        <div className="py-10 flex justify-center text-slate-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : mine.length === 0 ? (
        <div className="rounded-xl border border-dashed border-slate-300 dark:border-slate-700 py-10 text-center text-sm text-slate-500">
          You have no calendar to keep a staff list for yet. Create one on Calendars first.
        </div>
      ) : (
        <div className="space-y-3">
          {mine.map((row) => <CalendarStaff key={row.id} row={row} onSaved={load} onError={setError} />)}

          {others.length > 0 && (
            <p className="text-[11px] text-slate-400 flex items-start gap-1.5">
              <Info className="w-3.5 h-3.5 mt-px shrink-0" />
              {others.length === 1 ? 'One other calendar belongs' : `${others.length} other calendars belong`} to
              somebody else on this account{me.role === 'owner' ? '' : ', and only they (or the account owner) can edit its list'}.
            </p>
          )}
        </div>
      )}
    </div>
  );
}

function CalendarStaff({ row, onSaved, onError }: {
  row: CalendarRow;
  onSaved: () => Promise<void>;
  onError: (message: string | null) => void;
}) {
  const [staff, setStaff] = useState<StaffCaller[]>(() => parseStaffCallers(row.staffCallers));
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  // A fresh load (or another tab's save) wins over an untouched form.
  useEffect(() => setStaff(parseStaffCallers(row.staffCallers)), [row.staffCallers]);

  const dirty = serialiseStaffCallers(staff) !== serialiseStaffCallers(parseStaffCallers(row.staffCallers));

  const save = async () => {
    setSaving(true);
    try {
      await api.updateCalendar(row.id, { staffCallers: serialiseStaffCallers(staff) });
      onError(null);
      setSaved(true);
      window.setTimeout(() => setSaved(false), 2000);
      await onSaved();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not save the staff list.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016]">
      <div className="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between gap-3 flex-wrap">
        <div>
          <h2 className="text-sm font-medium">{row.label}</h2>
          <p className="text-[11px] text-slate-400 mt-0.5">
            {staff.length === 0
              ? 'Nobody yet — its staff hours are offered to no one'
              : `${staff.length} ${staff.length === 1 ? 'colleague' : 'colleagues'}`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {saved && <CheckCircle2 className="w-4 h-4 text-emerald-500" />}
          <button
            type="button"
            onClick={save}
            disabled={saving || !dirty}
            className="px-3 py-1.5 rounded-lg bg-blue-600 hover:bg-blue-700 disabled:opacity-40 text-white text-[13px] font-medium inline-flex items-center gap-1.5"
          >
            {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : null}
            {dirty ? 'Save' : 'Saved'}
          </button>
        </div>
      </div>

      <div className="p-4">
        {/* A list with nobody on it is not an error, but staff windows then open for nobody —
            which is worth saying here rather than leaving to be discovered by telephone. */}
        {staff.length === 0 && row.staffWindows > 0 && (
          <p className="mb-3 text-[11px] text-amber-600 dark:text-amber-400 inline-flex items-start gap-1.5">
            <AlertTriangle className="w-3.5 h-3.5 mt-px shrink-0" />
            This calendar has {row.staffWindows} staff-only {row.staffWindows === 1 ? 'window' : 'windows'} ahead, and
            an empty list — so those hours are currently offered to nobody.
          </p>
        )}
        <StaffCallersEditor value={staff} disabled={false} onChange={setStaff} />
      </div>
    </div>
  );
}
