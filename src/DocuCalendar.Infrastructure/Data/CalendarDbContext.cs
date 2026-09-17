using DocuCalendar.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocuCalendar.Infrastructure.Data;

public class CalendarDbContext : DbContext
{
    public CalendarDbContext(DbContextOptions<CalendarDbContext> options) : base(options) { }

    public DbSet<TenantRegistration> Tenants => Set<TenantRegistration>();
    public DbSet<StaffCalendar> Calendars => Set<StaffCalendar>();
    public DbSet<ContextDefault> ContextDefaults => Set<ContextDefault>();
    public DbSet<BusyBlock> BusyBlocks => Set<BusyBlock>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<KnownContext> KnownContexts => Set<KnownContext>();
    public DbSet<KnownPerson> KnownPeople => Set<KnownPerson>();
    public DbSet<ExternalConnection> ExternalConnections => Set<ExternalConnection>();
    public DbSet<EmbedHost> EmbedHosts => Set<EmbedHost>();

    /// <summary>
    /// Every table this service owns lives under one schema of its own.
    ///
    /// The plan was a separate database, and that is still the right shape: this service would then
    /// hold credentials that reach nothing but calendars. It shares a database instead because
    /// creating one needs a Postgres superuser nobody could produce, and a schema was the closest
    /// isolation available without it — the tables are namespaced, the migration history is
    /// separate, and neither system's migrations can see the other's.
    ///
    /// What that costs, recorded so it is not forgotten: the connection string this service uses
    /// also reaches the host application's data. Moving to a database of its own later is a
    /// connection-string change plus a dump/restore of this one schema — no code changes.
    /// </summary>
    public const string SchemaName = "calendar";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(SchemaName);

        b.Entity<TenantRegistration>(e =>
        {
            e.HasKey(t => t.TenantId);
            e.Property(t => t.TenantId).HasMaxLength(100);
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.ApiKeyHash).HasMaxLength(100);
            // Required with a default: a backfilled empty string is not a timezone, and every
            // hour this service renders would silently become UTC.
            e.Property(t => t.TimeZoneId).HasMaxLength(100).IsRequired().HasDefaultValue("Asia/Tashkent");
        });

        b.Entity<StaffCalendar>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.TenantId).HasMaxLength(100).IsRequired();
            e.Property(c => c.Label).HasMaxLength(200).IsRequired();
            // Every number here needs its default declared: EF backfills int columns with 0, and a
            // calendar with SlotMinutes 0 would offer infinitely many appointments of no length.
            e.Property(c => c.SlotMinutes).HasDefaultValue(20);
            e.Property(c => c.MaxMinutes).HasDefaultValue(60);
            e.Property(c => c.BufferMinutes).HasDefaultValue(0);
            e.Property(c => c.MinLeadMinutes).HasDefaultValue(60);
            e.Property(c => c.HorizonDays).HasDefaultValue(30);
            e.Property(c => c.Active).HasDefaultValue(true);
            e.Property(c => c.IsDefault).HasDefaultValue(false);
            e.Property(c => c.WeeklyAvailabilityJson).HasDefaultValue(StaffCalendar.DefaultWeek);
            e.Property(c => c.BookingScriptJson).HasMaxLength(8000);
            e.Property(c => c.StaffCallersJson).HasMaxLength(16000);
            e.HasIndex(c => new { c.TenantId, c.Active });
            e.HasIndex(c => c.OwnerUserId);
        });

        b.Entity<ContextDefault>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.TenantId).HasMaxLength(100).IsRequired();
            // One default per context, and one account-wide fallback (TenantContextId null).
            e.HasIndex(d => new { d.TenantId, d.TenantContextId }).IsUnique();
        });

        b.Entity<KnownContext>(e =>
        {
            e.HasKey(k => new { k.TenantId, k.TenantContextId });
            e.Property(k => k.TenantId).HasMaxLength(100);
            e.Property(k => k.Domain).HasMaxLength(300).IsRequired();
        });

        b.Entity<KnownPerson>(e =>
        {
            e.HasKey(p => new { p.TenantId, p.UserId });
            e.Property(p => p.TenantId).HasMaxLength(100);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Role).HasMaxLength(20).IsRequired().HasDefaultValue("operator");
        });

        b.Entity<EmbedHost>(e =>
        {
            e.HasKey(h => new { h.TenantId, h.Host });
            e.Property(h => h.TenantId).HasMaxLength(100);
            e.Property(h => h.Host).HasMaxLength(253); // the longest a hostname may be
        });

        b.Entity<BusyBlock>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.Source).HasMaxLength(30).IsRequired().HasDefaultValue("manual");
            // Graph event ids run to a couple of hundred characters; Google's may reach a thousand.
            e.Property(x => x.ExternalId).HasMaxLength(1024);
            e.Property(x => x.PushedEventId).HasMaxLength(1024);
            e.Property(x => x.PushedProvider).HasMaxLength(30);
            e.HasIndex(x => new { x.CalendarId, x.StartsAt });
            // The sync matches remote events by this pair on every run.
            e.HasIndex(x => new { x.CalendarId, x.Source, x.ExternalId });
        });

        b.Entity<ExternalConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TenantId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(20).IsRequired();
            e.Property(x => x.AccountEmail).HasMaxLength(300).IsRequired();
            e.Property(x => x.AccountName).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("connected");
            e.Property(x => x.LastSyncError).HasMaxLength(500);
            e.Property(x => x.LastPulled).HasDefaultValue(0);
            e.Property(x => x.LastPushed).HasDefaultValue(0);
            // Declared: a backfilled 0 here would silently turn every existing connection manual-only.
            e.Property(x => x.SyncEveryMinutes).HasDefaultValue(5);
            // One real calendar per staff calendar. Two would mean two sources of truth for the
            // same day, and the sync would fight itself.
            e.HasIndex(x => x.CalendarId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
        });

        b.Entity<Appointment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.TenantId).HasMaxLength(100).IsRequired();
            e.Property(a => a.VisitorName).HasMaxLength(200).IsRequired();
            e.Property(a => a.VisitorPhone).HasMaxLength(50).IsRequired();
            e.Property(a => a.CallerPhone).HasMaxLength(50);
            e.Property(a => a.Topic).HasMaxLength(500);
            e.Property(a => a.ServiceName).HasMaxLength(120);
            e.Property(a => a.AnswersJson).HasMaxLength(4000);
            e.Property(a => a.Channel).HasMaxLength(20).IsRequired().HasDefaultValue("manual");
            e.Property(a => a.SourceRef).HasMaxLength(200);
            e.Property(a => a.Status).HasMaxLength(20).IsRequired().HasDefaultValue("confirmed");
            e.Property(a => a.CancelledByName).HasMaxLength(200);
            e.Property(a => a.ExternalProvider).HasMaxLength(20);
            e.Property(a => a.ExternalEventId).HasMaxLength(1024);
            e.HasIndex(a => new { a.CalendarId, a.StartsAt });
            e.HasIndex(a => new { a.TenantId, a.StartsAt });
            e.HasIndex(a => a.VisitorPhone);
            e.HasIndex(a => new { a.CalendarId, a.ExternalEventId });
        });
    }
}
