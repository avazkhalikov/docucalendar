import { useCallback, useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import {
  Loader2, Plus, Save, CalendarClock, Archive, ChevronDown, ChevronRight, Info, BookOpen,
  RefreshCw, Unlink, AlertTriangle, CheckCircle2, Link2,
} from 'lucide-react';
import {
  api, connectUrl, embedded, type CalendarRow, type Me, type Person, type SyncConnection, type SyncProvider, type SyncProviderKey,
} from '../api';

/**
 * Google and Microsoft refuse to render their sign-in inside a frame, so from within Docurest
 * the connect links take over the whole tab; the callback brings it back to Docurest afterwards.
 */
const connectTarget = embedded ? '_top' : undefined;
import WeekEditor from '../components/WeekEditor';
import { ProviderMark, ago, SYNC_INTERVALS } from '../components/SyncBits';

/** What the provider round trip can come back with, in words a person can act on. */
const CONNECT_ERRORS: Record<string, string> = {
  denied: 'The sign-in was cancelled, so nothing was connected.',
  expired: 'That connection attempt took too long — press Connect again.',
  norefresh: 'The provider did not grant lasting access. Press Connect again and approve everything it asks for.',
  exchange: 'The provider would not complete the connection. Try again in a moment; if it keeps failing, tell the administrator.',
  missing: 'That calendar no longer exists.',
  provider: 'Unknown calendar provider.',
};

const PROVIDER_NAMES: Record<SyncProviderKey, string> = { microsoft: 'Outlook 365', google: 'Google Calendar' };

export default function CalendarsPage({ me }: { me: Me }) {
  const [rows, setRows] = useState<CalendarRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [newLabel, setNewLabel] = useState('');
  const [newOwnerId, setNewOwnerId] = useState('');
  const [creating, setCreating] = useState(false);
  const [people, setPeople] = useState<Person[]>([]);
  const [providers, setProviders] = useState<SyncProvider[]>([]);
  const [connections, setConnections] = useState<SyncConnection[]>([]);
  const location = useLocation();
  const navigate = useNavigate();

  const loadSync = useCallback(async () => {
    try {
      setConnections((await api.syncConnections()).connections);
    } catch {
      /* the sync line simply stays quiet */
    }
  }, []);

  useEffect(() => {
    // A missing list is not an error — it just means Docurest has not pushed the team yet, and the
    // picker falls back to "Mine", which is what a single-handed account wants anyway.
    api.people().then((r) => setPeople(r.people)).catch(() => setPeople([]));
    api.syncProviders().then((r) => setProviders(r.providers)).catch(() => setProviders([]));
    void loadSync();
  }, [loadSync]);

  // Back from Microsoft or Google: say what happened, then clean the address bar so a refresh
  // does not say it again.
  useEffect(() => {
    const params = new URLSearchParams(location.search);
    const connected = params.get('connected');
    const connectError = params.get('connectError');
    if (!connected && !connectError) return;

    if (connected) {
      setNotice(`${PROVIDER_NAMES[connected as SyncProviderKey] ?? connected} connected — the first sync is running now.`);
      // The first sync takes a few seconds; look again so the line shows its result.
      setTimeout(() => void loadSync(), 4000);
    } else if (connectError) {
      setError(CONNECT_ERRORS[connectError] ?? 'The calendar could not be connected.');
    }
    navigate('/calendars', { replace: true });
  }, [location.search, navigate, loadSync]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows((await api.calendars()).calendars);
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

  const create = async () => {
    if (!newLabel.trim()) return;
    setCreating(true);
    try {
      await api.createCalendar({
        label: newLabel.trim(),
        ownerUserId: newOwnerId.trim() || undefined,
      });
      setNewLabel('');
      setNewOwnerId('');
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the calendar.');
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold">Calendars</h1>
        <p className="mt-1 text-sm text-slate-500 dark:text-slate-400 max-w-3xl">
          One calendar per person or desk. The label is what a caller asks for — say “Aziza — Admissions”, and the
          assistant can find it when somebody asks for admissions. Times are always in {me.timeZoneId}.
        </p>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 dark:border-red-900 bg-red-50 dark:bg-red-950/40 px-4 py-3 text-sm text-red-700 dark:text-red-300">
          {error}
        </div>
      )}
      {notice && (
        <div className="rounded-lg border border-emerald-200 dark:border-emerald-900 bg-emerald-50 dark:bg-emerald-950/40 px-4 py-3 text-sm text-emerald-700 dark:text-emerald-300 flex items-center gap-2">
          <CheckCircle2 className="w-4 h-4 shrink-0" /> {notice}
        </div>
      )}

      {me.role === 'owner' && (
        <div className="rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-4">
          <h2 className="text-sm font-medium mb-2">Add a calendar</h2>
          <div className="flex flex-wrap items-end gap-2">
            <div className="flex-1 min-w-[16rem]">
              <label className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">Label</label>
              <input
                value={newLabel}
                onChange={(e) => setNewLabel(e.target.value)}
                placeholder="Aziza — Admissions"
                className="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
              />
            </div>
            <div className="min-w-[16rem]">
              <label className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">Whose calendar is it?</label>
              <select
                value={newOwnerId}
                onChange={(e) => setNewOwnerId(e.target.value)}
                className="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
              >
                <option value="">Mine</option>
                {people
                  .filter((p) => p.userId !== me.userId)
                  .map((p) => (
                    <option key={p.userId} value={p.userId}>
                      {p.name}{p.role === 'owner' ? ' (owner)' : ''}
                    </option>
                  ))}
              </select>
            </div>
            <button
              type="button"
              onClick={create}
              disabled={creating || !newLabel.trim()}
              className="px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium inline-flex items-center gap-1.5"
            >
              {creating ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />} Create
            </button>
          </div>
          <p className="mt-2 text-[11px] text-slate-400 flex items-start gap-1">
            <Info className="w-3.5 h-3.5 mt-px shrink-0" />
            {people.length > 1
              ? 'Whoever it belongs to can edit it and set their own hours; you can edit every calendar on the account.'
              : 'Your team appears here automatically — open this site from Docurest once and the list arrives.'}
          </p>
        </div>
      )}

      {loading ? (
        <div className="py-10 flex justify-center text-slate-400"><Loader2 className="w-5 h-5 animate-spin" /></div>
      ) : rows.length === 0 ? (
        // An empty account is exactly when somebody needs the guide, so it is offered here rather
        // than left to be discovered in the menu.
        <div className="rounded-xl border border-dashed border-slate-300 dark:border-slate-700 py-10 px-6 text-center">
          <p className="text-sm text-slate-500 dark:text-slate-400">
            No calendars yet.{me.role === 'owner' ? ' Create the first one above.' : ' Ask the account owner to set one up for you.'}
          </p>
          <Link
            to="/guide"
            className="mt-3 inline-flex items-center gap-1.5 text-sm font-medium text-blue-600 dark:text-blue-400 hover:underline"
          >
            <BookOpen className="w-4 h-4" /> Read the four-step guide first
          </Link>
        </div>
      ) : (
        <div className="space-y-3">
          {rows.map((row) => (
            <CalendarCard
              key={row.id}
              row={row}
              expanded={open === row.id}
              onToggle={() => setOpen(open === row.id ? null : row.id)}
              onSaved={load}
              onError={setError}
              isOwner={me.role === 'owner'}
              ownerName={people.find((p) => p.userId === row.ownerUserId)?.name}
              providers={providers}
              connection={connections.find((c) => c.calendarId === row.id)}
              onSyncChanged={loadSync}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function CalendarCard({
  row,
  expanded,
  onToggle,
  onSaved,
  onError,
  isOwner,
  ownerName,
  providers,
  connection,
  onSyncChanged,
}: {
  row: CalendarRow;
  expanded: boolean;
  onToggle: () => void;
  onSaved: () => Promise<void>;
  onError: (message: string) => void;
  isOwner: boolean;
  ownerName?: string;
  providers: SyncProvider[];
  connection?: SyncConnection;
  onSyncChanged: () => Promise<void>;
}) {
  const [draft, setDraft] = useState(row);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => setDraft(row), [row]);

  const save = async () => {
    setSaving(true);
    setSaved(false);
    try {
      await api.updateCalendar(row.id, {
        label: draft.label,
        slotMinutes: draft.slotMinutes,
        maxMinutes: draft.maxMinutes,
        bufferMinutes: draft.bufferMinutes,
        minLeadMinutes: draft.minLeadMinutes,
        horizonDays: draft.horizonDays,
        weeklyAvailability: draft.weeklyAvailability,
      });
      setSaved(true);
      await onSaved();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not save.');
    } finally {
      setSaving(false);
    }
  };

  const retire = async () => {
    if (!window.confirm(`Retire "${row.label}"? Existing appointments stay, but nothing new can be booked.`)) return;
    try {
      await api.deactivateCalendar(row.id);
      await onSaved();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not retire the calendar.');
    }
  };

  const num = (value: string, fallback: number) => {
    const parsed = parseInt(value, 10);
    return Number.isNaN(parsed) ? fallback : parsed;
  };

  return (
    <div className={`rounded-xl border bg-white dark:bg-[#101016] ${row.active ? 'border-slate-200 dark:border-slate-800' : 'border-slate-200/60 dark:border-slate-800/60 opacity-70'}`}>
      <div className="flex items-center gap-3 px-4 pt-3 pb-2 flex-wrap">
        <button type="button" onClick={onToggle} className="text-slate-400 hover:text-blue-500">
          {expanded ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
        </button>
        <span className="font-medium">{row.label}</span>
        {row.mine ? (
          <span className="px-1.5 py-0.5 rounded text-[11px] bg-blue-50 dark:bg-blue-950 text-blue-600 dark:text-blue-300">mine</span>
        ) : ownerName ? (
          // Whose day this is, by name — the roster is unreadable when every row looks the same.
          <span className="px-1.5 py-0.5 rounded text-[11px] bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">{ownerName}</span>
        ) : null}
        {!row.active && <span className="px-1.5 py-0.5 rounded text-[11px] bg-slate-100 dark:bg-slate-800 text-slate-500">retired</span>}
        <span className="text-xs text-slate-400">
          {row.slotMinutes} min slots · up to {row.maxMinutes} min · {row.minLeadMinutes} min notice
        </span>
        <div className="ml-auto flex items-center gap-3">
          <Link
            to={`/schedule/${row.id}`}
            className="text-sm text-blue-600 dark:text-blue-400 hover:underline inline-flex items-center gap-1"
          >
            <CalendarClock className="w-4 h-4" /> Schedule
          </Link>
          {isOwner && row.active && (
            <button type="button" onClick={retire} className="text-slate-400 hover:text-red-500" title="Retire this calendar">
              <Archive className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>

      {/* The link to the person's real calendar — always visible, because a calendar that is
          not linked is the one thing on this page most worth noticing. */}
      {row.active && (
        <div className="px-4 pb-3 pl-11">
          <SyncLine
            row={row}
            providers={providers}
            connection={connection}
            ownerName={ownerName}
            onChanged={onSyncChanged}
            onError={onError}
          />
        </div>
      )}

      {expanded && (
        <div className="border-t border-slate-100 dark:border-slate-800 px-4 py-4 space-y-4">
          {!row.canEdit && (
            <p className="text-xs text-slate-400">This is someone else's calendar — you can see how it is set up, but not change it.</p>
          )}

          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <Field label="Label">
              <input
                value={draft.label}
                disabled={!row.canEdit}
                onChange={(e) => setDraft({ ...draft, label: e.target.value })}
                className="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
              />
            </Field>
            <Field label="Appointment length (minutes)">
              <input
                type="number" min={5} max={240}
                value={draft.slotMinutes}
                disabled={!row.canEdit}
                onChange={(e) => setDraft({ ...draft, slotMinutes: num(e.target.value, row.slotMinutes) })}
                className="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
              />
            </Field>
            <Field label="Longest allowed (minutes)">
              <input
                type="number" min={draft.slotMinutes} max={480}
                value={draft.maxMinutes}
                disabled={!row.canEdit}
                onChange={(e) => setDraft({ ...draft, maxMinutes: num(e.target.value, row.maxMinutes) })}
                className="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
              />
            </Field>
            <Field label="Gap around appointments (minutes)">
              <input
                type="number" min={0} max={120}
                value={draft.bufferMinutes}
                disabled={!row.canEdit}
                onChange={(e) => setDraft({ ...draft, bufferMinutes: num(e.target.value, row.bufferMinutes) })}
                className="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
              />
            </Field>
            <Field label="Earliest notice (minutes)">
              <input
                type="number" min={0} max={10080}
                value={draft.minLeadMinutes}
                disabled={!row.canEdit}
                onChange={(e) => setDraft({ ...draft, minLeadMinutes: num(e.target.value, row.minLeadMinutes) })}
                className="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
              />
            </Field>
            <Field label="Book up to (days ahead)">
              <input
                type="number" min={1} max={365}
                value={draft.horizonDays}
                disabled={!row.canEdit}
                onChange={(e) => setDraft({ ...draft, horizonDays: num(e.target.value, row.horizonDays) })}
                className="w-full px-3 py-2 rounded-lg bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 text-sm"
              />
            </Field>
          </div>

          <div>
            <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-2">Working week</p>
            <WeekEditor
              value={draft.weeklyAvailability}
              disabled={!row.canEdit}
              onChange={(json) => setDraft({ ...draft, weeklyAvailability: json })}
            />
          </div>

          {row.canEdit && (
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={save}
                disabled={saving}
                className="px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium inline-flex items-center gap-1.5"
              >
                {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />} Save
              </button>
              {saved && <span className="text-xs text-emerald-600 dark:text-emerald-400">Saved.</span>}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * One line per calendar about its link to the person's real calendar. Three states: not linked
 * (offer the providers — to the calendar's own person only, because the account that signs in
 * would be theirs), linked and healthy, linked and needing attention.
 */
function SyncLine({
  row, providers, connection, ownerName, onChanged, onError,
}: {
  row: CalendarRow;
  providers: SyncProvider[];
  connection?: SyncConnection;
  ownerName?: string;
  onChanged: () => Promise<void>;
  onError: (message: string) => void;
}) {
  const [working, setWorking] = useState<'sync' | 'disconnect' | null>(null);
  const [result, setResult] = useState<string | null>(null);

  if (!connection) {
    if (!row.mine) {
      return (
        <p className="text-[11px] text-slate-400 inline-flex items-center gap-1">
          <Link2 className="w-3 h-3" /> Not linked to Outlook or Google — {ownerName ?? 'its owner'} can connect their own from their login.
        </p>
      );
    }
    if (providers.length === 0) return null;
    return (
      <div className="flex items-center gap-2 flex-wrap text-[11px] text-slate-400">
        <span className="inline-flex items-center gap-1"><Link2 className="w-3 h-3" /> Link your real calendar:</span>
        {providers.map((p) =>
          p.configured ? (
            <a
              key={p.key}
              href={connectUrl(p.key, row.id)}
              target={connectTarget}
              className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md border border-slate-300 dark:border-slate-700 text-[12px] font-medium text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
            >
              <ProviderMark provider={p.key} /> Connect {p.displayName}
            </a>
          ) : (
            <span
              key={p.key}
              title="Not set up on this server yet — ask the administrator"
              className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md border border-dashed border-slate-300 dark:border-slate-700 text-[12px] text-slate-400 cursor-not-allowed"
            >
              <ProviderMark provider={p.key} muted /> {p.displayName} — not set up
            </span>
          ),
        )}
      </div>
    );
  }

  const { status } = connection;
  const tone =
    status === 'connected'
      ? 'text-emerald-600 dark:text-emerald-400'
      : status === 'reconnect'
        ? 'text-amber-600 dark:text-amber-400'
        : 'text-red-600 dark:text-red-400';
  const StatusIcon = status === 'connected' ? CheckCircle2 : AlertTriangle;

  const syncNow = async () => {
    setWorking('sync');
    setResult(null);
    try {
      const r = await api.syncNow(row.id);
      setResult(
        r.ok
          ? `Synced — ${r.pulled} busy mirrored, ${r.pushed} pushed${r.moved ? `, ${r.moved} moved` : ''}${r.cancelled ? `, ${r.cancelled} cancelled` : ''}.`
          : r.error ?? 'Sync failed.',
      );
      await onChanged();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not sync.');
    } finally {
      setWorking(null);
    }
  };

  const disconnect = async () => {
    if (!window.confirm(`Disconnect ${connection.displayName}? Mirrored busy time disappears from here; the events already in ${connection.displayName} stay.`)) return;
    setWorking('disconnect');
    try {
      await api.disconnectSync(row.id);
      await onChanged();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not disconnect.');
    } finally {
      setWorking(null);
    }
  };

  return (
    <div className="flex items-center gap-x-3 gap-y-1 flex-wrap text-[11px]">
      <span className="inline-flex items-center gap-1.5 text-slate-600 dark:text-slate-300">
        <ProviderMark provider={connection.provider} /> {connection.displayName}
        <span className="text-slate-400">·</span>
        <span className="text-slate-500 dark:text-slate-400">{connection.accountEmail}</span>
      </span>
      <span className={`inline-flex items-center gap-1 ${tone}`}>
        <StatusIcon className="w-3 h-3" />
        {status === 'connected' ? `synced ${ago(connection.lastSyncAt)}` : status === 'reconnect' ? 'needs reconnecting' : 'last sync failed'}
      </span>
      {status === 'connected' && (
        <span className="text-slate-400 tabular-nums">{connection.lastPulled} busy mirrored · {connection.lastPushed} pushed</span>
      )}
      {status !== 'connected' && connection.lastSyncError && (
        <span className="text-slate-400 truncate max-w-[26rem]" title={connection.lastSyncError}>{connection.lastSyncError}</span>
      )}
      {result && <span className="text-slate-500 dark:text-slate-400">{result}</span>}
      {row.canEdit && (
        <span className="inline-flex items-center gap-3 ml-auto">
          <select
            value={connection.syncEveryMinutes}
            onChange={async (e) => {
              try {
                await api.setSyncInterval(row.id, Number(e.target.value));
                await onChanged();
              } catch (err) {
                onError(err instanceof Error ? err.message : 'Could not change the sync interval.');
              }
            }}
            title="How often this calendar syncs by itself"
            className="rounded-md bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 px-1.5 py-0.5 text-[11px]"
          >
            {SYNC_INTERVALS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
          {status === 'reconnect' && row.mine && (
            <a
              href={connectUrl(connection.provider, row.id)}
              target={connectTarget}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-md bg-amber-500 hover:bg-amber-600 text-white font-medium"
            >
              Reconnect
            </a>
          )}
          <button
            type="button"
            onClick={syncNow}
            disabled={working !== null || status === 'reconnect'}
            className="inline-flex items-center gap-1 text-blue-600 dark:text-blue-400 hover:underline disabled:opacity-50 disabled:no-underline"
          >
            {working === 'sync' ? <Loader2 className="w-3 h-3 animate-spin" /> : <RefreshCw className="w-3 h-3" />} Sync now
          </button>
          <button
            type="button"
            onClick={disconnect}
            disabled={working !== null}
            className="inline-flex items-center gap-1 text-slate-400 hover:text-red-500 disabled:opacity-50"
          >
            {working === 'disconnect' ? <Loader2 className="w-3 h-3 animate-spin" /> : <Unlink className="w-3 h-3" />} Disconnect
          </button>
        </span>
      )}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">{label}</span>
      {children}
    </label>
  );
}
