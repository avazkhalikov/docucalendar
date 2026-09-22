import { useCallback, useEffect, useMemo, useState } from 'react';
import { Loader2, Share2, Info, CheckCircle2, AlertTriangle } from 'lucide-react';
import { api, API_BASE, type CalendarRow, type Me } from '../api';

interface KnownContext {
  tenantContextId: string;
  domain: string;
}

/**
 * Who takes appointments on each knowledge base.
 *
 * This page used to choose ONE calendar per context, with an account-wide fallback underneath —
 * which meant an office with a reception desk, a call centre and a director offered every caller
 * the same diary, and the assistant, asked who could be seen, had exactly one name to give. A
 * context now has as many calendars as it needs, and a calendar can serve several contexts: the
 * reception desk answers admissions and the intranet without being created twice.
 */
export default function RoutingPage({ me }: { me: Me }) {
  const [calendars, setCalendars] = useState<CalendarRow[]>([]);
  const [contexts, setContexts] = useState<KnownContext[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savedKey, setSavedKey] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [list, ctxResponse] = await Promise.all([
        api.calendars(),
        fetch(`${API_BASE}/contexts`, { credentials: 'include' }).then((r) => (r.ok ? r.json() : { contexts: [] })),
      ]);
      setCalendars(list.calendars.filter((c) => c.active));
      setContexts(ctxResponse.contexts ?? []);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load the routing.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const servedBy = useMemo(() => {
    const map: Record<string, CalendarRow[]> = {};
    for (const ctx of contexts) map[ctx.tenantContextId] = [];
    for (const c of calendars)
      for (const id of c.contextIds ?? [])
        (map[id] ??= []).push(c);
    return map;
  }, [calendars, contexts]);

  // Calendars nobody put on a line: bookable if a caller says the name, volunteered to no one.
  const unassigned = calendars.filter((c) => (c.contextIds ?? []).length === 0);

  const toggle = async (calendar: CalendarRow, contextId: string) => {
    const key = `${calendar.id}:${contextId}`;
    const current = calendar.contextIds ?? [];
    const next = current.includes(contextId)
      ? current.filter((id) => id !== contextId)
      : [...current, contextId];

    setBusyKey(key);
    // Optimistic: a tick that waits for a round trip feels broken on a list this size.
    setCalendars((prev) => prev.map((c) => (c.id === calendar.id ? { ...c, contextIds: next } : c)));
    try {
      await api.setCalendarContexts(calendar.id, next);
      setSavedKey(key);
      window.setTimeout(() => setSavedKey((k) => (k === key ? null : k)), 2000);
      setError(null);
    } catch (e) {
      setCalendars((prev) => prev.map((c) => (c.id === calendar.id ? { ...c, contextIds: current } : c)));
      setError(e instanceof Error ? e.message : 'Could not save that.');
    } finally {
      setBusyKey((k) => (k === key ? null : k));
    }
  };

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold flex items-center gap-2">
          <Share2 className="w-5 h-5 text-blue-500" /> Assistant booking
        </h1>
        <p className="mt-1 text-sm text-slate-500 dark:text-slate-400 max-w-3xl">
          Tick everyone who takes appointments on each knowledge base. A knowledge base with nobody ticked offers no
          booking at all — the assistant will not mention appointments there.
        </p>
        <p className="mt-1 text-[12px] text-slate-400 max-w-3xl">
          When several people serve one knowledge base, the assistant reads the names out and asks the caller which
          they want, rather than choosing for them. A caller can also ask for somebody by name at any time.
        </p>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 dark:border-red-900 bg-red-50 dark:bg-red-950/40 px-4 py-3 text-sm text-red-700 dark:text-red-300">
          {error}
        </div>
      )}

      {loading ? (
        <div className="py-10 flex justify-center text-slate-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : calendars.length === 0 ? (
        <div className="rounded-xl border border-dashed border-slate-300 dark:border-slate-700 py-10 text-center text-sm text-slate-500">
          Create a calendar first — there is nothing to offer yet.
        </div>
      ) : contexts.length === 0 ? (
        <p className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] px-4 py-6 text-sm text-slate-500 dark:text-slate-400 flex items-start gap-2">
          <Info className="w-4 h-4 mt-0.5 shrink-0" />
          The list of knowledge bases has not arrived yet. Open this site once from your assistant dashboard and it is
          pushed here.
        </p>
      ) : (
        <div className="space-y-3">
          {contexts.map((ctx) => {
            const serving = servedBy[ctx.tenantContextId] ?? [];
            return (
              <div key={ctx.tenantContextId} className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016]">
                <div className="px-4 py-3 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between gap-3 flex-wrap">
                  <h2 className="text-sm font-medium">{ctx.domain}</h2>
                  {serving.length === 0 ? (
                    <span className="text-[11px] text-amber-600 dark:text-amber-400 inline-flex items-center gap-1">
                      <AlertTriangle className="w-3.5 h-3.5" /> No booking offered here
                    </span>
                  ) : (
                    <span className="text-[11px] text-slate-400">
                      {serving.length === 1
                        ? `${serving[0].label} takes appointments here`
                        : `${serving.length} people take appointments here — the assistant asks which`}
                    </span>
                  )}
                </div>
                <div className="p-3 grid gap-1.5 sm:grid-cols-2 lg:grid-cols-3">
                  {calendars.map((c) => {
                    const key = `${c.id}:${ctx.tenantContextId}`;
                    const on = (c.contextIds ?? []).includes(ctx.tenantContextId);
                    return (
                      <label
                        key={c.id}
                        className={`flex items-center gap-2 rounded-lg px-3 py-2 text-sm transition-colors ${
                          on
                            ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-900 dark:text-emerald-100'
                            : 'hover:bg-slate-50 dark:hover:bg-slate-900/40 text-slate-600 dark:text-slate-300'
                        } ${c.canEdit ? 'cursor-pointer' : 'opacity-60 cursor-not-allowed'}`}
                        title={c.canEdit ? undefined : 'Only the owner, or the person this calendar belongs to, can change this.'}
                      >
                        <input
                          type="checkbox"
                          checked={on}
                          disabled={!c.canEdit || busyKey === key}
                          onChange={() => void toggle(c, ctx.tenantContextId)}
                          className="w-4 h-4 rounded accent-emerald-600"
                        />
                        <span className="truncate">{c.label}</span>
                        {busyKey === key && <Loader2 className="w-3.5 h-3.5 animate-spin text-slate-400 ml-auto" />}
                        {savedKey === key && <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500 ml-auto" />}
                      </label>
                    );
                  })}
                </div>
              </div>
            );
          })}

          {unassigned.length > 0 && (
            <p className="text-[11px] text-slate-400 flex items-start gap-1.5">
              <Info className="w-3.5 h-3.5 mt-px shrink-0" />
              Not offered on any knowledge base yet: {unassigned.map((c) => c.label).join(', ')}. A caller can still ask
              for them by name, but the assistant will never suggest them.
            </p>
          )}

          <p className="text-[11px] text-slate-400">
            Callers can also ask for a person by name — “put me through to admissions” — and the assistant matches a
            calendar label among the people serving that knowledge base. Account: {me.accountName}.
          </p>
        </div>
      )}
    </div>
  );
}
