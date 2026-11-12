using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

/// The yearly reset of the providers' unavailable dates: from January 1 (Philippine time) the dates of the year
/// before are deleted, and the day-off requests left without days, so every provider starts the new year with an
/// empty list (UnavailableDateRules only allows dates of the current year, so nothing else is ever deleted).
///
/// It runs when the website starts and then every hour. A website that was asleep on January 1 resets on its first
/// start after it; running it again changes nothing. Old unavailable dates are not used by anything else
/// (bookings only look at the booked day), so deleting them loses no history.
public class UnavailableDatesResetService : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);
    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<UnavailableDatesResetService> _logger;

    public UnavailableDatesResetService(IServiceScopeFactory scopes, ILogger<UnavailableDatesResetService> logger, TimeProvider? clock = null)
    {
        _scopes = scopes;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// Deletes every unavailable date BEFORE January 1 of today's year, then the day-off requests (database v2.7) that
    /// were made before January 1 and have no days left (last year's requests, and rejected ones, whose days were
    /// already given back). Returns how many dates were deleted.
    public static async Task<int> DeleteLastYearsDatesAsync(ServisyoDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var firstOfYear = UnavailableDateRules.FirstOfYear(today);
        var firstOfYearUtc = new DateTime(today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc) - ManilaOffset;   // Jan 1, 00:00 in Cebu
        var old = db.ProviderUnavailableDates.Where(d => d.UnavailableDate < firstOfYear);
        var oldRequests = db.DayOffRequests.Where(r => r.RequestedAt < firstOfYearUtc && !r.Days.Any());

        if (db.Database.IsRelational())
        {
            var deleted = await old.ExecuteDeleteAsync(ct);              // one DELETE ... WHERE UNAVAILABLE_DATE < @first
            await oldRequests.ExecuteDeleteAsync(ct);                    // then the requests that have no days left
            return deleted;
        }

        var rows = await old.ToListAsync(ct);                            // the in-memory test database
        db.ProviderUnavailableDates.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        db.DayOffRequests.RemoveRange(await oldRequests.ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every, _clock);
        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // the website is shutting down
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            var today = DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(ManilaOffset).DateTime);
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServisyoDbContext>();

            var deleted = await DeleteLastYearsDatesAsync(db, today, ct);
            if (deleted > 0)
                _logger.LogInformation("New year reset: deleted {Count} unavailable date(s) from before {FirstOfYear}.",
                    deleted, UnavailableDateRules.FirstOfYear(today));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // a database problem must not stop the website; the next run tries again
            _logger.LogError(ex, "The yearly reset of unavailable dates failed; it will try again in {Minutes} minutes.", Every.TotalMinutes);
        }
    }
}
