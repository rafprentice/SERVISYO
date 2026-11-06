using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Xunit;

namespace Servisyo.Tests;

/// The daily clean-up keeps NOTIFICATIONS from growing forever: READ notifications older than 6 months are deleted,
/// unread ones are always kept.
public class NotificationCleanupTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 4, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    private static ServisyoDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// The worker and the test must see the SAME in-memory database: one name and one explicit root.
    private static ServisyoDbContext SharedDb(string name, InMemoryDatabaseRoot root) =>
        new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(name, root).Options);

    private static NotificationCleanupService Create(ServisyoDbContext db, int keepMonths = 6) =>
        new(db, Options.Create(new NotificationCleanupOptions { KeepReadMonths = keepMonths }), new FixedClock(Now));

    private static async Task<User> AddUserAsync(ServisyoDbContext db)
    {
        var u = new User
        {
            FirstName = "Ana", LastName = "Cruz", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = Roles.Customer, AccountStatus = AccountStatuses.Active
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static Notification Note(User u, string text, bool read, DateTime createdUtc) =>
        new() { UserId = u.UserId, Message = text, IsRead = read, CreatedAt = createdUtc };

    [Fact]
    public async Task DeletesOnlyReadNotifications_OlderThanSixMonths()
    {
        var db = NewDb();
        var u = await AddUserAsync(db);
        db.Notifications.AddRange(
            Note(u, "old read", read: true, Now.AddMonths(-7)),                 // deleted
            Note(u, "old read 2", read: true, Now.AddYears(-2)),                // deleted
            Note(u, "old UNREAD", read: false, Now.AddYears(-2)),               // kept: never seen
            Note(u, "recent read", read: true, Now.AddMonths(-5)),              // kept: not 6 months old yet
            Note(u, "exactly 6 months", read: true, Now.AddMonths(-6)),         // kept: "older than" means before the cutoff
            Note(u, "new", read: false, Now.AddDays(-1)));                      // kept
        await db.SaveChangesAsync();

        var result = await Create(db).DeleteOldReadAsync();

        Assert.Equal(2, result.Deleted);
        Assert.Equal(Now.AddMonths(-6), result.CutoffUtc);
        Assert.Equal(new[] { "exactly 6 months", "new", "old UNREAD", "recent read" },
            (await db.Notifications.AsNoTracking().Select(n => n.Message).ToListAsync()).OrderBy(m => m));
    }

    [Fact]
    public async Task WritesOneSystemLogLine_WhenSomethingWasDeleted()
    {
        var db = NewDb();
        var u = await AddUserAsync(db);
        db.Notifications.AddRange(Note(u, "a", true, Now.AddMonths(-8)), Note(u, "b", true, Now.AddMonths(-9)));
        await db.SaveChangesAsync();

        await Create(db).DeleteOldReadAsync();

        var log = await db.SystemLogs.AsNoTracking().SingleAsync();
        Assert.Equal("System", log.UserRole);
        Assert.Null(log.UserId);
        Assert.Equal(SystemLogActions.Delete, log.ActionType);
        Assert.Equal("Notification", log.EntityName);
        Assert.StartsWith("Daily clean-up: 2 read notification(s) older than 6 months deleted", log.Description);
        Assert.Contains("May 5, 2026", log.Description);                        // the cutoff in Cebu time
        Assert.True(log.Description.Length <= 500);
    }

    [Fact]
    public async Task NothingToDelete_ChangesNothing_AndWritesNoLog()
    {
        var db = NewDb();
        var u = await AddUserAsync(db);
        db.Notifications.Add(Note(u, "recent", true, Now.AddDays(-10)));
        await db.SaveChangesAsync();

        var result = await Create(db).DeleteOldReadAsync();

        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, await db.Notifications.CountAsync());
        Assert.Empty(await db.SystemLogs.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ASettingBelowOneMonth_IsTreatedAsOneMonth(int keepMonths)
    {
        var db = NewDb();
        var u = await AddUserAsync(db);
        db.Notifications.AddRange(Note(u, "2 weeks", true, Now.AddDays(-14)), Note(u, "2 months", true, Now.AddMonths(-2)));
        await db.SaveChangesAsync();

        var result = await Create(db, keepMonths).DeleteOldReadAsync();

        Assert.Equal(1, result.Deleted);                                         // a typo in appsettings can never empty the table
        Assert.Equal("2 weeks", (await db.Notifications.SingleAsync()).Message);
    }

    [Fact]
    public void TheDefaults_AreSixMonthsOnceADay()
    {
        var o = new NotificationCleanupOptions();
        Assert.True(o.Enabled);
        Assert.Equal(6, o.KeepReadMonths);
        Assert.Equal(24, o.IntervalHours);
        Assert.Equal("NotificationCleanup", NotificationCleanupOptions.SectionName);
    }

    // ------------------------------------------------------------------ the background worker

    private static (NotificationCleanupWorker worker, Func<ServisyoDbContext> openDb) Worker(bool enabled, bool brokenService = false)
    {
        var dbName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<ServisyoDbContext>(o => o.UseInMemoryDatabase(dbName, root));
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.Configure<NotificationCleanupOptions>(o => o.Enabled = enabled);
        if (brokenService) services.AddScoped<INotificationCleanupService, BrokenCleanup>();
        else services.AddScoped<INotificationCleanupService, NotificationCleanupService>();
        var provider = services.BuildServiceProvider();
        var worker = new NotificationCleanupWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NotificationCleanupOptions { Enabled = enabled }), NullLogger<NotificationCleanupWorker>.Instance);
        return (worker, () => SharedDb(dbName, root));
    }

    private sealed class BrokenCleanup : INotificationCleanupService
    {
        public Task<NotificationCleanupResult> DeleteOldReadAsync(CancellationToken ct = default) => throw new InvalidOperationException("database is down");
    }

    private static async Task SeedOldReadAsync(Func<ServisyoDbContext> openDb)
    {
        await using var db = openDb();
        var u = await AddUserAsync(db);
        db.Notifications.Add(Note(u, "old read", true, Now.AddYears(-1)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TheWorker_RunsTheCleanupInItsOwnScope()
    {
        var (worker, openDb) = Worker(enabled: true);
        await SeedOldReadAsync(openDb);

        await worker.RunOnceAsync();

        await using var db = openDb();
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task TheWorker_DoesNothing_WhenTurnedOff()
    {
        var (worker, openDb) = Worker(enabled: false);
        await SeedOldReadAsync(openDb);

        await worker.RunOnceAsync();

        await using var db = openDb();
        Assert.Equal(1, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task AFailingCleanup_NeverStopsTheWebsite()
    {
        var (worker, _) = Worker(enabled: true, brokenService: true);

        var error = await Record.ExceptionAsync(() => worker.RunOnceAsync());

        Assert.Null(error);                                                       // logged, not thrown
    }
}
