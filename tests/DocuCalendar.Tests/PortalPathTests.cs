using DocuCalendar.WebApi;
using DocuCalendar.WebApi.Controllers;

namespace DocuCalendar.Tests;

/// <summary>
/// The two small rules that decide whether a white-label portal works at all: where a redirect
/// sends the browser, and which sites are recognised as ours.
/// </summary>
public class PortalPathTests
{
    [Theory]
    [InlineData("/calendars?connected=google", "/calendar/calendars?connected=google")]
    [InlineData("/schedule/abc", "/calendar/schedule/abc")]
    [InlineData("/", "/calendar/")]
    [InlineData("", "/calendar/")]
    [InlineData(null, "/calendar/")]
    public void EveryInAppRedirectKeepsThePrefix(string? input, string expected)
    {
        // Without this the OAuth callback sends people to a path that only exists on the canonical
        // host, and only when nginx happens to rewrite it.
        Assert.Equal(expected, UiPaths.Ui(input!));
    }

    [Fact]
    public void APathWithoutALeadingSlashIsStillPlacedUnderThePrefix()
    {
        Assert.Equal("/calendar/calendars", UiPaths.Ui("calendars"));
    }

    [Theory]
    [InlineData("ai-assistant.wiut.uz", "ai-assistant.wiut.uz")]
    [InlineData("AI-Assistant.WIUT.uz", "ai-assistant.wiut.uz")]
    [InlineData("https://ai-assistant.wiut.uz", "ai-assistant.wiut.uz")]
    [InlineData("https://ai-assistant.wiut.uz/app/calendar", "ai-assistant.wiut.uz")]
    [InlineData("ai-assistant.wiut.uz/", "ai-assistant.wiut.uz")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void AHostIsRecognisedHoweverItWasWritten(string? input, string expected)
    {
        // The list is pushed by another system and typed by people: half of it in one form and
        // half in the other would silently fail to match, and the failure looks like a refused
        // sign-in rather than a configuration mistake.
        Assert.Equal(expected, EmbedHostsController.Normalise(input));
    }
}
