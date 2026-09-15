# Booking scripts — how each calendar takes its appointments

_Built 2026-09-15. Follows on from `CALENDAR-PLATFORM-PLAN.md` (the phone tools) and
`EXTERNAL-CALENDAR-SYNC-PLAN.md`._

## Why

The assistant's booking flow was one flow: name → phone read back → offer real slots → 20
minutes → book. Fine for a university line; wrong for everyone else. A dentist needs to know
what hurts and books an hour for a filling; a bank desk wants an account number; a restaurant
wants a party size. The owner of a calendar — or the operator whose calendar it is — should
shape that conversation themselves, without a developer.

## What a script is

Per calendar (`StaffCalendar.BookingScriptJson`), edited on the Calendars page under **How the
assistant books here**. Three parts, all optional:

| Part | What it does on the phone |
|---|---|
| **Questions** (≤ 8, each *required* or optional) | Asked one at a time after name and phone. The answers are stored on the appointment, shown on the Schedule, put into the Outlook/Google event body, and sent in the Telegram/in-app notification. A *required* question with no answer blocks the booking — the tool tells the model to go and ask. |
| **Services** (≤ 12, each with minutes) | The assistant asks which the caller needs and books **that length** instead of the default. "Filling — 60 minutes" on a calendar whose default slot is 20. Validation refuses a service longer than the calendar's *longest allowed*, so it can never offer a length the slot engine will then reject. |
| **Instructions** (≤ 1500 chars) | Free text, followed as-is: "if in pain now, tell them to come straight in and do not book", "we do not book same-day". |

The spine is fixed and not editable: a name, a phone number read back digit by digit, only times
the calendar actually has, one booking per call. That is the part that must never be customised
away, because it is what makes an appointment keepable.

## How it reaches the assistant

Two paths, because a caller may ask for a person whose calendar differs from the line's default:

1. **At call setup** (`AudioSocketListener.ResolveSpecAsync`) the probe that checks "can this
   line book?" now also carries the default calendar's script → `PhoneCallSpec.BookingScript` →
   rendered into the prompt's booking rule before the greeting is spoken.
2. **From the slots tool.** `GET /api/booking/slots` returns the *resolved* calendar's `script`;
   `PhoneBookingTools.GetSlotsAsync` appends it to the tool reply ("THIS CALENDAR HAS ITS OWN
   RULES: …") and remembers it on `CallOutcome.BookingScript`. `book_appointment` then refuses to
   book while a required question is unanswered, and passes `service` and `answers` through.

Both phone brains declare the two new tool parameters — `service` (string) and `answers` (array
of `{question, answer}`) — with the same shapes, uppercase types for Gemini as ever.

## Only people with calendars can be booked

Seen live the day this shipped: a caller asked for a lecturer whose name the assistant had read in
the knowledge base. The slots tool found no calendar with that label, **fell back to the line's
default calendar**, and the assistant announced a meeting with the lecturer — an appointment in
the owner's diary that the lecturer would never know about.

The fallback is gone. `CalendarService.ResolveAsync` returns a `Resolution`: a named hint that
matches nothing is `UnknownStaff`, and `GET /api/booking/slots` answers
`{ unknownStaff, requested, bookable: [labels] }` instead of times. On the phone side that becomes
`DescribeUnknownStaff`: *nobody of that name takes appointments here, you cannot book with them
or promise to email them, these are the people who can be booked — ask the caller whether one of
them will do.* The prompt's booking rule says the same up front: a name or an email address in the
documents is not a calendar, and the assistant cannot send email to anyone.

Hints with no name at all (a plain "I'd like an appointment") still go to the context's default
calendar, as before.

## Pure code, tested

`DocuCalendar.Application.Scheduling.BookingScript`: `Parse` never throws (an unreadable script
means "the standard way", never "cannot book"); `Validate` is strict on the way in (limits,
duplicate service names, a service longer than the ceiling); `FindService` matches how a caller
would say it. `BookingScriptTests` cover each of those. On the Docurest side,
`PhoneBookingTools.MissingRequiredAnswers` / `ReadAnswers` are public and pure for the same
reason.

## Prove it

1. On a calendar, add a required question "What is the problem?" and a service "Filling · 60".
   Save. The row's script is returned by `GET /api/calendars`.
2. Phone in and ask for an appointment: after name and phone, the assistant asks the question,
   asks which service, and books 60 minutes. The Schedule card shows "Name · Filling" and the
   answer; the Telegram message carries both; the Outlook/Google event body lists them.
3. Refuse to answer the question: the assistant asks again rather than booking.
4. Clear the script: the assistant is back to the standard flow.
