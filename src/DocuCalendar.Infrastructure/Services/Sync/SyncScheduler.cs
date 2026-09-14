using System.Collections.Concurrent;
using System.Threading.Channels;
using DocuCalendar.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuCalendar.Infrastructure.Services.Sync;

/// <summary>
/// The line between "something changed on a calendar" and the worker that mirrors it. A booking
/// nudges its calendar so the remote copy appears within seconds; the five-minute tick catches
/// everything else. Also hands out the per-calendar lock that stops a nudge and a tick pushing
/// the same appointment twice.
/// </summary>
public sealed class SyncScheduler
{
    private readonly Channel<Guid> _nudges = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public void Nudge(Guid calendarId) => _nudges.Writer.TryWrite(calendarId);

    public ChannelReader<Guid> Nudges => _nudges.Reader;

    public SemaphoreSlim LockFor(Guid calendarId) => _locks.GetOrAdd(calendarId, _ => new SemaphoreSlim(1, 1));
}

/// <summary>
/// Runs the sync: every connection on a timer, and any calendar that was nudged as soon as it is.
/// Each run gets its own scope, so one dead token or one bad page from a provider stays that
/// person's problem and never stalls a colleague's sync.
/// </summary>
public sealed class SyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SyncScheduler _scheduler;
    private readonly SyncOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<SyncWorker> _logger;
    private bool? _lastStandby;

    public SyncWorker(IServiceScopeFactory scopes, SyncScheduler scheduler, IOptions<SyncOptions> options, IHostEnvironment env, ILogger<SyncWorker> logger)
    {
        _scopes = scopes;
        _scheduler = scheduler;
        _options = options.Value;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Blue/green keeps the previous slot running after a flip. The database lock makes that
    /// safe; this makes it quiet — the standby slot, which may be running last week's code by
    /// then, leaves the scheduled runs to the slot that is actually serving. Nudges are still
    /// honoured: they only arrive through requests, and requests only reach the serving slot.
    /// </summary>
    private bool IsStandbySlot()
    {
        var marker = _options.ActiveSlotFile;
        if (string.IsNullOrWhiteSpace(marker) || !File.Exists(marker)) return false;
        try
        {
            var active = File.ReadAllText(marker).Trim();
            var slot = Path.GetFileName(_env.ContentRootPath.TrimEnd('/', '\\'));
            if (active.Length == 0 || slot.Length == 0) return false;
            var standby = !slot.EndsWith(active, StringComparison.OrdinalIgnoreCase);
            if (_lastStandby != standby)
            {
                _lastStandby = standby;
                _logger.LogInformation("[Sync] This slot ({Slot}) is {Role}; active slot is \"{Active}\".",
                    slot, standby ? "STANDBY — scheduled runs paused" : "ACTIVE", active);
            }
            return standby;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The worker looks once a minute; each connection's own interval (5, 10, 15… minutes, or
    /// manual-only) decides whether it runs. Options.IntervalMinutes is no longer the cadence —
    /// it survives in config only so an old file does not fail to bind.
    /// </summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var interval = Tick;
        // Migrations have run by now (they happen before the host starts); a short pause just lets
        // the first requests through before the worker takes the database for itself.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stop); } catch (OperationCanceledException) { return; }

        var nextTick = DateTimeOffset.UtcNow;
        while (!stop.IsCancellationRequested)
        {
            var wait = nextTick - DateTimeOffset.UtcNow;
            if (wait <= TimeSpan.Zero)
            {
                if (!IsStandbySlot()) await RunAllAsync(stop);
                nextTick = DateTimeOffset.UtcNow + interval;
                continue;
            }

            using var timer = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timer.CancelAfter(wait);
            try
            {
                var first = await _scheduler.Nudges.ReadAsync(timer.Token);
                var calendars = new HashSet<Guid> { first };
                while (_scheduler.Nudges.TryRead(out var more)) calendars.Add(more);
                foreach (var calendarId in calendars)
                    await RunOneAsync(calendarId, stop);
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                // The timer elapsed with nothing nudged — the loop falls through to the full run.
            }
        }
    }

    private async Task RunAllAsync(CancellationToken ct)
    {
        List<Guid> calendarIds;
        try
        {
            using var scope = _scopes.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<CalendarSyncService>();
            calendarIds = await sync.ListDueCalendarsAsync(DateTimeOffset.UtcNow, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[Sync] Could not list connections for the scheduled run.");
            return;
        }

        foreach (var calendarId in calendarIds)
        {
            if (ct.IsCancellationRequested) return;
            await RunOneAsync(calendarId, ct);
        }
    }

    private async Task RunOneAsync(Guid calendarId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<CalendarSyncService>();
            await sync.SyncCalendarAsync(calendarId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SyncCalendarAsync records its own failures; this catches only what escaped it.
            _logger.LogError(ex, "[Sync] Unhandled failure syncing calendar {Calendar}.", calendarId);
        }
    }
}
