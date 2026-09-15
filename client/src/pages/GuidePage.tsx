import { Link } from 'react-router-dom';
import {
  CalendarDays, Clock, Share2, Ban, Phone, ArrowRight, Check, Info, Users, CalendarClock, RefreshCw,
} from 'lucide-react';

/**
 * The page a new person reads once and then never needs again.
 *
 * Four steps, because that is genuinely all there is: make a calendar, say when you work, tell the
 * assistant to use it, block what is not free. The centrepiece is the call transcript — people
 * grasp what this product does the moment they see what a caller hears, and no amount of feature
 * description gets them there faster.
 */
export default function GuidePage() {
  return (
    <div className="max-w-3xl mx-auto pb-16">
      {/* Opening: what this is, in one breath. */}
      <header className="text-center pt-4 pb-10">
        <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-[11px] font-medium tracking-wide uppercase bg-blue-50 dark:bg-blue-950/60 text-blue-700 dark:text-blue-300">
          <CalendarDays className="w-3.5 h-3.5" /> Getting started
        </span>
        <h1 className="mt-4 text-3xl sm:text-4xl font-semibold tracking-tight text-slate-900 dark:text-white text-balance">
          Let the assistant book your meetings
        </h1>
        <p className="mt-3 text-[15px] leading-relaxed text-slate-600 dark:text-slate-400 max-w-xl mx-auto">
          Tell it when you are free, and it takes appointments for you — on the phone, in your own
          language, without ever offering a time you cannot keep.
        </p>
        <p className="mt-4 text-[13px] text-slate-400">Four steps, one optional. About three minutes.</p>
      </header>

      <ol className="space-y-4">
        <Step
          n={1}
          icon={Users}
          title="Make a calendar"
          where="Calendars"
          action={{ to: '/calendars', label: 'Open Calendars' }}
        >
          <p>
            One per person or desk. The <strong className="font-medium text-slate-800 dark:text-slate-200">label
            matters</strong>: it is what a caller says out loud. Name it{' '}
            <Quote>Aziza — Admissions</Quote> and someone asking “can I speak to admissions?” reaches
            the right diary.
          </p>
          <p className="mt-2 text-slate-500 dark:text-slate-400">
            Anyone can add their own calendar. Only the account owner adds one for somebody else,
            picking whose it is from the list — your team appears there by itself once this site has
            been opened from your assistant dashboard.
          </p>
        </Step>

        <Step
          n={2}
          icon={Clock}
          title="Say when you work"
          where="Calendars → open a calendar"
        >
          <p>
            Set the working week — mornings, afternoons, the lunch hour in between. A day with no hours
            is simply not bookable, which is how weekends are expressed.
          </p>
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            <Setting label="Appointment length" value="20 minutes" note="the size of one slot" />
            <Setting label="Longest allowed" value="60 minutes" note="even if a caller asks for more" />
            <Setting label="Earliest notice" value="60 minutes" note="no ambushes ten minutes from now" />
            <Setting label="Gap between meetings" value="0 minutes" note="breathing room, if you want it" />
          </div>
          <p className="mt-3 text-slate-500 dark:text-slate-400">
            Further down the same card, <strong className="font-medium text-slate-800 dark:text-slate-200">How the
            assistant books here</strong> is yours to shape — start from one of ten ready-made templates (dental clinic, university, bank, salon, restaurant…) and adjust, or from blank: the questions it asks before booking (“What is the problem
            with your teeth?”), services with their own lengths (a filling is an hour, a consultation twenty minutes),
            and instructions in your own words. Name and phone number are always asked.
          </p>
        </Step>

        <Step
          n={3}
          icon={Share2}
          title="Point the assistant at it"
          where="Assistant booking"
          action={{ to: '/routing', label: 'Open Assistant booking' }}
          emphasis
        >
          <p>
            Choose which calendar the AI books into. <strong className="font-medium text-slate-800 dark:text-slate-200">
            Until you do this, it will not mention appointments at all</strong> — the ability simply is
            not offered to it, so it can never promise a meeting it cannot make.
          </p>
          <p className="mt-2 text-slate-500 dark:text-slate-400">
            “Everywhere else” covers every phone line and chat. You can override it per knowledge base
            once the assistant has sent the list.
          </p>
        </Step>

        <Step
          n={4}
          icon={Ban}
          title="Block what is not free"
          where="Schedule"
          action={{ to: '/schedule', label: 'Open Schedule' }}
        >
          <p>
            Busy for a morning? Block it, and that time stops existing as far as callers are concerned.
            The week grid shows the day as it stands — grey for busy, blue for appointments, a green wash
            where a caller could still be booked — and clicking an empty spot blocks it or books someone in.
            Month view is the same at a glance.
          </p>
          <p className="mt-2 text-slate-500 dark:text-slate-400">
            You can also book someone in by hand — the same rules apply, so you cannot double-book
            yourself by accident.
          </p>
        </Step>

        <Step
          n={5}
          icon={RefreshCw}
          title="Link your Outlook or Google calendar"
          where="Calendars → your calendar"
          action={{ to: '/calendars', label: 'Open Calendars' }}
        >
          <p>
            Optional, and worth it. Press <strong className="font-medium text-slate-800 dark:text-slate-200">Connect
            Outlook 365</strong> or <strong className="font-medium text-slate-800 dark:text-slate-200">Connect Google
            Calendar</strong> on your own calendar and sign in once. From then on, whatever is busy there is busy
            here — the assistant never offers a time you already gave away — and every appointment it books
            lands in your real calendar within seconds, with the visitor’s name and number.
          </p>
          <p className="mt-2 text-slate-500 dark:text-slate-400">
            Move or delete one of those appointments in Outlook or Google and this calendar follows, and the
            team is told. Your event titles stay yours: colleagues see only “Busy”.
          </p>
        </Step>
      </ol>

      {/* The centrepiece: what the caller actually experiences. */}
      <section className="mt-12">
        <div className="flex items-center gap-2 mb-3">
          <Phone className="w-4 h-4 text-blue-500" />
          <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
            What your caller hears
          </h2>
        </div>

        <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016] overflow-hidden">
          <div className="px-4 py-2.5 border-b border-slate-100 dark:border-slate-800 text-[11px] text-slate-400">
            You blocked <span className="font-medium text-slate-500 dark:text-slate-300">09:00–12:00</span> this morning
          </div>
          <div className="p-4 space-y-2.5">
            <Line who="caller">I’d like to book a meeting.</Line>
            <Line who="ai">Of course — may I take your name?</Line>
            <Line who="caller">Aziz Juma.</Line>
            <Line who="ai">Thank you. And a phone number I can note down?</Line>
            <Line who="caller">90 182 26 00.</Line>
            <Line who="ai">
              That’s nine zero, one eight two, two six zero zero — correct? I have{' '}
              <em className="not-italic font-medium">twelve o’clock</em> or{' '}
              <em className="not-italic font-medium">twenty past twelve</em> today. Which suits you?
            </Line>
            <Line who="caller">Twelve o’clock.</Line>
            <Line who="ai">
              Booked — today at twelve, for twenty minutes. See you then.
            </Line>
          </div>
          <div className="px-4 py-3 border-t border-slate-100 dark:border-slate-800 bg-slate-50/60 dark:bg-slate-900/40">
            <p className="text-[12px] text-slate-500 dark:text-slate-400">
              Notice what it never did: offer your blocked morning, invent a time, or book without a name
              and a number it read back first.
            </p>
          </div>
        </div>
      </section>

      {/* The three questions everybody asks next. */}
      <section className="mt-10 grid gap-3 sm:grid-cols-3">
        <Note title="Two callers, one slot">
          The first one gets it. The second is told immediately and offered the next free times — nobody
          is double-booked.
        </Note>
        <Note title="You always hear about it">
          Every booking arrives in your Telegram operator group and your assistant notifications, with the
          visitor’s name and number.
        </Note>
        <Note title="Times are yours">
          Everything is Asia/Tashkent — what you set is what the caller is told, in their own language.
        </Note>
      </section>

      {/* Troubleshooting: the one failure people actually hit. */}
      <section className="mt-10 rounded-2xl border border-amber-200 dark:border-amber-900/60 bg-amber-50/60 dark:bg-amber-950/20 p-4">
        <h2 className="text-sm font-semibold text-amber-900 dark:text-amber-200 flex items-center gap-1.5">
          <Info className="w-4 h-4" /> The assistant says it can’t book?
        </h2>
        <p className="mt-1.5 text-[13px] text-amber-900/80 dark:text-amber-200/80">
          Almost always step 3: no calendar is chosen under <strong>Assistant booking</strong>. Set it,
          wait about five minutes for the phone line to notice, and try again.
        </p>
      </section>

      <div className="mt-10 flex flex-wrap items-center justify-center gap-3">
        <Link
          to="/calendars"
          className="inline-flex items-center gap-1.5 px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium transition-colors"
        >
          Start with step one <ArrowRight className="w-4 h-4" />
        </Link>
        <Link
          to="/schedule"
          className="inline-flex items-center gap-1.5 px-5 py-2.5 rounded-xl border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 text-sm font-medium hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
        >
          <CalendarClock className="w-4 h-4" /> See my week
        </Link>
      </div>
    </div>
  );
}

