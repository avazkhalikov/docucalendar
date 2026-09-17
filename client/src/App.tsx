import { useCallback, useEffect, useState } from 'react';
import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import { ListChecks, CalendarDays, LogOut, Loader2, Users, CalendarClock, Share2, AlertTriangle, BookOpen, ExternalLink } from 'lucide-react';
import { api, ApiError, embedded, type Me } from './api';
import { applyTitle, brandingNow, resolveBranding, type Branding } from './branding';
import CalendarsPage from './pages/CalendarsPage';
import SchedulePage from './pages/SchedulePage';
import AppointmentsPage from './pages/AppointmentsPage';
import RoutingPage from './pages/RoutingPage';
import GuidePage from './pages/GuidePage';

export default function App() {
  const [me, setMe] = useState<Me | null>(null);
  const [state, setState] = useState<'loading' | 'ready' | 'signed-out'>('loading');
  // On a white-label portal this site belongs to the university, not to us: whatever the host
  // says it is called is what the header and the tab title show.
  const [branding, setBranding] = useState<Branding>(brandingNow);

  useEffect(() => {
    let cancelled = false;
    void resolveBranding().then((b) => {
      if (cancelled) return;
      setBranding(b);
      applyTitle(b.brand);
    });
    return () => {
      cancelled = true;
    };
  }, []);

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

  if (state === 'signed-out' || !me) return <SignedOut branding={branding} />;

  const navClass = ({ isActive }: { isActive: boolean }) =>
    `px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
      isActive
        ? 'bg-blue-600 text-white'
        : 'text-slate-600 dark:text-slate-300 hover:bg-slate-200/70 dark:hover:bg-slate-800'
    }`;

  // Inside Docurest's page the shell around us is Docurest's: no second brand, no second
  // account line, no sign-out that would only sign out the frame. The navigation stays.
  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016]">
        <div className={`max-w-6xl mx-auto px-4 flex items-center gap-3 flex-wrap ${embedded ? 'py-2' : 'py-3'}`}>
          {!embedded && (
            <div className="flex items-center gap-2 mr-2">
              {branding.logoUrl ? (
                <img src={branding.logoUrl} alt="" className="w-5 h-5 rounded object-contain" />
              ) : (
                <CalendarDays className="w-5 h-5 text-blue-500" />
              )}
              <span className="font-semibold">{branding.brand}</span>
            </div>
          )}

          <nav className="flex items-center gap-1">
            <NavLink to="/calendars" className={navClass}>
              <span className="inline-flex items-center gap-1.5"><Users className="w-4 h-4" /> Calendars</span>
            </NavLink>
            <NavLink to="/schedule" className={navClass}>
              <span className="inline-flex items-center gap-1.5"><CalendarClock className="w-4 h-4" /> Schedule</span>
            </NavLink>
            <NavLink to="/appointments" className={navClass}>
              <span className="inline-flex items-center gap-1.5"><ListChecks className="w-4 h-4" /> Appointments</span>
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

          {embedded ? (
            <a
              href="/calendar/"
              target="_blank"
              rel="noreferrer"
              className="ml-auto inline-flex items-center gap-1 text-[12px] text-slate-400 hover:text-blue-500"
              title="Open the calendar in its own tab"
            >
              Own tab <ExternalLink className="w-3.5 h-3.5" />
            </a>
          ) : (
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
          )}
        </div>
      </header>

      <main className={`max-w-6xl mx-auto px-4 ${embedded ? 'py-4' : 'py-6'}`}>
        <Routes>
          <Route path="/" element={<Navigate to="/calendars" replace />} />
          <Route path="/calendars" element={<CalendarsPage me={me} />} />
          <Route path="/schedule" element={<SchedulePage me={me} />} />
          <Route path="/schedule/:calendarId" element={<SchedulePage me={me} />} />
          <Route path="/appointments" element={<AppointmentsPage me={me} />} />
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
function SignedOut({ branding }: { branding: Branding }) {
  const failed = new URLSearchParams(window.location.search).get('sso') === 'failed';
  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <div className="max-w-md w-full rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] p-6 text-center">
        <CalendarDays className="w-8 h-8 text-blue-500 mx-auto" />
        <h1 className="mt-3 text-lg font-semibold">{branding.brand}</h1>
        {failed ? (
          <p className="mt-2 text-sm text-amber-600 dark:text-amber-400 flex items-center justify-center gap-1.5">
            <AlertTriangle className="w-4 h-4" /> That sign-in link had expired.
          </p>
        ) : null}
        {embedded ? (
          // Inside Docurest a dead session means the sign-in link this frame was opened with has
          // expired; reloading the page mints a fresh one.
          <p className="mt-2 text-sm text-slate-500 dark:text-slate-400">
            Your calendar session has ended. Reload this page to sign in again.
          </p>
        ) : (
          <>
            <p className="mt-2 text-sm text-slate-500 dark:text-slate-400">
              Open this from <span className="font-medium">My Calendar</span> in the sidebar of your assistant. There is
              no separate password here; your existing account is the key.
            </p>
            {/* Host-relative: the app lives on whichever host served this page. */}
            <a
              href="/app/calendar"
              className="mt-4 inline-block px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium"
            >
              Go to My Calendar
            </a>
          </>
        )}
      </div>
    </div>
  );
}
