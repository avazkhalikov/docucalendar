import { useEffect, useState } from 'react';
import { Ban, CalendarCheck, ChevronDown, ChevronRight, Info } from 'lucide-react';

const OPEN_KEY = 'docucalendar.bookableHelp.open';

/**
 * What the three kinds of time mean, written where people act on them.
 *
 * The keywords are the part nobody can guess: a colleague setting their own hours in Outlook has
 * no way to learn that the event must be named exactly "Bookable", and the owner who set it up
 * months ago will not remember either. So this stays on the page rather than living in a document
 * somebody has to be told about. It collapses, and remembers that it was collapsed, for the people
 * who have already learnt it.
 */
export default function BookableHelp() {
  const [open, setOpen] = useState(() => {
    try { return localStorage.getItem(OPEN_KEY) !== 'closed'; } catch { return true; }
  });
  useEffect(() => {
    try { localStorage.setItem(OPEN_KEY, open ? 'open' : 'closed'); } catch { /* a private window forgets; fine */ }
  }, [open]);

  return (
    <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016]">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="w-full flex items-center gap-2 px-4 py-3 text-left"
        aria-expanded={open}
      >
        {open ? <ChevronDown className="w-4 h-4 text-slate-400 shrink-0" /> : <ChevronRight className="w-4 h-4 text-slate-400 shrink-0" />}
        <Info className="w-4 h-4 text-blue-500 shrink-0" />
        <span className="text-sm font-medium">Blocked, Bookable and Bookable staff — what each one does</span>
        {!open && <span className="text-[11px] text-slate-400 ml-auto hidden sm:inline">the keywords for Outlook and Google are in here</span>}
      </button>

      {open && (
        <div className="px-4 pb-4 space-y-3 text-[12px] text-slate-600 dark:text-slate-300">
          <div className="grid gap-2 sm:grid-cols-3">
            <div className="rounded-lg bg-slate-100 dark:bg-slate-800/60 px-3 py-2">
              <div className="flex items-center gap-1.5 font-medium text-slate-700 dark:text-slate-200">
                <Ban className="w-3.5 h-3.5 text-slate-400" /> Blocked
              </div>
              <p className="mt-1 text-slate-500 dark:text-slate-400">
                Time that is taken — your meetings, or time you block here. Never offered to a caller.
              </p>
            </div>
            <div className="rounded-lg bg-emerald-50 dark:bg-emerald-950/40 px-3 py-2">
              <div className="flex items-center gap-1.5 font-medium text-emerald-800 dark:text-emerald-200">
                <CalendarCheck className="w-3.5 h-3.5 text-emerald-500" /> Bookable
              </div>
              <p className="mt-1 text-emerald-700/80 dark:text-emerald-300/80">
                Hours you are open for appointments. Anyone who calls may be offered a time inside them.
              </p>
            </div>
            <div className="rounded-lg bg-emerald-50 dark:bg-emerald-950/40 px-3 py-2">
              <div className="flex items-center gap-1.5 font-medium text-emerald-800 dark:text-emerald-200">
                <CalendarCheck className="w-3.5 h-3.5 text-emerald-500" /> Bookable staff
              </div>
              <p className="mt-1 text-emerald-700/80 dark:text-emerald-300/80">
                The same, but kept for colleagues: offered only when the call comes from a number on this
                calendar’s staff list.
              </p>
            </div>
          </div>

          <div className="rounded-lg border border-amber-200 dark:border-amber-900/60 bg-amber-50 dark:bg-amber-950/30 px-3 py-2">
            <p className="font-medium text-amber-900 dark:text-amber-200">
              One bookable window switches your working hours off — everywhere, not just that day.
            </p>
            <p className="mt-1 text-amber-800/90 dark:text-amber-300/80">
              While this calendar has any bookable window at all, those windows <strong>are</strong> the availability:
              your normal working week stops being used, and a day with no window is closed. So mark every day you
              take appointments, not just one. A meeting that lands inside a window still blocks that part of it.
            </p>
          </div>

          <div>
            <p className="font-medium text-slate-700 dark:text-slate-200">Setting these from your own Outlook or Google</p>
            <p className="mt-1">
              You do not have to come here. In your own calendar, create an event over the hours you are open and
              name it exactly{' '}
              <code className="px-1 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-emerald-700 dark:text-emerald-300">Bookable</code>
              {' '}— or{' '}
              <code className="px-1 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-emerald-700 dark:text-emerald-300">Bookable staff</code>
              {' '}for colleagues only. Recurring events work, so “every weekday 09:00–13:00” is one entry.
            </p>
            <ul className="mt-1.5 space-y-1 list-disc pl-4 text-slate-500 dark:text-slate-400">
              <li>Capital letters do not matter, but <strong>nothing else may be in the name</strong>. “Bookable 9–11”, “Bookable — office” and “Not Bookable” are ordinary events, and ordinary events simply block the time.</li>
              <li>You never need a “Not Bookable” event. Time is unbookable unless a window says otherwise. Use a blocked event inside a window to carve out a lunch hour.</li>
              <li>Marking it Free or Busy makes no difference — either way it counts as a window. People forget, and it should not punish them.</li>
              <li>Windows added on this page are copied into your linked calendar within seconds, and deleting one here deletes the copy there.</li>
            </ul>
          </div>
        </div>
      )}
    </div>
  );
}
