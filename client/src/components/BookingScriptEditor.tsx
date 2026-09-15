import { useState } from 'react';
import { Plus, X, GripVertical, MessageSquareText, ListChecks, Timer, Sparkles } from 'lucide-react';
import type { BookingScript, BookingTemplate } from '../api';

/**
 * How this calendar takes appointments, on top of the fixed spine (name, phone read back, only
 * real times offered). A dentist asks what hurts and books an hour for a filling; a bank desk
 * asks for an account number; a restaurant asks how many are coming. All three are the same
 * three controls: questions to ask, services with their own lengths, and instructions in the
 * owner's own words.
 */
export default function BookingScriptEditor({
  value, maxMinutes, disabled, onChange, templates, onApplyTemplate,
}: {
  value: BookingScript;
  maxMinutes: number;
  disabled: boolean;
  onChange: (next: BookingScript) => void;
  /** Ready-made scripts to start from; the picker is hidden when there are none. */
  templates?: BookingTemplate[];
  /** Fills the form from a template — the parent also takes the lengths it proposes. */
  onApplyTemplate?: (template: BookingTemplate) => void;
}) {
  const set = (patch: Partial<BookingScript>) => onChange({ ...value, ...patch });
  const [templateKey, setTemplateKey] = useState('');
  const chosen = templates?.find((t) => t.key === templateKey);
  const isBlank = !value.instructions && value.questions.length === 0 && value.services.length === 0;
  const applyTemplate = () => {
    if (!chosen || !onApplyTemplate) return;
    if (!isBlank && !window.confirm(`Replace what is here with the "${chosen.name}" template? Nothing is saved until you click Save.`)) return;
    onApplyTemplate(chosen);
  };
  // No width here: each row decides who grows. (A shared w-full on both inputs of the service
  // row let the minutes box win the whole line and squeezed the name to a sliver.)
  const field =
    'px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm disabled:opacity-60';
  const input = `${field} w-full`;

  return (
    <div className="space-y-4">
      {/* Ten kinds of business, ready to go: a dentist should not have to invent "what hurts?"
          from a blank form. Choosing one fills everything below; only Save writes it. */}
      {!disabled && templates && templates.length > 0 && (
        <div className="rounded-lg border border-dashed border-slate-300 dark:border-slate-700 p-3">
          <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-1.5 inline-flex items-center gap-1">
            <Sparkles className="w-3.5 h-3.5" /> Start from a template
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <select value={templateKey} onChange={(e) => setTemplateKey(e.target.value)} className={`${field} min-w-[18rem]`}>
              <option value="">Choose your kind of business…</option>
              {templates.map((t) => (
                <option key={t.key} value={t.key}>{t.name}</option>
              ))}
            </select>
            <button
              type="button"
              disabled={!chosen}
              onClick={applyTemplate}
              className="px-3 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium"
            >
              Fill in the form
            </button>
          </div>
          <p className="mt-1.5 text-[11px] text-slate-400">
            {chosen
              ? `${chosen.blurb} Fills the questions, services and instructions below, sets appointments to ${chosen.slotMinutes} min (longest ${chosen.maxMinutes}); change anything you like, then Save.`
              : 'Questions, services and instructions written for that business, ready to adjust and save.'}
          </p>
        </div>
      )}
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
                placeholder="Service name — what a caller would ask for, e.g. Filling"
                maxLength={80}
                onChange={(e) => set({ services: value.services.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) })}
                className={`${field} flex-1 min-w-0`}
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
                className={`${field} w-24 shrink-0 tabular-nums`}
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
          placeholder="Optional — in your own words, e.g. “We do not book on the day; offer tomorrow onwards.”"
          onChange={(e) => set({ instructions: e.target.value })}
          className={input}
        />
        <p className="text-[10px] text-slate-400 mt-1 text-right tabular-nums">{(value.instructions ?? '').length} / 1500</p>
      </div>
    </div>
  );
}
