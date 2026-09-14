import { useCallback, useEffect, useState } from 'react';
import { Loader2, Share2, Info, CheckCircle2 } from 'lucide-react';
import { api, API_BASE, type CalendarRow, type Me } from '../api';

interface KnownContext {
  tenantContextId: string;
  domain: string;
}

/**
 * Where the assistant sends people. Owner-only: this is the switch that decides whether the AI
 * offers appointments at all, and on whose day they land.
 */
export default function RoutingPage({ me }: { me: Me }) {
  const [calendars, setCalendars] = useState<CalendarRow[]>([]);
  const [defaults, setDefaults] = useState<Record<string, string>>({}); // contextId ("account" for the fallback) → calendarId
  const [contexts, setContexts] = useState<KnownContext[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savedKey, setSavedKey] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [list, ctxResponse] = await Promise.all([
        api.calendars(),
        fetch(`${API_BASE}/contexts`, { credentials: 'include' }).then((r) => (r.ok ? r.json() : { contexts: [] })),
      ]);
      setCalendars(list.calendars.filter((c) => c.active));
      const map: Record<string, string> = {};
      for (const d of list.contextDefaults) map[d.tenantContextId ?? 'account'] = d.calendarId;
      setDefaults(map);
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

  const set = async (key: string, calendarId: string) => {
    try {
      await api.setContextDefault({
        tenantContextId: key === 'account' ? null : key,
        calendarId: calendarId || null,
      });
      setDefaults((prev) => {
        const next = { ...prev };
        if (calendarId) next[key] = calendarId;
        else delete next[key];
        return next;
      });
      setSavedKey(key);
      window.setTimeout(() => setSavedKey((k) => (k === key ? null : k)), 2500);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save that.');
    }
  };

  const Picker = ({ keyName }: { keyName: string }) => (
    <div className="flex items-center gap-2">
      <select
        value={defaults[keyName] ?? ''}
        onChange={(e) => set(keyName, e.target.value)}
        className="px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm min-w-[14rem]"
      >
        <option value="">No booking offered</option>
        {calendars.map((c) => (
          <option key={c.id} value={c.id}>{c.isDefault ? `★ ${c.label}` : c.label}</option>
        ))}
      </select>
      {savedKey === keyName && <CheckCircle2 className="w-4 h-4 text-emerald-500" />}
    </div>
  );

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold flex items-center gap-2">
          <Share2 className="w-5 h-5 text-blue-500" /> Assistant booking
        </h1>
        <p className="mt-1 text-sm text-slate-500 dark:text-slate-400 max-w-3xl">
          Choose which calendar the AI books into. Leave a context on “No booking offered” and the assistant will not
          mention appointments there at all — the tools simply do not appear for that conversation.
        </p>
        <p className="mt-1 text-[12px] text-slate-400 max-w-3xl">
          ★ marks each person’s default calendar (set on Calendars). Point a context at someone’s default and it follows
          them if they later star a different calendar of theirs.
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
          Create a calendar first — there is nothing to route to yet.
        </div>
      ) : (
        <>
          <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-4">
            <div className="flex items-center justify-between gap-4 flex-wrap">
              <div>
                <h2 className="text-sm font-medium">Everywhere else</h2>
                <p className="text-[11px] text-slate-400 mt-0.5">
                  Used for any context without a choice of its own — including phone lines whose context is not listed.
                </p>
              </div>
              <Picker keyName="account" />
            </div>
          </div>

          <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016]">
            <div className="px-4 py-3 border-b border-slate-100 dark:border-slate-800">
              <h2 className="text-sm font-medium">Per knowledge base</h2>
            </div>
            {contexts.length === 0 ? (
              <p className="px-4 py-6 text-sm text-slate-500 dark:text-slate-400 flex items-start gap-2">
                <Info className="w-4 h-4 mt-0.5 shrink-0" />
                The list of knowledge bases has not arrived yet. The account-wide choice above still applies to
                everything; open this site from your assistant dashboard once and the list is pushed here.
              </p>
            ) : (
              contexts.map((ctx) => (
                <div
                  key={ctx.tenantContextId}
                  className="px-4 py-3 border-b last:border-b-0 border-slate-100 dark:border-slate-800 flex items-center justify-between gap-4 flex-wrap"
                >
                  <span className="text-sm">{ctx.domain}</span>
                  <Picker keyName={ctx.tenantContextId} />
                </div>
              ))
            )}
          </div>

          <p className="text-[11px] text-slate-400">
            Callers can also ask for a person by name — “put me through to admissions” — and the assistant will match a
            calendar label before falling back to these choices. Account: {me.accountName}.
          </p>
        </>
      )}
    </div>
  );
}
