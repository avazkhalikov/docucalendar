import { useMemo, type MouseEvent, type ReactNode } from 'react';
import { Ban, Phone, MessageSquare, User, X } from 'lucide-react';
import type { AppointmentRow, BusyRow, WeekResponse } from '../api';
import { dayInZone, timeInZone } from '../api';
import { clipToDay, formatDay, isWeekend, nowMinutesInZone } from '../time';

const HOUR_PX = 52;
const AXIS_PX = 56;

type Placed<T> = { item: T; start: number; end: number };

/**
 * The week as a grid: hours down the side, days across, and every kind of time drawn where it
 * falls — grey for busy (typed here or mirrored from Outlook/Google), blue for the assistant's
 * and staff's appointments, a faint green wash where a caller could still be booked. Click an
 * empty spot to block it or book somebody into it.
 */
export default function WeekGrid({
  days, today, timeZone, data, canEdit, onPick, onCancelAppointment, onRemoveBusy,
}: {
  days: string[];
  today: string;
  timeZone: string;
  data: WeekResponse;
  canEdit: boolean;
  onPick: (day: string, time: string) => void;
  onCancelAppointment: (a: AppointmentRow) => void;
  onRemoveBusy: (b: BusyRow) => void;
}) {
  const slotMinutes = data.calendar.slotMinutes || 20;

  // Everything placed per day, clipped to the day, so a multi-day block draws on each day it touches.
  const perDay = useMemo(
    () =>
      days.map((day) => {
        const busy: Placed<BusyRow>[] = [];
        for (const b of data.busy) {
          const c = clipToDay(b.startsAtUtc, b.endsAtUtc, day, timeZone);
          if (c) busy.push({ item: b, ...c });
        }
        const appts: Placed<AppointmentRow>[] = [];
        for (const a of data.appointments) {
          const c = clipToDay(a.startsAtUtc, a.endsAtUtc, day, timeZone);
          if (c) appts.push({ item: a, ...c });
        }
        // Free slots merge into bands: availability reads as "9 to 1", not as twelve stripes.
        const free: Array<{ start: number; end: number }> = [];
        const pieces = data.freeSlots
          .map((s) => clipToDay(s.startsAtUtc, new Date(Date.parse(s.startsAtUtc) + slotMinutes * 60_000).toISOString(), day, timeZone))
          .filter((x): x is { start: number; end: number } => x !== null)
          .sort((a, b) => a.start - b.start);
        for (const p of pieces) {
          const last = free[free.length - 1];
          if (last && p.start <= last.end) last.end = Math.max(last.end, p.end);
          else free.push({ ...p });
        }
        const freeCount = data.freeSlots.filter((s) => dayInZone(s.startsAtUtc, timeZone) === day).length;
        return { day, busy, appts, free, freeCount };
      }),
    [days, data, timeZone, slotMinutes],
  );

  // The hours shown: a normal working day, widened to whatever is actually on the calendar.
  const [startHour, endHour] = useMemo(() => {
    let lo = 8 * 60;
    let hi = 19 * 60;
    for (const d of perDay) {
      for (const x of [...d.busy, ...d.appts]) {
        lo = Math.min(lo, x.start);
        hi = Math.max(hi, x.end);
      }
      for (const f of d.free) {
        lo = Math.min(lo, f.start);
        hi = Math.max(hi, f.end);
      }
    }
    return [Math.max(0, Math.floor(lo / 60)), Math.min(24, Math.ceil(hi / 60))];
  }, [perDay]);

  const hours = Array.from({ length: Math.max(1, endHour - startHour) }, (_, i) => startHour + i);
  const bodyHeight = hours.length * HOUR_PX;
  const y = (minutes: number) => ((minutes - startHour * 60) / 60) * HOUR_PX;
  const nowMin = nowMinutesInZone(timeZone);
  const columns = `${AXIS_PX}px repeat(${days.length}, minmax(0, 1fr))`;

  const pick = (day: string, e: MouseEvent<HTMLDivElement>) => {
    if (!canEdit) return;
    const rect = e.currentTarget.getBoundingClientRect();
    const minutes = startHour * 60 + ((e.clientY - rect.top) / HOUR_PX) * 60;
    const snapped = Math.max(0, Math.floor(minutes / slotMinutes) * slotMinutes);
    onPick(day, `${String(Math.floor(snapped / 60)).padStart(2, '0')}:${String(snapped % 60).padStart(2, '0')}`);
  };

  return (
    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] overflow-hidden">
      <div className="overflow-x-auto">
        <div className="min-w-[880px]">
          {/* Day headers */}
          <div className="grid border-b border-slate-200 dark:border-slate-800" style={{ gridTemplateColumns: columns }}>
            <div />
            {perDay.map((d) => {
              const isToday = d.day === today;
              return (
                <div
                  key={d.day}
                  className={`px-2 py-2 text-center border-l border-slate-100 dark:border-slate-800 ${isWeekend(d.day) ? 'bg-slate-50/70 dark:bg-slate-900/30' : ''}`}
                >
                  <div className="text-[10px] uppercase tracking-wide text-slate-400">{formatDay(d.day, { weekday: 'short' })}</div>
                  <div
                    className={`mx-auto mt-0.5 w-7 h-7 grid place-items-center rounded-full text-sm font-semibold tabular-nums ${
                      isToday ? 'bg-blue-600 text-white' : 'text-slate-800 dark:text-slate-100'
                    }`}
                  >
                    {Number(d.day.slice(8))}
                  </div>
                  <div className={`text-[10px] tabular-nums ${d.freeCount > 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'}`}>
                    {d.freeCount} free
                  </div>
                </div>
              );
            })}
          </div>

          {/* Body */}
          <div className="grid" style={{ gridTemplateColumns: columns, height: bodyHeight }}>
            <div className="relative">
              {hours.map((h) => (
                <div key={h} className="absolute right-2 -translate-y-1/2 text-[10px] text-slate-400 tabular-nums" style={{ top: y(h * 60) }}>
                  {String(h).padStart(2, '0')}:00
                </div>
              ))}
            </div>

            {perDay.map((d) => (
              <div
                key={d.day}
                className={`relative border-l border-slate-100 dark:border-slate-800 ${canEdit ? 'cursor-crosshair' : ''} ${
                  isWeekend(d.day) ? 'bg-slate-50/40 dark:bg-slate-900/20' : ''
                }`}
                onClick={(e) => pick(d.day, e)}
                title={canEdit ? 'Click to block time or book someone in' : undefined}
              >
                {hours.map((h) => (
                  <div key={h} className="absolute inset-x-0 border-t border-slate-100 dark:border-slate-800/80" style={{ top: y(h * 60) }} />
                ))}

                {d.free.map((f, i) => (
                  <div
                    key={i}
                    className="absolute inset-x-0 bg-emerald-500/[0.07] dark:bg-emerald-400/[0.09] pointer-events-none"
                    style={{ top: y(f.start), height: y(f.end) - y(f.start) }}
                  />
                ))}

                {d.busy.map(({ item, start, end }) => (
                  <Block
                    key={`b-${item.id}-${start}`}
                    top={y(start)}
                    height={y(end) - y(start)}
                    tone="busy"
                    icon={<Ban className="w-3 h-3 shrink-0 mt-px" />}
                    title={item.reason ?? (item.source === 'manual' ? 'Blocked' : 'Busy')}
                    subtitle={`${timeInZone(item.startsAtUtc, timeZone)}–${timeInZone(item.endsAtUtc, timeZone)}${item.source !== 'manual' ? ` · ${sourceName(item.source)}` : ''}`}
                    hint={`${item.reason ?? 'Busy'} · ${timeInZone(item.startsAtUtc, timeZone)}–${timeInZone(item.endsAtUtc, timeZone)}${item.source !== 'manual' ? ` · from ${sourceName(item.source)}` : ''}`}
                    onRemove={canEdit && item.source === 'manual' ? () => onRemoveBusy(item) : undefined}
                    removeTitle="Free this time up"
                  />
                ))}

                {d.appts.map(({ item, start, end }) => (
                  <Block
                    key={`a-${item.id}-${start}`}
                    top={y(start)}
                    height={y(end) - y(start)}
                    tone={item.status === 'cancelled' ? 'cancelled' : 'appt'}
                    icon={
                      item.channel === 'phone' ? <Phone className="w-3 h-3 shrink-0 mt-px" />
                        : item.channel === 'chat' ? <MessageSquare className="w-3 h-3 shrink-0 mt-px" />
                          : <User className="w-3 h-3 shrink-0 mt-px" />
                    }
                    title={item.serviceName ? `${item.visitorName} · ${item.serviceName}` : item.visitorName}
                    subtitle={`${timeInZone(item.startsAtUtc, timeZone)} · ${item.minutes} min${item.topic ? ` · ${item.topic}` : ''}`}
                    hint={
                      `${item.visitorName} · ${item.visitorPhone} · ${timeInZone(item.startsAtUtc, timeZone)}, ${item.minutes} min` +
                      (item.serviceName ? `\n${item.serviceName}` : '') +
                      (item.topic ? `\n${item.topic}` : '') +
                      (item.answers?.length ? '\n' + item.answers.map((a) => `${a.question} ${a.answer}`).join('\n') : '') +
                      (item.status === 'cancelled' ? '\n(cancelled)' : '')
                    }
                    onRemove={canEdit && item.status !== 'cancelled' ? () => onCancelAppointment(item) : undefined}
                    removeTitle="Cancel this appointment"
                  />
                ))}

                {d.day === today && nowMin >= startHour * 60 && nowMin <= endHour * 60 && (
                  <div className="absolute inset-x-0 h-px bg-red-500 z-20 pointer-events-none" style={{ top: y(nowMin) }}>
                    <span className="absolute -left-1 -top-[3px] w-2 h-2 rounded-full bg-red-500" />
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      </div>

      <div className="flex items-center gap-4 px-3 py-2 border-t border-slate-100 dark:border-slate-800 text-[10px] text-slate-400">
        <span className="inline-flex items-center gap-1"><i className="w-2.5 h-2.5 rounded-sm bg-emerald-500/30" /> could be offered to a caller</span>
        <span className="inline-flex items-center gap-1"><i className="w-2.5 h-2.5 rounded-sm bg-blue-500/50" /> appointment</span>
        <span className="inline-flex items-center gap-1"><i className="w-2.5 h-2.5 rounded-sm bg-slate-400/60" /> busy</span>
        {canEdit && <span className="ml-auto hidden sm:inline">click an empty spot to block it or book someone</span>}
      </div>
    </div>
  );
}

function Block({
  top, height, tone, icon, title, subtitle, hint, onRemove, removeTitle,
}: {
  top: number;
  height: number;
  tone: 'busy' | 'appt' | 'cancelled';
  icon: ReactNode;
  title: string;
  subtitle: string;
  hint: string;
  onRemove?: () => void;
  removeTitle: string;
}) {
  const px = Math.max(height, 14);
  const tones: Record<typeof tone, string> = {
    busy: 'bg-slate-200/90 dark:bg-slate-700/70 border-slate-400 text-slate-700 dark:text-slate-200',
    appt: 'bg-blue-500/15 dark:bg-blue-500/20 border-blue-500 text-blue-900 dark:text-blue-100',
    cancelled: 'bg-slate-100 dark:bg-slate-800/40 border-slate-300 dark:border-slate-600 text-slate-400 line-through',
  };
  return (
    <div
      className={`group absolute left-1 right-1 rounded-md border-l-2 px-1.5 overflow-hidden text-[10.5px] leading-tight z-10 ${tones[tone]}`}
      style={{ top, height: px }}
      title={hint}
      onClick={(e) => e.stopPropagation()}
    >
      <div className="flex items-start gap-1 pt-0.5">
        {icon}
        <span className="truncate font-medium">{title}</span>
        {onRemove && (
          <button
            type="button"
            title={removeTitle}
            onClick={onRemove}
            className="ml-auto -mr-0.5 opacity-0 group-hover:opacity-100 text-slate-400 hover:text-red-500 transition-opacity"
          >
            <X className="w-3 h-3" />
          </button>
        )}
      </div>
      {px >= 30 && <div className="truncate opacity-75">{subtitle}</div>}
    </div>
  );
}

function sourceName(source: string): string {
  return source === 'microsoft' ? 'Outlook 365' : source === 'google' ? 'Google Calendar' : source;
}
