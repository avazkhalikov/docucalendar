using System.Text.Json;
using DocuCalendar.Infrastructure.Services.Sync;

namespace DocuCalendar.Tests;

/// <summary>
/// The two providers' JSON, as they actually send it, read into the one shape the sync uses.
/// The instants must come out in UTC whatever zone the provider rendered them in, all-day events
/// must land on the right local day, and the free/declined/cancelled flags must be read.
/// </summary>
public class ProviderParsingTests
{
    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    // ------------------------------------------------------------------ Microsoft Graph ----

    private const string GraphPage = """
    {
      "value": [
        {
          "id": "AAMkAGI1",
          "subject": "Dentist",
          "isAllDay": false,
          "showAs": "busy",
          "isCancelled": false,
          "responseStatus": { "response": "organizer" },
          "start": { "dateTime": "2026-09-11T10:00:00.0000000", "timeZone": "Asia/Tashkent" },
          "end":   { "dateTime": "2026-09-11T11:00:00.0000000", "timeZone": "Asia/Tashkent" }
        },
        {
          "id": "AAMkAGI2",
          "subject": "Optional lunch",
          "isAllDay": false,
          "showAs": "free",
          "isCancelled": false,
          "start": { "dateTime": "2026-09-11T13:00:00.0000000", "timeZone": "UTC" },
          "end":   { "dateTime": "2026-09-11T14:00:00.0000000", "timeZone": "UTC" }
        },
        {
          "id": "AAMkAGI3",
          "subject": "Public holiday",
          "isAllDay": true,
          "showAs": "oof",
          "isCancelled": false,
          "start": { "dateTime": "2026-09-12T00:00:00.0000000", "timeZone": "Asia/Tashkent" },
          "end":   { "dateTime": "2026-09-13T00:00:00.0000000", "timeZone": "Asia/Tashkent" }
        },
        {
          "id": "AAMkAGI4",
          "subject": "Declined thing",
          "isAllDay": false,
          "showAs": "busy",
          "isCancelled": false,
          "responseStatus": { "response": "declined" },
          "start": { "dateTime": "2026-09-11T15:00:00.0000000", "timeZone": "Asia/Tashkent" },
          "end":   { "dateTime": "2026-09-11T16:00:00.0000000", "timeZone": "Asia/Tashkent" }
        }
      ]
    }
    """;

