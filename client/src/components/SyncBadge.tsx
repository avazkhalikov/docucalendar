import { useState } from 'react';
import { Link } from 'react-router-dom';
import { RefreshCw, Loader2, CheckCircle2, AlertTriangle, Link2 } from 'lucide-react';
import { api, connectUrl, embedded, type SyncConnection } from '../api';
import { ProviderMark, ago, SYNC_INTERVALS } from './SyncBits';

/**
 * The state of a calendar's link to Outlook or Google, with the two controls people reach for:
 * sync it now, and how often it syncs by itself. Lives on the Schedule page, where somebody
 * looking at their week is the one wondering whether it is up to date.
 */
export default function SyncBadge({
  calendarId, canEdit, mine, connection, onChanged, onError,
}: {
  calendarId: string;
  canEdit: boolean;
  mine: boolean;
  connection?: SyncConnection;
  onChanged: () => Promise<void>;
  onError: (message: string) => void;
}) {
  const [working, setWorking] = useState(false);
  const [result, setResult] = useState<string | null>(null);

  if (!connection) {
    return (
      <div className="inline-flex items-center gap-1.5 rounded-xl border border-dashed border-slate-300 dark:border-slate-700 px-3 py-1.5 text-[11px] text-slate-400">
        <Link2 className="w-3.5 h-3.5" />
        {mine ? (
          <>
            Not linked to Outlook or Google —{' '}
            <Link to="/calendars" className="text-blue-600 dark:text-blue-400 hover:underline">link it on Calendars</Link>
          </>
        ) : (
          'Not linked to Outlook or Google.'
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
    setWorking(true);
    setResult(null);
    try {
      const r = await api.syncNow(calendarId);
      setResult(
        r.ok
          ? `${r.pulled} busy mirrored, ${r.pushed} pushed${r.moved ? `, ${r.moved} moved` : ''}${r.cancelled ? `, ${r.cancelled} cancelled` : ''}`
          : r.error ?? 'Sync failed.',
      );
      await onChanged();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not sync.');
    } finally {
      setWorking(false);
    }
  };

  const changeInterval = async (minutes: number) => {
    try {
      await api.setSyncInterval(calendarId, minutes);
      await onChanged();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not change the sync interval.');
    }
  };

  return (
    <div className="inline-flex items-center gap-x-2.5 gap-y-1 flex-wrap rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] px-3 py-1.5 text-[11px]">
      <span className="inline-flex items-center gap-1.5 text-slate-700 dark:text-slate-200">
        <ProviderMark provider={connection.provider} /> {connection.displayName}
      </span>
      <span className={`inline-flex items-center gap-1 ${tone}`} title={connection.lastSyncError ?? undefined}>
        <StatusIcon className="w-3 h-3" />
        {status === 'connected'
          ? `synced ${ago(connection.lastSyncAt)}`
          : status === 'reconnect'
            ? 'needs reconnecting'
            : 'last sync failed'}
      </span>
      {result && <span className="text-slate-400">· {result}</span>}

      {canEdit && (
        <>
          <select
            value={connection.syncEveryMinutes}
            onChange={(e) => void changeInterval(Number(e.target.value))}
            title="How often this calendar syncs by itself"
            className="rounded-md bg-slate-50 dark:bg-[#0b0b0f] border border-slate-300 dark:border-slate-700 px-1.5 py-0.5 text-[11px]"
          >
            {SYNC_INTERVALS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
          <button
            type="button"
            onClick={syncNow}
            disabled={working || status === 'reconnect'}
            className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-medium"
          >
            {working ? <Loader2 className="w-3 h-3 animate-spin" /> : <RefreshCw className="w-3 h-3" />} Sync now
          </button>
        </>
      )}
      {status === 'reconnect' && mine && (
        <a
          href={connectUrl(connection.provider, calendarId)}
          target={embedded ? '_top' : undefined}
          className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-amber-500 hover:bg-amber-600 text-white font-medium"
        >
          Reconnect
        </a>
      )}
    </div>
  );
}
