using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Servisyo.Web.Data;
using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

/// appsettings.json, section "NotificationCleanup". Keeps the NOTIFICATIONS table from growing forever:
/// once a day, READ notifications older than KeepReadMonths are deleted. Unread notifications are never deleted.
public class NotificationCleanupOptions
{
    public const string SectionName = "NotificationCleanup";

    public bool Enabled { get; set; } = true;
    public int KeepReadMonths { get; set; } = 6;            // never less than 1 (see NotificationCleanupService.MinKeepMonths)
    public int FirstRunDelayMinutes { get; set; } = 2;      // after the website starts
    public int IntervalHours { get; set; } = 24;
}

/// Deleted = how many notifications were removed; CutoffUtc = read notifications created before this were removed.
public sealed record NotificationCleanupResult(int Deleted, DateTime CutoffUtc);

public interface INotificationCleanupService
{
    /// Deletes READ notifications older than the configured number of months and writes one SYSTEM_LOGS line
    /// saying how many. Unread notifications are kept, however old they are.
    Task<NotificationCleanupResult> DeleteOldReadAsync(CancellationToken ct = default);
}

public class NotificationCleanupService : INotificationCleanupService
{
    public const int MinKeepMonths = 1;
    private static readonly TimeSpan Cebu = TimeSpan.FromHours(8);     // the Philippines has no daylight saving

    private readonly ServisyoDbContext _db;
    private readonly NotificationCleanupOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationCleanupService> _logger;

    public NotificationCleanupService(ServisyoDbContext db, IOptions<NotificationCleanupOptions>? options = null,
        TimeProvider? clock = null, ILogger<NotificationCleanupService>? logger = null)
    {
        _db = db;
        _options = options?.Value ?? new NotificationCleanupOptions();
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationCleanupService>.Instance;
    }

    public async Task<NotificationCleanupResult> DeleteOldReadAsync(CancellationToken ct = default)
    {
        var months = Math.Max(MinKeepMonths, _options.KeepReadMonths);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var cutoffUtc = nowUtc.AddMonths(-months);

        // NOTIFICATIONS.CREATED_AT is stored in UTC
        var old = _db.Notifications.Where(n => n.IsRead && n.CreatedAt < cutoffUtc);

        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(ct) : null;

        int deleted;
        if (relational)
        {
            deleted = await old.ExecuteDeleteAsync(ct);                // one DELETE ... WHERE on SQL Server
        }
        else
        {
            var rows = await old.ToListAsync(ct);                      // the in-memory test database has no ExecuteDelete
            _db.Notifications.RemoveRange(rows);
            await _db.SaveChangesAsync(ct);
            deleted = rows.Count;
        }

        if (deleted > 0)
        {
            var cutoffCebu = cutoffUtc + Cebu;
            _db.SystemLogs.Add(new SystemLog
            {
                UserId = null,
                UserRole = "System",
                ActionType = SystemLogActions.Delete,
                EntityName = "Notification",
                EntityId = null,
                Description = $"Daily clean-up: {deleted} read notification(s) older than {months} months deleted "
                              + $"(created before {cutoffCebu.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}). Unread notifications are kept.",
                CreatedAt = nowUtc
            });
            await _db.SaveChangesAsync(ct);
        }

        if (tx is not null) await tx.CommitAsync(ct);

        _logger.LogInformation("Notification clean-up: {Deleted} read notification(s) created before {Cutoff:u} deleted.", deleted, cutoffUtc);
        return new(deleted, cutoffUtc);
    }
}

/// Runs the notification clean-up a couple of minutes after the website starts, then once every IntervalHours.
/// (The website may be restarted by the host at any time, e.g. MonsterASP recycling the app; it simply runs again
/// after the next start, so old read notifications never pile up.) A failure is logged and never stops the website.
public sealed class NotificationCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly NotificationCleanupOptions _options;
    private readonly ILogger<NotificationCleanupWorker> _logger;

    public NotificationCleanupWorker(IServiceScopeFactory scopes, IOptions<NotificationCleanupOptions> options, ILogger<NotificationCleanupWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Notification clean-up is turned off (NotificationCleanup:Enabled = false).");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(0, _options.FirstRunDelayMinutes)), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;                                               // the website is stopping
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, _options.IntervalHours)));
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await NextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> NextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    /// One clean-up in its own scope (its own database context). Never throws.
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<INotificationCleanupService>();
            await cleanup.DeleteOldReadAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // the website is stopping
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notification clean-up failed. It will try again at the next run.");
        }
    }
}
