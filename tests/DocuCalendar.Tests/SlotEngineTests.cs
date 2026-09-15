using DocuCalendar.Application.Scheduling;

namespace DocuCalendar.Tests;

/// <summary>
/// The slot engine decides what a caller is offered, so these tests are the specification. A bug
/// here does not throw — it sends a person to an office where nobody is waiting, or hides a free
/// hour nobody ever gets offered.
/// </summary>
public class SlotEngineTests
{
    // A fixed +05:00 zone stands in for Tashkent: the point of these tests is the arithmetic, and a
    // custom zone makes them independent of the machine's timezone database.
    private static readonly TimeZoneInfo Tashkent =
        TimeZoneInfo.CreateCustomTimeZone("Test/Tashkent", TimeSpan.FromHours(5), "Test Tashkent", "Test Tashkent");

    private const string OfficeWeek =
        """{"mon":[["09:00","13:00"],["14:00","18:00"]],"tue":[["09:00","13:00"],["14:00","18:00"]],"wed":[["09:00","13:00"],["14:00","18:00"]],"thu":[["09:00","13:00"],["14:00","18:00"]],"fri":[["09:00","13:00"],["14:00","18:00"]],"sat":[],"sun":[]}""";

    private static SchedulingRules Rules(
        int slot = 20, int max = 60, int buffer = 0, int lead = 60, int horizon = 30, string? week = null) =>
        new(slot, max, buffer, lead, horizon, week ?? OfficeWeek);

