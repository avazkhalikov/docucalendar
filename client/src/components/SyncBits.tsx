import type { SyncProviderKey } from '../api';

/** The provider's mark — a letter on the provider's own blue, so a row reads at a glance. */
export function ProviderMark({ provider, muted }: { provider: SyncProviderKey; muted?: boolean }) {
  const isMicrosoft = provider === 'microsoft';
  return (
    <span
      className={`inline-grid place-items-center w-4 h-4 rounded text-[9px] font-bold leading-none ${
        muted
          ? 'bg-slate-200 dark:bg-slate-800 text-slate-400'
          : isMicrosoft
            ? 'bg-[#0f6cbd] text-white'
            : 'bg-[#1a73e8] text-white'
      }`}
      aria-hidden
    >
      {isMicrosoft ? 'O' : 'G'}
    </span>
  );
}

export function ago(iso: string | null): string {
  if (!iso) return 'never';
  const seconds = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return 'just now';
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} h ago`;
  return `${Math.floor(seconds / 86400)} d ago`;
}

/** The auto-sync choices — the same list the server accepts. 0 means "only when I press Sync now". */
export const SYNC_INTERVALS: Array<{ value: number; label: string }> = [
  { value: 0, label: 'Manual only' },
  { value: 5, label: 'Every 5 min' },
  { value: 10, label: 'Every 10 min' },
  { value: 15, label: 'Every 15 min' },
  { value: 30, label: 'Every 30 min' },
  { value: 60, label: 'Every hour' },
];
