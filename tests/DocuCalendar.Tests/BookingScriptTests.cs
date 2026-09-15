using DocuCalendar.Application.Scheduling;

namespace DocuCalendar.Tests;

/// <summary>
/// The per-calendar booking script: what a dentist, a bank desk or a restaurant can put on top
/// of the fixed name-and-phone spine. Parsing must never break a booking; validation must stop a
/// script the phone cannot deliver.
/// </summary>
public class BookingScriptTests
{
    private const string Dentist = """
    {
      "instructions": "Ask whether it hurts now. Emergencies: tell them to come straight in.",
      "questions": [
        { "ask": "What is the problem with your teeth?", "required": true },
        { "ask": "Do you have insurance?", "required": false }
      ],
      "services": [
        { "name": "Consultation", "minutes": 20 },
        { "name": "Cleaning", "minutes": 30 },
        { "name": "Filling", "minutes": 60 }
      ]
    }
    """;

    [Fact]
    public void ParsesTheWholeScript()
    {
        var script = BookingScript.Parse(Dentist);

        Assert.StartsWith("Ask whether it hurts", script.Instructions);
        Assert.Equal(2, script.Questions.Count);
        Assert.True(script.Questions[0].Required);
        Assert.False(script.Questions[1].Required);
        Assert.Equal(3, script.Services.Count);
        Assert.Equal(60, script.Services[2].Minutes);
        Assert.False(script.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"questions\": \"nope\"}")]
    public void AnythingUnreadableIsSimplyTheStandardScript(string? json)
    {
        // A broken script must degrade to "book the normal way", never to "cannot book".
        var script = BookingScript.Parse(json);
        Assert.True(script.IsEmpty);
    }

    [Fact]
    public void BlankQuestionsAndZeroMinuteServicesAreDropped()
    {
        var script = BookingScript.Parse("""{"questions":[{"ask":"  "},{"ask":"Why?"}],"services":[{"name":"X","minutes":0},{"name":"Y","minutes":15}]}""");
        Assert.Single(script.Questions);
        Assert.Single(script.Services);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var script = BookingScript.Parse(Dentist);
        var again = BookingScript.Parse(script.ToJson());
        // Part by part: the record holds lists, and list equality is by reference.
        Assert.Equal(script.Instructions, again.Instructions);
        Assert.Equal(script.Questions, again.Questions);
        Assert.Equal(script.Services, again.Services);
    }

    [Fact]
    public void ValidationAcceptsAGoodScript()
    {
        Assert.Null(BookingScript.Validate(Dentist, maxMinutes: 60, out var script));
        Assert.Equal(3, script.Services.Count);
    }

    [Fact]
    public void AServiceLongerThanTheCalendarAllowsIsRefused()
    {
        // The longest-allowed setting is what the slot engine enforces; a service that exceeds it
        // would be offered and then refused at booking time, in front of the caller.
        var error = BookingScript.Validate(Dentist, maxMinutes: 30, out _);
        Assert.NotNull(error);
        Assert.Contains("longest allowed", error);
    }

    [Fact]
    public void TooManyQuestionsIsRefused()
    {
        var many = "{\"questions\":[" + string.Join(",", Enumerable.Range(1, 9).Select(i => $"{{\"ask\":\"Q{i}?\"}}")) + "]}";
        Assert.Contains("At most", BookingScript.Validate(many, 60, out _));
    }

    [Fact]
    public void DuplicateServiceNamesAreRefused()
    {
        var dup = """{"services":[{"name":"Cleaning","minutes":30},{"name":"cleaning","minutes":45}]}""";
        Assert.Contains("same name", BookingScript.Validate(dup, 60, out _));
    }

    [Fact]
    public void UnreadableJsonIsAValidationError_NotASilentEmpty()
    {
        // On the way IN, a person is watching: tell them. (On the way OUT, Parse stays lenient.)
        Assert.NotNull(BookingScript.Validate("{oops", 60, out _));
    }

    [Theory]
    [InlineData("Filling", "Filling")]
    [InlineData("filling", "Filling")]
    [InlineData("a filling", "Filling")]
    [InlineData("CLEANING ", "Cleaning")]
    public void AServiceIsFoundHoweverTheCallerSaidIt(string said, string expected)
    {
        Assert.Equal(expected, BookingScript.Parse(Dentist).FindService(said)?.Name);
    }

    [Fact]
    public void AnUnknownServiceIsNotGuessedAt()
    {
        Assert.Null(BookingScript.Parse(Dentist).FindService("root canal"));
        Assert.Null(BookingScript.Parse(Dentist).FindService(null));
    }
}
