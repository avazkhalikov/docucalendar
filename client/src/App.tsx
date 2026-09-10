import { useCallback, useEffect, useState } from 'react';
import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import { CalendarDays, LogOut, Loader2, Users, CalendarClock, Share2, AlertTriangle, BookOpen } from 'lucide-react';
import { api, ApiError, type Me } from './api';
import CalendarsPage from './pages/CalendarsPage';
import SchedulePage from './pages/SchedulePage';
import RoutingPage from './pages/RoutingPage';
import GuidePage from './pages/GuidePage';

export default function App() {
  const [me, setMe] = useState<Me | null>(null);
  const [state, setState] = useState<'loading' | 'ready' | 'signed-out'>('loading');

  const load = useCallback(async () => {
    try {
      setMe(await api.me());
      setState('ready');
    } catch (error) {
      // 401 is not a failure here — it is simply someone who has not come through Docurest yet.
      setState(error instanceof ApiError && error.status === 401 ? 'signed-out' : 'signed-out');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  if (state === 'loading') {
    return (
      <div className="min-h-screen flex items-center justify-center text-slate-400">
        <Loader2 className="w-6 h-6 animate-spin" />
      </div>
    );
  }

  if (state === 'signed-out' || !me) return <SignedOut />;

  const navClass = ({ isActive }: { isActive: boolean }) =>
    `px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
      isActive
        ? 'bg-blue-600 text-white'
        : 'text-slate-600 dark:text-slate-300 hover:bg-slate-200/70 dark:hover:bg-slate-800'
    }`;

  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016]">
        <div className="max-w-6xl mx-auto px-4 py-3 flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-2 mr-2">
            <CalendarDays className="w-5 h-5 text-blue-500" />
            <span className="font-semibold">DocuCalendar</span>
          </div>

          <nav className="flex items-center gap-1">
            <NavLink to="/calendars" className={navClass}>
              <span className="inline-flex items-center gap-1.5"><Users className="w-4 h-4" /> Calendars</span>
            </NavLink>
            <NavLink to="/schedule" className={navClass}>
              <span className="inline-flex items-center gap-1.5"><CalendarClock className="w-4 h-4" /> Schedule</span>
            </NavLink>
            {me.role === 'owner' && (
              <NavLink to="/routing" className={navClass}>
                <span className="inline-flex items-center gap-1.5"><Share2 className="w-4 h-4" /> Assistant booking</span>
              </NavLink>
            )}
            <NavLink to="/guide" className={navClass}>
              <span className="inline-flex items-center gap-1.5"><BookOpen className="w-4 h-4" /> Guide</span>
            </NavLink>
          </nav>

          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="text-slate-500 dark:text-slate-400">
              {me.accountName} · <span className="text-slate-700 dark:text-slate-200">{me.name}</span>
              <span className="ml-1.5 px-1.5 py-0.5 rounded text-[11px] bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
                {me.role}
              </span>
            </span>
            <button
              type="button"
              onClick={async () => {
                await api.logout();
                setState('signed-out');
              }}
              className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              title="Sign out"
            >
              <LogOut className="w-4 h-4" />
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-6xl mx-auto px-4 py-6">
        <Routes>
          <Route path="/" element={<Navigate to="/calendars" replace />} />
          <Route path="/calendars" element={<CalendarsPage me={me} />} />
          <Route path="/schedule" element={<SchedulePage me={me} />} />
          <Route path="/schedule/:calendarId" element={<SchedulePage me={me} />} />
          <Route path="/guide" element={<GuidePage />} />
          <Route
            path="/routing"
            element={me.role === 'owner' ? <RoutingPage me={me} /> : <Navigate to="/calendars" replace />}
          />
          <Route path="*" element={<Navigate to="/calendars" replace />} />
        </Routes>
      </main>
    </div>
  );
}

/**
 * There is no login form here on purpose: this site trusts Docurest and nothing else. Telling
 * someone to go back and click the link is the whole of the recovery path.
 */
function SignedOut() {
  const failed = new URLSearchParams(window.location.search).get('sso') === 'failed';
  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <div className="max-w-md w-full rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-6 text-center">
        <CalendarDays className="w-8 h-8 text-blue-500 mx-auto" />
        <h1 className="mt-3 text-lg font-semibold">DocuCalendar</h1>
        {failed ? (
          <p className="mt-2 text-sm text-amber-600 dark:text-amber-400 flex items-center justify-center gap-1.5">
            <AlertTriangle className="w-4 h-4" /> That sign-in link had expired.
          </p>
        ) : null}
        <p className="mt-2 text-sm text-slate-500 dark:text-slate-400">
          Open this from Docurest — <span className="font-medium">My Calendar</span> in the sidebar. There is no
          separate password here; your Docurest account is the key.
        </p>
        <a
          href="https://docurest.com/app/calendar"
          className="mt-4 inline-block px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium"
        >
          Go to Docurest
        </a>
      </div>
    </div>
  );
}
