
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
