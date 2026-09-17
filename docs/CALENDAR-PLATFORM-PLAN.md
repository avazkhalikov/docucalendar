
## Slots: a start date and a per-day cap (2026-09-15)

`GET /api/booking/slots` takes `from=YYYY-MM-DD` (the window starts on that local day; a past
day means today) and `perDay=N` (at most N slots per day, so the 60-result budget spans days
instead of exhausting itself on tomorrow). Both came from one phone call: the assistant had been
handed the first six 20-minute slots — all Wednesday morning — and told a caller who asked about
Friday that only Wednesday was free. Docurest now asks `perDay=8`, reads out three per day across
five days, and re-queries with `from` when a caller names a day.

The same call exposed the other bug: "Avaz Xalikov" string-matched only the bare "Avaz" label,
not "Avaz Outlook", and a single match is honoured as named — so the un-starred Google calendar
took the booking. `MatchByLabel` now widens a match to the same person's calendars whose labels
contain each other before the star decides; a desk label is not a variant of a personal diary.

## The Appointments list (2026-09-17)

The Schedule answers "what does Tuesday look like for Aziza". The new **Appointments** tab answers
the question an owner asks out loud: who is coming, across everybody, and whose number do I ring.
`GET /api/appointments` (owner = the whole account, anyone else = their own calendars) takes
`from`/`to` local dates, `calendarId`, `status` (default: everything not cancelled/declined/lapsed,
or `all`), `search` (name, topic, or either phone number — digits only, so "90 189" finds
"+998901892620"), and pages. Pending requests carry Accept/Decline inline, confirmed ones Cancel.

**Two phone numbers, never merged.** `Appointment.VisitorPhone` is what the visitor SAID;
`Appointment.CallerPhone` (migration `AddCallerPhone`) is the caller ID the call actually came
from, passed by Docurest on the book request and already used for matching staff windows. They
disagree more often than you would expect — somebody books from a colleague's phone, or misspeaks
a digit — and the one that rang is the one worth calling back. Null for web, chat, manual bookings
and withheld caller IDs; the list says "not known" rather than pretending. The webhook carries
`callerPhone` too, and the Telegram notification names both only when they differ.
