import { useMemo } from 'react';
import { Plus, X } from 'lucide-react';

const DAYS: Array<{ key: string; label: string }> = [
  { key: 'mon', label: 'Monday' },
  { key: 'tue', label: 'Tuesday' },
  { key: 'wed', label: 'Wednesday' },
  { key: 'thu', label: 'Thursday' },
  { key: 'fri', label: 'Friday' },
  { key: 'sat', label: 'Saturday' },
  { key: 'sun', label: 'Sunday' },
];

type Week = Record<string, Array<[string, string]>>;

/**
 * The working week, edited as what it is: a few time ranges per day. A day with no ranges is not
 * bookable, which is how a weekend is expressed — no checkbox, no special case.
 */
export default function WeekEditor({
  value,
  onChange,
  disabled,
}: {
  value: string;
  onChange: (json: string) => void;
  disabled?: boolean;
}) {
  const week = useMemo<Week>(() => {
    try {
      const parsed = JSON.parse(value);
      return typeof parsed === 'object' && parsed !== null ? (parsed as Week) : {};
    } catch {
      return {};
    }
  }, [value]);

  const emit = (next: Week) => onChange(JSON.stringify(next));

  const setWindow = (day: string, index: number, which: 0 | 1, time: string) => {
    const next: Week = { ...week, [day]: [...(week[day] ?? [])] };
    const window: [string, string] = [...next[day][index]] as [string, string];
    window[which] = time;
    next[day][index] = window;
    emit(next);
  };

  const addWindow = (day: string) => {
    const existing = week[day] ?? [];
    // A sensible second range is the afternoon, not another copy of the morning.
    const suggestion: [string, string] = existing.length === 0 ? ['09:00', '13:00'] : ['14:00', '18:00'];
    emit({ ...week, [day]: [...existing, suggestion] });
  };

  const removeWindow = (day: string, index: number) => {
    const next = { ...week, [day]: (week[day] ?? []).filter((_, i) => i !== index) };
    emit(next);
  };

  const bookableDays = DAYS.filter((d) => (week[d.key] ?? []).length > 0).length;

  return (
    <div className="space-y-2">
      {DAYS.map((day) => {
        const windows = week[day.key] ?? [];
        return (
          <div key={day.key} className="flex items-start gap-3 flex-wrap">
            <span className={`w-24 pt-1.5 text-sm ${windows.length ? 'text-slate-700 dark:text-slate-200' : 'text-slate-400'}`}>
              {day.label}
            </span>
            <div className="flex-1 flex flex-wrap items-center gap-2">
              {windows.length === 0 && <span className="text-xs text-slate-400 py-1.5">Not bookable</span>}
              {windows.map((window, index) => (
                <span key={index} className="inline-flex items-center gap-1 rounded-lg border border-slate-200 dark:border-slate-700 px-2 py-1">
                  <input
                    type="time"
                    value={window[0]}
                    disabled={disabled}
                    onChange={(e) => setWindow(day.key, index, 0, e.target.value)}
                    className="bg-transparent text-sm w-[5.5rem] focus:outline-none"
                  />
                  <span className="text-slate-400">–</span>
                  <input
                    type="time"
                    value={window[1]}
                    disabled={disabled}
                    onChange={(e) => setWindow(day.key, index, 1, e.target.value)}
                    className="bg-transparent text-sm w-[5.5rem] focus:outline-none"
                  />
                  {!disabled && (
                    <button
                      type="button"
                      onClick={() => removeWindow(day.key, index)}
                      className="text-slate-400 hover:text-red-500"
                      title="Remove this range"
                    >
                      <X className="w-3.5 h-3.5" />
                    </button>
                  )}
                </span>
              ))}
              {!disabled && (
                <button
                  type="button"
                  onClick={() => addWindow(day.key)}
                  className="inline-flex items-center gap-1 text-xs text-blue-600 dark:text-blue-400 hover:underline py-1.5"
                >
                  <Plus className="w-3.5 h-3.5" /> add
                </button>
              )}
            </div>
          </div>
        );
      })}
      {bookableDays === 0 && (
        <p className="text-xs text-amber-600 dark:text-amber-400">
          Nothing is bookable yet — add at least one time range, or the assistant will have nothing to offer.
        </p>
      )}
    </div>
  );
}
