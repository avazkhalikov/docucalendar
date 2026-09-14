namespace DocuCalendar.WebApi;

/// <summary>
/// Where this app's pages live in a browser: always under <c>/calendar</c>, on every host that
/// serves it — docurest.com, calendar.docurest.com and each white-label portal.
///
/// The API builds these paths itself rather than letting nginx rewrite redirects, because its
/// redirects leave by two different doors: the SSO hand-off arrives through the portal's
/// <c>/calendar/api/</c> location, but a provider's OAuth callback always lands on
/// calendar.docurest.com's plain <c>/api/</c> location, which serves machines and rewrites
/// nothing. A path that is right in one place and wrong in the other is the kind of bug that only
/// shows up for the person connecting their calendar, once, with no way to retry cleanly.
/// </summary>
public static class UiPaths
{
    public const string Prefix = "/calendar";

    /// <summary>A browser-facing path for an in-app location such as "/calendars?connected=google".</summary>
    public static string Ui(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || localPath == "/") return Prefix + "/";
        return localPath.StartsWith('/') ? Prefix + localPath : $"{Prefix}/{localPath}";
    }
}
