using DocuCalendar.Application.Scheduling;

namespace DocuCalendar.Tests;

/// <summary>
/// A template exists to be applied and saved without edits, so every one of them must pass the
/// same validation the Save button runs — with the appointment lengths the template itself
/// proposes. A template the calendar would refuse is worse than none: it teaches the owner that
/// the feature is broken.
/// </summary>
public class BookingTemplatesTests
{
    public static IEnumerable<object[]> Templates() => BookingTemplates.All.Select(t => new object[] { t.Key });

    private static BookingTemplate Get(string key) => BookingTemplates.All.Single(t => t.Key == key);

    [Fact]
    public void TenTemplates_WithUniqueKeys()
    {
        Assert.Equal(10, BookingTemplates.All.Count);
        Assert.Equal(BookingTemplates.All.Count, BookingTemplates.All.Select(t => t.Key).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryTemplateValidatesWithItsOwnLengths(string key)
    {
        var t = Get(key);
        var error = BookingScript.Validate(t.ScriptJson, t.MaxMinutes, out var script);

        Assert.Null(error);
        Assert.True(t.SlotMinutes >= 5 && t.SlotMinutes <= t.MaxMinutes, $"{key}: slot {t.SlotMinutes} must fit inside {t.MaxMinutes}");
        Assert.All(script.Services, s => Assert.True(s.Minutes % 5 == 0 && s.Minutes >= 5, $"{key}: {s.Name} is {s.Minutes} min"));
        Assert.NotEmpty(script.Questions);
        Assert.NotEmpty(script.Services);
        Assert.False(string.IsNullOrWhiteSpace(script.Instructions), $"{key}: a template without instructions is just a form");
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryTemplateAsksForSomethingSpecificBeyondNameAndPhone(string key)
    {
        // Either a question the booking cannot be made without, or services — when services are
        // listed the assistant must ask which one, which is a required question in all but name.
        // A salon needs no mandatory question, but it does need "haircut or colouring?".
        var script = BookingScript.Parse(Get(key).ScriptJson);
        Assert.True(script.Questions.Any(q => q.Required) || script.Services.Count > 0,
            $"{key}: nothing specific is asked beyond name and phone");
    }

    [Fact]
    public void TheSafetyRulesComeFirst()
    {
        // The clinic and dental scripts open with the emergency rule, because a model reads the
        // first sentence most carefully — and it is the sentence that must never be missed.
        Assert.StartsWith("Chest pain", BookingScript.Parse(Get("clinic").ScriptJson).Instructions);
        Assert.StartsWith("If the caller is in severe pain", BookingScript.Parse(Get("dental").ScriptJson).Instructions);
        Assert.StartsWith("Never ask for", BookingScript.Parse(Get("bank").ScriptJson).Instructions);
    }
}