    [Fact]
    public void GraphTimesInTheAccountZoneComeOutInUtc()
    {
        using var doc = JsonDocument.Parse(GraphPage);
        var events = MicrosoftCalendarProvider.ParseEvents(doc.RootElement, Tashkent);

        var dentist = events.Single(e => e.Id == "AAMkAGI1");
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 5, 0, 0, TimeSpan.Zero), dentist.StartUtc); // 10:00 +05:00
        Assert.Equal(TimeSpan.Zero, dentist.StartUtc.Offset);
        Assert.True(dentist.IsBusy);
        Assert.False(dentist.IsCancelled);
    }

    [Fact]
    public void GraphUtcTimesAreTakenAsUtc()
    {
        using var doc = JsonDocument.Parse(GraphPage);
        var lunch = MicrosoftCalendarProvider.ParseEvents(doc.RootElement, Tashkent).Single(e => e.Id == "AAMkAGI2");

        Assert.Equal(new DateTimeOffset(2026, 9, 11, 13, 0, 0, TimeSpan.Zero), lunch.StartUtc);
        Assert.False(lunch.IsBusy); // showAs: free
    }

    [Fact]
    public void GraphAllDayEventsCoverTheLocalDay()
    {
        using var doc = JsonDocument.Parse(GraphPage);
        var holiday = MicrosoftCalendarProvider.ParseEvents(doc.RootElement, Tashkent).Single(e => e.Id == "AAMkAGI3");

        Assert.True(holiday.IsAllDay);
        // Midnight Tashkent on the 12th is 19:00 UTC on the 11th.
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 19, 0, 0, TimeSpan.Zero), holiday.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 19, 0, 0, TimeSpan.Zero), holiday.EndUtc);
        Assert.True(holiday.IsBusy); // out of office occupies the day
    }

    [Fact]
    public void GraphDeclinedInvitationsDoNotOccupyTheCalendar()
    {
        using var doc = JsonDocument.Parse(GraphPage);
        var declined = MicrosoftCalendarProvider.ParseEvents(doc.RootElement, Tashkent).Single(e => e.Id == "AAMkAGI4");

        Assert.False(declined.IsBusy);
    }

    // ------------------------------------------------------------------ Google Calendar ----

    private const string GooglePage = """
    {
      "items": [
        {
          "id": "g1",
          "status": "confirmed",
          "summary": "1:1 with the dean",
          "start": { "dateTime": "2026-09-11T10:00:00+05:00" },
          "end":   { "dateTime": "2026-09-11T10:30:00+05:00" }
        },
        {
          "id": "g2",
          "status": "confirmed",
          "summary": "Reminder: pay rent",
          "transparency": "transparent",
          "start": { "dateTime": "2026-09-11T12:00:00Z" },
          "end":   { "dateTime": "2026-09-11T12:15:00Z" }
        },
        {
          "id": "g3",
          "status": "confirmed",
          "summary": "Conference",
          "start": { "date": "2026-09-12" },
          "end":   { "date": "2026-09-13" }
        },
        {
          "id": "g4",
          "status": "cancelled"
        },
        {
          "id": "g5",
          "status": "confirmed",
          "summary": "Declined webinar",
          "attendees": [ { "email": "me@example.com", "self": true, "responseStatus": "declined" } ],
          "start": { "dateTime": "2026-09-11T16:00:00+05:00" },
          "end":   { "dateTime": "2026-09-11T17:00:00+05:00" }
        }
      ]
    }
    """;

    [Fact]
    public void GoogleOffsetTimesComeOutInUtc()
    {
        using var doc = JsonDocument.Parse(GooglePage);
        var dean = GoogleCalendarProvider.ParseEvents(doc.RootElement, Tashkent).Single(e => e.Id == "g1");

        Assert.Equal(new DateTimeOffset(2026, 9, 11, 5, 0, 0, TimeSpan.Zero), dean.StartUtc);
        Assert.Equal(TimeSpan.Zero, dean.StartUtc.Offset);
        Assert.True(dean.IsBusy);
    }

    [Fact]
    public void GoogleTransparentEventsDoNotOccupyTheCalendar()
    {
        using var doc = JsonDocument.Parse(GooglePage);
        var rent = GoogleCalendarProvider.ParseEvents(doc.RootElement, Tashkent).Single(e => e.Id == "g2");

        Assert.False(rent.IsBusy);
    }

    [Fact]
    public void GoogleAllDayEventsCoverTheLocalDayWithAnExclusiveEnd()
    {
        using var doc = JsonDocument.Parse(GooglePage);
        var conf = GoogleCalendarProvider.ParseEvents(doc.RootElement, Tashkent).Single(e => e.Id == "g3");

        Assert.True(conf.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 19, 0, 0, TimeSpan.Zero), conf.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 19, 0, 0, TimeSpan.Zero), conf.EndUtc);
    }

    [Fact]
    public void GoogleCancelledInstancesAreReportedEvenWithoutTimes()
    {
        using var doc = JsonDocument.Parse(GooglePage);
        var gone = GoogleCalendarProvider.ParseEvents(doc.RootElement, Tashkent).Single(e => e.Id == "g4");

        Assert.True(gone.IsCancelled);
    }

    [Fact]
    public void GoogleDeclinedInvitationsDoNotOccupyTheCalendar()
    {
        using var doc = JsonDocument.Parse(GooglePage);
        var declined = GoogleCalendarProvider.ParseEvents(doc.RootElement, Tashkent).Single(e => e.Id == "g5");

        Assert.False(declined.IsBusy);
    }
}
