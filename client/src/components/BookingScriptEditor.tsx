import { Plus, X, GripVertical, MessageSquareText, ListChecks, Timer } from 'lucide-react';
import type { BookingScript } from '../api';

/**
 * How this calendar takes appointments, on top of the fixed spine (name, phone read back, only
 * real times offered). A dentist asks what hurts and books an hour for a filling; a bank desk
 * asks for an account number; a restaurant asks how many are coming. All three are the same
 * three controls: questions to ask, services with their own lengths, and instructions in the
 * owner's own words.
 */
export default function BookingScriptEditor({
  value, maxMinutes, disabled, onChange,
}: {
  value: BookingScript;
  maxMinutes: number;
  disabled: boolean;
  onChange: (next: BookingScript) => void;
}) {
  const set = (patch: Partial<BookingScript>) => onChange({ ...value, ...patch });
  const input =
    'w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm disabled:opacity-60';

  return (
    <div className="space-y-4">
      <div>
        <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-1 inline-flex items-center gap-1">
          <ListChecks className="w-3.5 h-3.5" /> Questions the assistant asks before booking
        </p>
        <p className="text-[11px] text-slate-400 mb-2">
          Name and phone number are always asked. Add what <em>you</em> need to know — “What is the problem with your
          teeth?”, “How many people?” — and mark the ones without which no appointment should be made.
        </p>
        <div className="space-y-1.5">
          {value.questions.map((q, i) => (
            <div key={i} className="flex items-center gap-2">
              <GripVertical className="w-3.5 h-3.5 text-slate-300 dark:text-slate-700 shrink-0" />
              <input
                value={q.ask}
                disabled={disabled}
                placeholder="What should the assistant ask?"
                maxLength={200}
                onChange={(e) => set({ questions: value.questions.map((x, j) => (j === i ? { ...x, ask: e.target.value } : x)) })}
                className={input}
              />
              <label className="inline-flex items-center gap-1 text-[11px] text-slate-500 dark:text-slate-400 whitespace-nowrap">
                <input
                  type="checkbox"
                  checked={q.required}
                  disabled={disabled}
                  onChange={(e) => set({ questions: value.questions.map((x, j) => (j === i ? { ...x, required: e.target.checked } : x)) })}
                />
                required
              </label>
              {!disabled && (
                <button
                  type="button"
                  title="Remove this question"
                  onClick={() => set({ questions: value.questions.filter((_, j) => j !== i) })}
                  className="text-slate-400 hover:text-red-500"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>
          ))}
          {!disabled && value.questions.length < 8 && (
            <button
              type="button"
              onClick={() => set({ questions: [...value.questions, { ask: '', required: false }] })}
              className="inline-flex items-center gap-1 text-[12px] text-blue-600 dark:text-blue-400 hover:underline"
            >
              <Plus className="w-3.5 h-3.5" /> Add a question
            </button>
          )}
        </div>
      </div>

      <div>
        <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-1 inline-flex items-center gap-1">
          <Timer className="w-3.5 h-3.5" /> Services, each with its own length
        </p>
        <p className="text-[11px] text-slate-400 mb-2">
          Optional. With services listed, the assistant asks which one the caller needs and books that length instead
          of the default — a filling can be an hour while a consultation stays twenty minutes. None may exceed the
          longest allowed ({maxMinutes} min).
        </p>
        <div className="space-y-1.5">
          {value.services.map((s, i) => (
            <div key={i} className="flex items-center gap-2">
              <input
                value={s.name}
                disabled={disabled}
                placeholder="Service name (what a caller would say)"
                maxLength={80}
                onChange={(e) => set({ services: value.services.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) })}
                className={input}
              />
              <input
                type="number"
                min={5}
                max={maxMinutes}
                step={5}
                value={s.minutes || ''}
                disabled={disabled}
                placeholder="min"
                onChange={(e) => set({ services: value.services.map((x, j) => (j === i ? { ...x, minutes: Number(e.target.value) || 0 } : x)) })}
                className={`${input} w-24 shrink-0 tabular-nums`}
              />
              <span className="text-[11px] text-slate-400 shrink-0">min</span>
              {!disabled && (
                <button
                  type="button"
                  title="Remove this service"
                  onClick={() => set({ services: value.services.filter((_, j) => j !== i) })}
                  className="text-slate-400 hover:text-red-500"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>
          ))}
          {!disabled && value.services.length < 12 && (
            <button
              type="button"
              onClick={() => set({ services: [...value.services, { name: '', minutes: 0 }] })}
              className="inline-flex items-center gap-1 text-[12px] text-blue-600 dark:text-blue-400 hover:underline"
            >
              <Plus className="w-3.5 h-3.5" /> Add a service
            </button>
          )}
        </div>
      </div>

      <div>
        <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-1 inline-flex items-center gap-1">
          <MessageSquareText className="w-3.5 h-3.5" /> Instructions for the assistant
        </p>
        <textarea
          value={value.instructions ?? ''}
          disabled={disabled}
          maxLength={1500}
          rows={3}
          placeholder="In your own words. “If the caller is in pain now, tell them to come straight in and do not book.” “Ask whether they have been here before.” “We do not book on the day — offer tomorrow onwards.”"
          onChange={(e) => set({ instructions: e.target.value })}
          className={input}
        />
        <p className="text-[10px] text-slate-400 mt-1 text-right tabular-nums">{(value.instructions ?? '').length} / 1500</p>
      </div>
    </div>
  );
}
