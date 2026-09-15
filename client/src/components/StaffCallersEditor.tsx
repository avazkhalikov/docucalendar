import { Plus, X, Users } from 'lucide-react';
import type { StaffCaller } from '../api';

/**
 * Who counts as staff for this calendar's "Bookable staff" hours: colleagues by the number they
 * call from. Caller ID is the one thing a caller cannot simply say, which is why the list holds
 * numbers and not names alone — the name is there so the owner remembers whose number it is.
 */
export default function StaffCallersEditor({
  value, disabled, onChange,
}: {
  value: StaffCaller[];
  disabled: boolean;
  onChange: (next: StaffCaller[]) => void;
}) {
  const field =
    'px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm disabled:opacity-60';
  const update = (i: number, patch: Partial<StaffCaller>) => onChange(value.map((x, j) => (j === i ? { ...x, ...patch } : x)));

  return (
    <div>
      <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-1 inline-flex items-center gap-1">
        <Users className="w-3.5 h-3.5" /> Staff callers — who may book the “Bookable staff” hours
      </p>
      <p className="text-[11px] text-slate-400 mb-2">
        Colleagues by the number they call from. A call from one of these numbers is offered the staff hours as well as
        the public ones; everybody else sees public hours only. The name is just so you remember whose number it is.
      </p>
      <div className="space-y-1.5">
        {value.map((s, i) => (
          <div key={i} className="flex items-center gap-2">
            <input
              value={s.name}
              disabled={disabled}
              placeholder="Name — e.g. Avaz, IT manager"
              maxLength={80}
              onChange={(e) => update(i, { name: e.target.value })}
              className={`${field} flex-1 min-w-0`}
            />
            <input
              value={s.phone}
              disabled={disabled}
              placeholder="+998 90 123 45 67"
              inputMode="tel"
              maxLength={32}
              onChange={(e) => update(i, { phone: e.target.value })}
              className={`${field} w-48 shrink-0 tabular-nums`}
            />
            {!disabled && (
              <button
                type="button"
                title="Remove"
                onClick={() => onChange(value.filter((_, j) => j !== i))}
                className="text-slate-400 hover:text-red-500"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            )}
          </div>
        ))}
        {!disabled && value.length < 200 && (
          <button
            type="button"
            onClick={() => onChange([...value, { name: '', phone: '' }])}
            className="inline-flex items-center gap-1 text-[12px] text-blue-600 dark:text-blue-400 hover:underline"
          >
            <Plus className="w-3.5 h-3.5" /> Add a colleague
          </button>
        )}
      </div>
    </div>
  );
}
