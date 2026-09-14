import { useMemo } from 'react';
import type { AppointmentRow, BusyRow, WeekResponse } from '../api';
import { dayInZone, timeInZone } from '../api';
import { addDays, isWeekend } from '../time';

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

/**
 * The month at a glance: six weeks of days, each showing what is booked, what is busy and how
 * many slots a caller could still take. Click a day to open its week.
 */
export default function MonthGrid({
  gridStart, monthStart, today, timeZone, data, onOpenDay,
}: {
  gridStart: string;
  monthStart: string;
  today: string;
  timeZone: string;
  data: WeekResponse;
  onOpenDay: (day: string) => void;
}) {
  const days = useMemo(() => Array.from({ length: 42 }, (_, i) => addDays(gridStart, i)), [gridStart]);
  const month = monthStart.slice(0, 7);

  const byDay = useMemo(() => {
    const map = new Map<string, { busy: BusyRow[]; appts: AppointmentRow[]; free: number }>();
    for (const day of days) map.set(day, { busy: [], appts: [], free: 0 });
    const first = days[0];
    const last = days[days.length - 1];

    // A block spanning several days belongs to each of them.
    for (const b of data.busy) {
      let day = dayInZone(b.startsAtUtc, timeZone);
      const end = dayInZone(new Date(Date.parse(b.endsAtUtc) - 1).toISOString(), timeZone);
      while (day <= end) {
        if (day >= first && day <= last) map.get(day)?.busy.push(b);
        day = addDays(day, 1);
      }
    }
    for (const a of data.appointments) map.get(dayInZone(a.startsAtUtc, timeZone))?.appts.push(a);
    for (const s of data.freeSlots) {
      const entry = map.get(dayInZone(s.startsAtUtc, timeZone));
      if (entry) entry.free += 1;
    }
    return map;
  }, [days, data, timeZone]);

  return (
    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] overflow-hidden">
      <div className="grid grid-cols-7 border-b border-slate-200 dark:border-slate-800">
        {WEEKDAYS.map((w) => (
          <div key={w} className="px-2 py-1.5 text-center text-[10px] uppercase tracking-wide text-slate-400">{w}</div>
        ))}
      </div>
      <div className="grid grid-cols-7">
        {days.map((day, i) => {
          const entry = byDay.get(day)!;
          const inMonth = day.startsWith(month);
          const isToday = day === today;
          const confirmed = entry.appts.filter((a) => a.status !== 'cancelled');
          const shownAppts = confirmed.slice(0, 2);
          const shownBusy = entry.busy.slice(0, Math.max(0, 3 - shownAppts.length));
          const overflow = confirmed.length - shownAppts.length + entry.busy.length - shownBusy.length;
          return (
            <button
              type="button"
              key={day}
              onClick={() => onOpenDay(day)}
              title={`Open the week of ${day}`}
              className={`min-h-[96px] text-left p-1.5 border-b border-slate-100 dark:border-slate-800 transition-colors hover:bg-blue-50/60 dark:hover:bg-blue-950/20 ${
                i % 7 !== 0 ? 'border-l' : ''
              } ${inMonth ? '' : 'opacity-40'} ${isWeekend(day) ? 'bg-slate-50/60 dark:bg-slate-900/20' : ''}`}
            >
              <div className="flex items-center justify-between">
                <span
                  className={`w-6 h-6 grid place-items-center rounded-full text-[12px] font-medium tabular-nums ${
                    isToday ? 'bg-blue-600 text-white' : 'text-slate-700 dark:text-slate-200'
                  }`}
                >
                  {Number(day.slice(8))}
                </span>
                {entry.free > 0 && (
                  <span className="text-[10px] text-emerald-600 dark:text-emerald-400 tabular-nums">{entry.free} free</span>
                )}
              </div>
              <div className="mt-1 space-y-0.5">
                {shownAppts.map((a) => (
                  <div
                    key={a.id}
                    className="truncate rounded px-1 text-[10px] bg-blue-500/15 dark:bg-blue-500/20 text-blue-800 dark:text-blue-200 border-l-2 border-blue-500"
                  >
                    <span className="tabular-nums">{timeInZone(a.startsAtUtc, timeZone)}</span> {a.visitorName}
                  </div>
                ))}
                {shownBusy.map((b) => (
                  <div
                    key={`${b.id}-${day}`}
                    className="truncate rounded px-1 text-[10px] bg-slate-200/80 dark:bg-slate-700/60 text-slate-600 dark:text-slate-300 border-l-2 border-slate-400"
                  >
                    <span className="tabular-nums">{timeInZone(b.startsAtUtc, timeZone)}</span> {b.reason ?? 'Busy'}
                  </div>
                ))}
                {overflow > 0 && <div className="px-1 text-[10px] text-slate-400">+{overflow} more</div>}
              </div>
            </button>
          );
        })}
      </div>
    </div>
  );
}
