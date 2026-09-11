namespace DocuCalendar.Infrastructure.Options;

/// <summary>
/// The OAuth apps this server may sync calendars through. Bound from the "Sync" section. Real
/// values live ONLY in the server's appsettings.Production.json — a provider with an empty client
/// id is simply not offered in the UI, so a missing secret degrades to a missing button rather
/// than a broken one.
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>Where the providers send the browser back to. Must match what each console has.</summary>
    public string PublicBaseUrl { get; set; } = "https://calendar.docurest.com";

    public int IntervalMinutes { get; set; } = 5;

    /// <summary>
    /// The blue/green marker file ("blue" or "green"). When set, a slot whose folder does not
    /// match it treats itself as standby and leaves the scheduled runs to the serving slot.
    /// Empty (as in development) means "always run".
    /// </summary>
    public string? ActiveSlotFile { get; set; }

    public MicrosoftSyncApp Microsoft { get; set; } = new();
    public GoogleSyncApp Google { get; set; } = new();
}

public sealed class MicrosoftSyncApp
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>"common" lets both work/school and personal Microsoft accounts sign in.</summary>
    public string Authority { get; set; } = "https://login.microsoftonline.com/common";
}

public sealed class GoogleSyncApp
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}