function Step({
  n, icon: Icon, title, where, children, action, emphasis,
}: {
  n: number;
  icon: typeof Users;
  title: string;
  where: string;
  children: React.ReactNode;
  action?: { to: string; label: string };
  emphasis?: boolean;
}) {
  return (
    <li
      className={`relative rounded-2xl border p-5 transition-colors ${
        emphasis
          ? 'border-blue-300 dark:border-blue-800 bg-blue-50/40 dark:bg-blue-950/20'
          : 'border-slate-200 dark:border-slate-800 bg-white dark:bg-[#101016]'
      }`}
    >
      <div className="flex items-start gap-4">
        <span
          className={`shrink-0 w-9 h-9 rounded-xl grid place-items-center text-sm font-semibold tabular-nums ${
            emphasis
              ? 'bg-blue-600 text-white'
              : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300'
          }`}
        >
          {n}
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <h2 className="text-[17px] font-semibold text-slate-900 dark:text-white">{title}</h2>
            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
              <Icon className="w-3 h-3" /> {where}
            </span>
          </div>
          <div className="mt-2 text-[14px] leading-relaxed text-slate-600 dark:text-slate-300">{children}</div>
          {action && (
            <Link
              to={action.to}
              className="mt-3 inline-flex items-center gap-1 text-[13px] font-medium text-blue-600 dark:text-blue-400 hover:underline"
            >
              {action.label} <ArrowRight className="w-3.5 h-3.5" />
            </Link>
          )}
        </div>
      </div>
    </li>
  );
}