    /// <summary>A local wall-clock time in the test zone, as an instant.</summary>
    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0) =>
        new(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), TimeSpan.FromHours(5));

    // 2026-09-10 is a Thursday. Early morning, so the whole working day is still ahead: "days: 1"
    // means TODAY, and a fixture starting the evening before would be testing the wrong day.
    private static readonly DateTimeOffset EarlyThursday = Local(2026, 9, 10, 6, 0);

    [Fact]
    public void BusyMorning_IsNeverOffered_AndTheDayResumesAtNoon()
    {
        // The brief's own example: 9/10/2026, 09:00–12:00 marked busy.
        var busy = new[] { new Interval(Local(2026, 9, 10, 9), Local(2026, 9, 10, 12)) };

        var slots = SlotEngine.GetSlots(Rules(), Tashkent, busy, EarlyThursday, days: 1);

        Assert.NotEmpty(slots);
        Assert.Equal(Local(2026, 9, 10, 12), slots[0].StartsAtUtc);
        Assert.DoesNotContain(slots, s => s.StartsAtUtc < Local(2026, 9, 10, 12));
        // …and the rest of the morning window is still usable up to the 13:00 break.
        Assert.Contains(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 12, 40));
        Assert.DoesNotContain(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 13));
    }

    [Fact]
    public void WithNothingBusy_TheDayStartsAtOpening_OnTheSlotGrid()
    {
        var slots = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 1);

        Assert.Equal(Local(2026, 9, 10, 9), slots[0].StartsAtUtc);
        Assert.Equal(Local(2026, 9, 10, 9, 20), slots[1].StartsAtUtc);
        // The lunch hour is not a window, so nothing is offered inside it.
        Assert.DoesNotContain(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 13, 20));
        Assert.Contains(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 14));
    }

    [Fact]
    public void LeadTime_HidesSlotsTooSoonFromNow()
    {
        // 08:30 with a 60-minute lead: 09:00 and 09:20 are too soon, 09:40 is the first honest offer.
        var now = Local(2026, 9, 10, 8, 30);

        var slots = SlotEngine.GetSlots(Rules(lead: 60), Tashkent, Array.Empty<Interval>(), now, days: 1);

        Assert.Equal(Local(2026, 9, 10, 9, 40), slots[0].StartsAtUtc);
    }

    [Fact]
    public void Buffer_KeepsTimeFreeOnBothSidesOfBusy()
    {
        // 10:00–10:20 busy with a 10-minute buffer blocks 09:50 through 10:30.
        var busy = new[] { new Interval(Local(2026, 9, 10, 10), Local(2026, 9, 10, 10, 20)) };

        var slots = SlotEngine.GetSlots(Rules(buffer: 10), Tashkent, busy, EarlyThursday, days: 1);

        Assert.DoesNotContain(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 9, 40)); // would end 10:00, inside the pad
        Assert.DoesNotContain(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 10, 20));
        Assert.Contains(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 9, 20));       // ends 09:40, clear of the pad
        Assert.Contains(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 10, 40));
    }

    [Fact]
    public void TouchingEdges_AreFree_BecauseTheRangeIsHalfOpen()
    {
        // Busy 10:00–10:20 exactly: a slot ending at 10:00 and one starting at 10:20 both survive.
        var busy = new[] { new Interval(Local(2026, 9, 10, 10), Local(2026, 9, 10, 10, 20)) };

        var slots = SlotEngine.GetSlots(Rules(), Tashkent, busy, EarlyThursday, days: 1);

        Assert.Contains(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 9, 40));
        Assert.Contains(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 10, 20));
        Assert.DoesNotContain(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 10));
    }

    [Fact]
    public void ALongerMeeting_MustFitEntirelyInsideAWindow()
    {
        // A 60-minute meeting cannot start at 12:20 (it would run past the 13:00 break).
        var slots = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 1, requestedMinutes: 60);

        Assert.Contains(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 12));   // 12:00–13:00 fits exactly
        Assert.DoesNotContain(slots, s => s.StartsAtUtc == Local(2026, 9, 10, 12, 20));
        Assert.All(slots, s => Assert.Equal(60, (s.EndsAtUtc - s.StartsAtUtc).TotalMinutes));
    }

    [Fact]
    public void Duration_IsClampedToTheCalendarsLimits()
    {
        var rules = Rules(slot: 20, max: 60);

        Assert.Equal(20, SlotEngine.ClampDuration(rules, null));  // default = one slot
        Assert.Equal(20, SlotEngine.ClampDuration(rules, 5));     // below the grid
        Assert.Equal(40, SlotEngine.ClampDuration(rules, 40));
        Assert.Equal(60, SlotEngine.ClampDuration(rules, 240));   // the hour cap the owner asked for
    }

    [Fact]
    public void Weekend_IsSimplyNotBookable()
    {
        // 2026-09-12 is a Saturday; the week above gives it no windows.
        var fridayEvening = Local(2026, 9, 11, 20, 0);

        var slots = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), fridayEvening, days: 2);

        Assert.Empty(slots);
    }

    [Fact]
    public void Horizon_CutsOffTheFuture()
    {
        // A 1-day horizon cannot be widened by asking for thirty.
        var slots = SlotEngine.GetSlots(Rules(horizon: 1), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 30);

        Assert.All(slots, s => Assert.True(s.StartsAtUtc < Local(2026, 9, 11, 0)));
    }

    [Fact]
    public void ADayFullOfAppointments_OffersNothing_RatherThanSomethingWrong()
    {
        var busy = new[]
        {
            new Interval(Local(2026, 9, 10, 9), Local(2026, 9, 10, 13)),
            new Interval(Local(2026, 9, 10, 14), Local(2026, 9, 10, 18)),
        };

        var slots = SlotEngine.GetSlots(Rules(), Tashkent, busy, EarlyThursday, days: 1);

        Assert.Empty(slots);
    }

    [Fact]
    public void TheEngineDoesNotAssumeAZoneWithoutDst()
    {
        // A zone WITH daylight saving must still produce sane local times. Central European Time
        // moves; Tashkent does not — the engine may not be written for either in particular.
        var berlin = FindZone("Europe/Berlin", "W. Europe Standard Time");
        if (berlin is null) return; // no tz database on this machine — the other tests still cover the logic

        var nowUtc = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero); // summer time in Berlin
        var slots = SlotEngine.GetSlots(Rules(lead: 0), berlin, Array.Empty<Interval>(), nowUtc, days: 1);

        Assert.NotEmpty(slots);
        var firstLocal = TimeZoneInfo.ConvertTime(slots[0].StartsAtUtc, berlin);
        Assert.Equal(9, firstLocal.Hour); // 09:00 local means 09:00 local, whatever the offset that day
    }

    [Fact]
    public void IsOfferable_AgreesWithWhatWasOffered()
    {
        var busy = new[] { new Interval(Local(2026, 9, 10, 9), Local(2026, 9, 10, 12)) };
        var rules = Rules();
        var slots = SlotEngine.GetSlots(rules, Tashkent, busy, EarlyThursday, days: 1);

        Assert.True(SlotEngine.IsOfferable(rules, Tashkent, busy, EarlyThursday, slots[0].StartsAtUtc, 20));
        // The busy morning is refused even if a caller asks for it by name.
        Assert.False(SlotEngine.IsOfferable(rules, Tashkent, busy, EarlyThursday, Local(2026, 9, 10, 10), 20));
        // So is a time off the grid, and one beyond the horizon.
        Assert.False(SlotEngine.IsOfferable(rules, Tashkent, busy, EarlyThursday, Local(2026, 9, 10, 12, 7), 20));
        Assert.False(SlotEngine.IsOfferable(rules, Tashkent, busy, EarlyThursday, Local(2026, 12, 10, 12), 20));
        // And a duration the calendar does not allow.
        Assert.False(SlotEngine.IsOfferable(rules, Tashkent, busy, EarlyThursday, slots[0].StartsAtUtc, 240));
    }

    [Fact]
    public void IsOfferable_RefusesASlotTakenSinceItWasOffered()
    {
        var rules = Rules();
        var free = SlotEngine.GetSlots(rules, Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 1);
        var chosen = free[0].StartsAtUtc;

        // Somebody else booked it in the meantime.
        var nowBusy = new[] { new Interval(chosen, chosen.AddMinutes(20)) };

        Assert.False(SlotEngine.IsOfferable(rules, Tashkent, nowBusy, EarlyThursday, chosen, 20));
    }

    [Fact]
    public void AWeekThatCannotBeRead_OffersNothing_AndSaysSo()
    {
        Assert.Empty(SlotEngine.ParseWeek("not json at all {{{"));
        Assert.Empty(SlotEngine.ParseWeek(null));
        Assert.Empty(SlotEngine.ParseWeek("[]"));
        Assert.False(SlotEngine.IsValidWeek("""{"mon":[]}"""));          // present but nothing bookable
        Assert.False(SlotEngine.IsValidWeek("""{"mon":[["18:00","09:00"]]}""")); // backwards window
        Assert.True(SlotEngine.IsValidWeek(OfficeWeek));

        var slots = SlotEngine.GetSlots(Rules(week: "broken"), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 7);
        Assert.Empty(slots);
    }

    [Fact]
    public void TheResultCapStopsAtWholeDays_NotInTheMiddleOfTheWeek()
    {
        // The cap is a menu length for a phone call, and it silently truncated the week view: a
        // week's slots ran out on Tuesday and Wednesday was reported as fully booked while empty.
        // Asking for everything must return everything.
        var capped = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 7);
        var all = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 7, maxResults: int.MaxValue);

        Assert.Equal(SlotEngine.DefaultMaxResults, capped.Count);
        Assert.True(all.Count > capped.Count, "a full working week has more slots than the default cap");

        // Every working day in the range must be represented, the last one included.
        var days = all.Select(s => TimeZoneInfo.ConvertTime(s.StartsAtUtc, Tashkent).Date).Distinct().ToList();
        Assert.Contains(new DateTime(2026, 9, 16), days); // the Wednesday that read as "0 free"
    }

    [Fact]
    public void EverySlotLeavesTheEngineInUtc()
    {
        // Not pedantry: these instants are round-tripped through JSON and handed back as query
        // parameters, and Postgres refuses a timestamptz carrying any offset but zero. A slot
        // built with +05:00 is the correct moment and still throws on the way into the database —
        // which is exactly how it failed the first time it met a real server.
        var slots = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 3);

        Assert.NotEmpty(slots);
        Assert.All(slots, s =>
        {
            Assert.Equal(TimeSpan.Zero, s.StartsAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, s.EndsAtUtc.Offset);
        });
        // …and the moment is still right: 09:00 in a +05:00 zone is 04:00 UTC.
        Assert.Equal(4, slots[0].StartsAtUtc.Hour);
        Assert.Equal("Thu 10 Sep, 09:00", slots[0].Local);
    }

    [Fact]
    public void TheLocalStringIsWhatAPersonWouldSay()
    {
        var slots = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 1);

        Assert.Equal("Thu 10 Sep, 09:00", slots[0].Local);
    }

    private static TimeZoneInfo? FindZone(params string[] ids)
    {
        foreach (var id in ids)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return null;
    }

    [Fact]
    public void APerDayCapSpreadsTheAnswerAcrossDays()
    {
        // Sixty 20-minute slots are three office days; a phone assistant that only ever sees the
        // first six tells every caller "only Wednesday". Capped per day, the same budget reaches
        // a week ahead.
        var slots = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 7, maxResults: 20, maxPerDay: 4);

        var days = slots.Select(s => TimeZoneInfo.ConvertTime(s.StartsAtUtc, Tashkent).Date).Distinct().ToList();
        Assert.Equal(5, days.Count); // Thu, Fri, Mon, Tue, Wed — the weekend is not bookable
        Assert.All(slots.GroupBy(s => TimeZoneInfo.ConvertTime(s.StartsAtUtc, Tashkent).Date), g => Assert.Equal(4, g.Count()));
    }

    [Fact]
    public void AStartDateMovesTheWindowThere()
    {
        // "Is there anything next Tuesday?" — the answer starts on that day, not today.
        var slots = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 1, fromLocalDate: new DateOnly(2026, 9, 15));

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.Equal(new DateTime(2026, 9, 15), TimeZoneInfo.ConvertTime(s.StartsAtUtc, Tashkent).Date));
        Assert.Equal(Local(2026, 9, 15, 9), slots[0].StartsAtUtc);
    }

    [Fact]
    public void AStartDateInThePastMeansToday()
    {
        var slots = SlotEngine.GetSlots(Rules(), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 1, fromLocalDate: new DateOnly(2026, 9, 1));

        Assert.NotEmpty(slots);
        Assert.Equal(new DateTime(2026, 9, 10), TimeZoneInfo.ConvertTime(slots[0].StartsAtUtc, Tashkent).Date);
    }

    // ---- "Bookable" windows from the person's own calendar ----

    [Fact]
    public void BookableWindowsReplaceTheWorkingWeek()
    {
        // The rector marked 10–11 and 16–18. Nothing at 9, even though the week says 9–13.
        var windows = new[] { new Interval(Local(2026, 9, 10, 10), Local(2026, 9, 10, 11)), new Interval(Local(2026, 9, 10, 16), Local(2026, 9, 10, 18)) };

        var slots = SlotEngine.GetSlots(Rules(slot: 30), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 1, windows: windows);

        Assert.Equal(new[] { Local(2026, 9, 10, 10), Local(2026, 9, 10, 10, 30), Local(2026, 9, 10, 16), Local(2026, 9, 10, 16, 30), Local(2026, 9, 10, 17), Local(2026, 9, 10, 17, 30) },
            slots.Select(s => s.StartsAtUtc));
    }

    [Fact]
    public void AWindowOutsideTheWorkingWeekIsOffered()
    {
        // Saturday is "not bookable" in the week, but a Bookable event on Saturday says otherwise.
        var saturday = new[] { new Interval(Local(2026, 9, 12, 10), Local(2026, 9, 12, 12)) };

        var slots = SlotEngine.GetSlots(Rules(slot: 60), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 3, windows: saturday);

        Assert.Equal(new[] { Local(2026, 9, 12, 10), Local(2026, 9, 12, 11) }, slots.Select(s => s.StartsAtUtc));
    }

    [Fact]
    public void ARealMeetingInsideAWindowStillBlocksIt()
    {
        var windows = new[] { new Interval(Local(2026, 9, 10, 9), Local(2026, 9, 10, 11)) };
        var busy = new[] { new Interval(Local(2026, 9, 10, 9), Local(2026, 9, 10, 10)) };

        var slots = SlotEngine.GetSlots(Rules(slot: 30), Tashkent, busy, EarlyThursday, days: 1, windows: windows);

        Assert.Equal(new[] { Local(2026, 9, 10, 10), Local(2026, 9, 10, 10, 30) }, slots.Select(s => s.StartsAtUtc));
    }

    [Fact]
    public void DaysWithoutAWindowOfferNothing_WhileWindowsExist()
    {
        // Thursday has a window; Friday has none — Friday is closed, not "back to the week".
        var windows = new[] { new Interval(Local(2026, 9, 10, 10), Local(2026, 9, 10, 11)) };

        var slots = SlotEngine.GetSlots(Rules(slot: 30), Tashkent, Array.Empty<Interval>(), EarlyThursday, days: 2, windows: windows);

        Assert.All(slots, s => Assert.Equal(new DateTime(2026, 9, 10), TimeZoneInfo.ConvertTime(s.StartsAtUtc, Tashkent).Date));
    }

    [Fact]
    public void ABookedWindowSlotIsOfferable_AndOneOutsideIsNot()
    {
        var windows = new[] { new Interval(Local(2026, 9, 10, 10), Local(2026, 9, 10, 11)) };

        Assert.True(SlotEngine.IsOfferable(Rules(slot: 30), Tashkent, Array.Empty<Interval>(), EarlyThursday, Local(2026, 9, 10, 10, 30), 30, windows));
        Assert.False(SlotEngine.IsOfferable(Rules(slot: 30), Tashkent, Array.Empty<Interval>(), EarlyThursday, Local(2026, 9, 10, 9), 30, windows));
    }
}
