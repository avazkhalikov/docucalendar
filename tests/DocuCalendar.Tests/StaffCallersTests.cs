using DocuCalendar.Application.Scheduling;

namespace DocuCalendar.Tests;

/// <summary>
/// The calendar's staff list — the numbers that unlock "Bookable staff" hours. Read forgivingly,
/// saved strictly, like the booking script.
/// </summary>
public class StaffCallersTests
{
    private const string Two = """[{"name":"Avaz — IT","phone":"+998 90 123 45 67"},{"name":"Achror — accounts","phone":"998901112233"}]""";

    [Fact]
    public void ParsesNamesAndNumbers()
    {
        var list = StaffCallers.Parse(Two);
        Assert.Equal(2, list.Count);
        Assert.Equal("Avaz — IT", list[0].Name);
        Assert.Equal(new[] { "+998 90 123 45 67", "998901112233" }, StaffCallers.Phones(Two));
    }

    [Fact]
    public void AnUnreadableListIsAnEmptyOne_NeverAnError()
    {
        Assert.Empty(StaffCallers.Parse("not json"));
        Assert.Empty(StaffCallers.Parse(null));
        Assert.Empty(StaffCallers.Parse("""[{"name":"no number"}]"""));
    }

    [Fact]
    public void SavingRefusesANumberTooShortToBeOne()
    {
        var problem = StaffCallers.Validate("""[{"name":"Dean","phone":"1234"}]""", out _);
        Assert.NotNull(problem);
        Assert.Contains("Dean", problem);
    }

    [Fact]
    public void SavingDropsBlankRows_AndRoundTrips()
    {
        var problem = StaffCallers.Validate("""[{"name":"","phone":""},{"name":"Dean","phone":"+998 71 200 00 00"}]""", out var list);
        Assert.Null(problem);
        var one = Assert.Single(list);
        Assert.Equal("Dean", one.Name);

        var json = StaffCallers.ToJson(list);
        Assert.NotNull(json);
        Assert.Equal("+998 71 200 00 00", StaffCallers.Parse(json).Single().Phone);
        Assert.Null(StaffCallers.ToJson(Array.Empty<StaffCaller>()));
    }
}