function Setting({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <div className="rounded-xl border border-slate-200 dark:border-slate-800 px-3 py-2">
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-[12px] text-slate-500 dark:text-slate-400">{label}</span>
        <span className="text-[13px] font-medium text-slate-800 dark:text-slate-100 tabular-nums">{value}</span>
      </div>
      <p className="text-[11px] text-slate-400 mt-0.5">{note}</p>
    </div>
  );
}

function Quote({ children }: { children: React.ReactNode }) {
  return (
    <span className="px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-200 text-[13px]">
      “{children}”
    </span>
  );
}

function Line({ who, children }: { who: 'caller' | 'ai'; children: React.ReactNode }) {
  const isAi = who === 'ai';
  return (
    <div className={`flex ${isAi ? 'justify-start' : 'justify-end'}`}>
      <div
        className={`max-w-[85%] rounded-2xl px-3.5 py-2 text-[13.5px] leading-relaxed ${
          isAi
            ? 'bg-blue-50 dark:bg-blue-950/40 text-slate-800 dark:text-slate-100 rounded-tl-sm'
            : 'bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-200 rounded-tr-sm'
        }`}
      >
        {children}
      </div>
    </div>
  );
}

function Note({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-slate-200 dark:border-slate-800 p-3.5">
      <h3 className="text-[13px] font-semibold text-slate-800 dark:text-slate-100 flex items-center gap-1.5">
        <Check className="w-3.5 h-3.5 text-emerald-500 shrink-0" /> {title}
      </h3>
      <p className="mt-1 text-[12.5px] leading-relaxed text-slate-500 dark:text-slate-400">{children}</p>
    </div>
  );
}
