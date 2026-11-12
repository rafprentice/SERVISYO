using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class NotificationKindTests
{
    [Theory]
    [InlineData(NotificationKinds.BookingNew)]
    [InlineData(NotificationKinds.BookingCancelled)]
    [InlineData(NotificationKinds.BookingUpdate)]
    [InlineData(NotificationKinds.ApplicationNew)]
    [InlineData(NotificationKinds.Application)]
    [InlineData(NotificationKinds.General)]
    public void Normalize_KeepsTheKindsTheProcedureReturns(string kind) => Assert.Equal(kind, NotificationKinds.Normalize(kind));

    [Theory]
    [InlineData("something-else")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("https://evil.example")]
    public void Normalize_TurnsAnythingElseIntoGeneral(string? kind) => Assert.Equal(NotificationKinds.General, NotificationKinds.Normalize(kind));

    [Fact]
    public void EveryKind_HasATitleAndAnIcon()
    {
        foreach (var kind in NotificationKinds.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(NotificationKinds.TitleOf(kind)));
            Assert.Contains(NotificationKinds.IconOf(kind), new[] { "calendar", "user", "bell" });
        }
    }

    // "now" is Wed 14 Oct 2026, 10:00 in Manila (02:00 UTC)
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "Just now")]
    [InlineData(30, "Just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(10 * 60, "10 mins ago")]
    [InlineData(59 * 60, "59 mins ago")]
    [InlineData(60 * 60, "1 hr ago")]
    [InlineData(3 * 3600 + 5, "3 hrs ago")]
    [InlineData(-120, "Just now")]                       // a clock that is a little ahead never shows a negative time
    public void TimeAgo_ForRecentTimes(int secondsAgo, string expected)
        => Assert.Equal(expected, NotificationFormat.TimeAgo(Now.UtcDateTime.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void TimeAgo_UsesCebuDaysForYesterdayAndOlder()
    {
        // 23:00 Manila on the 13th (15:00 UTC on the 13th) is "Yesterday" for 10:00 Manila on the 14th
        Assert.Equal("Yesterday", NotificationFormat.TimeAgo(new DateTime(2026, 10, 13, 15, 0, 0), Now));
        Assert.Equal("Yesterday", NotificationFormat.TimeAgo(new DateTime(2026, 10, 12, 20, 0, 0), Now));     // 04:00 Manila on the 13th
        Assert.Equal("3 days ago", NotificationFormat.TimeAgo(new DateTime(2026, 10, 11, 2, 0, 0), Now));
        Assert.Equal("Oct 2, 2026", NotificationFormat.TimeAgo(new DateTime(2026, 10, 2, 2, 0, 0), Now));
    }
}

/// Database v2.3: the rows come from dbo.usp_GetNotifications (tested by the SQL self-test 30/31); here a stand-in
/// source gives the rows, so the panel, the page, the unread number and "mark all as read" can be tested.
public class NotificationServiceTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeSource : INotificationSource
    {
        public Dictionary<int, List<NotificationRow>> Rows { get; } = new();
        public Task<IReadOnlyList<NotificationRow>> GetAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationRow>>(Rows.TryGetValue(userId, out var r) ? r : new List<NotificationRow>());
    }

    private static readonly DateTime NowUtc = new(2026, 10, 14, 2, 0, 0);

    private static (NotificationService svc, ServisyoDbContext db, FakeSource source) Create()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var source = new FakeSource();
        return (new NotificationService(db, new FixedClock(), source), db, source);
    }

    private static async Task<User> AddUserAsync(ServisyoDbContext db, string role, string first = "Ana", string status = AccountStatuses.Active)
    {
        var u = new User { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status };
        if (role == Roles.ServiceProvider) u.Provider = new Provider { BusinessName = "Marco Works", ApprovalStatus = ApprovalStatuses.Approved };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static NotificationRow Row(string message, int minutesAgo, bool unread = true, string kind = NotificationKinds.BookingUpdate) =>
        new() { Kind = kind, Message = message, CreatedAt = NowUtc.AddMinutes(-minutesAgo), IsUnread = unread };

    [Fact]
    public async Task Panel_ShowsMyRows_NewestFirst_WithTheUnreadNumber()
    {
        var (svc, db, source) = Create();
        var me = await AddUserAsync(db, Roles.Customer);
        source.Rows[me.UserId] = new()
        {
            Row("Your booking #1 is now Accepted.", 120, unread: false),
            Row("Your booking #3 is now Completed.", 1),
            Row("Your booking #2 is now Rejected.", 10)
        };

        var panel = (await svc.GetPanelAsync(me.UserId))!;

        Assert.Equal(2, panel.Unread);
        Assert.Equal(new[] { "Your booking #3 is now Completed.", "Your booking #2 is now Rejected.", "Your booking #1 is now Accepted." },
            panel.Items.Select(i => i.Message));
        Assert.Equal("Booking Update", panel.Items[0].Title);
        Assert.Equal("1 min ago", panel.Items[0].TimeText);
        Assert.Equal("2 hrs ago", panel.Items[2].TimeText);
        Assert.False(panel.Items[0].IsRead);
        Assert.True(panel.Items[2].IsRead);
    }

    [Fact]
    public async Task Panel_ShowsEight_ButTheNumberCountsEveryUnreadOne()
    {
        var (svc, db, source) = Create();
        var me = await AddUserAsync(db, Roles.ServiceProvider, "Marco");
        source.Rows[me.UserId] = Enumerable.Range(1, 12)
            .Select(i => Row($"You have a new booking request (Booking #{i}).", i, kind: NotificationKinds.BookingNew)).ToList();

        var panel = (await svc.GetPanelAsync(me.UserId))!;

        Assert.Equal(12, panel.Unread);
        Assert.Equal(NotificationService.PanelSize, panel.Items.Count);
        Assert.Equal("You have a new booking request (Booking #1).", panel.Items[0].Message);
        Assert.Equal("New Booking Request", panel.Items[0].Title);
    }

    [Fact]
    public async Task NeverMoreThanTwentyFive_EvenIfTheSourceGivesMore()
    {
        var (svc, db, source) = Create();
        var me = await AddUserAsync(db, Roles.ServiceProvider, "Marco");
        source.Rows[me.UserId] = Enumerable.Range(1, 40).Select(i => Row($"Booking #{i}", i)).ToList();

        var page = (await svc.GetPageAsync(me.UserId, 1))!;

        Assert.Equal(DerivedQueries.Top, page.Total);
        Assert.Equal(DerivedQueries.Top, page.Unread);
        Assert.Equal(2, page.PageCount);                   // 25 rows, 15 a page
    }

    [Fact]
    public async Task Page_IsPaged_AndClampsTheNumber()
    {
        var (svc, db, source) = Create();
        var me = await AddUserAsync(db, Roles.Customer);
        source.Rows[me.UserId] = Enumerable.Range(1, 20).Select(i => Row($"Your booking #{i} is now Accepted.", i)).ToList();

        var page2 = (await svc.GetPageAsync(me.UserId, 2))!;
        Assert.Equal(2, page2.Page);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal("Your booking #16 is now Accepted.", page2.Items[0].Message);

        Assert.Equal(2, (await svc.GetPageAsync(me.UserId, 99))!.Page);
        Assert.Equal(1, (await svc.GetPageAsync(me.UserId, -3))!.Page);
    }

    [Fact]
    public async Task Panel_IsEmptyForSomeoneWithNoNotifications()
    {
        var (svc, db, _) = Create();
        var me = await AddUserAsync(db, Roles.Administrator, "Ada");

        var panel = (await svc.GetPanelAsync(me.UserId))!;

        Assert.Equal(0, panel.Unread);
        Assert.Empty(panel.Items);
    }

    [Fact]
    public async Task UnknownKinds_AreShownAsGeneral()
    {
        var (svc, db, source) = Create();
        var me = await AddUserAsync(db, Roles.Customer);
        source.Rows[me.UserId] = new() { Row("Hello", 1, kind: "not-a-kind") };

        var item = Assert.Single((await svc.GetPanelAsync(me.UserId))!.Items);

        Assert.Equal(NotificationKinds.General, item.Kind);
        Assert.Equal("bell", item.Icon);
    }

    [Fact]
    public async Task SuspendedDeactivatedOrUnknownAccounts_GetNothing()
    {
        var (svc, db, source) = Create();
        var suspended = await AddUserAsync(db, Roles.Customer, "Sus", AccountStatuses.Suspended);
        var deactivated = await AddUserAsync(db, Roles.ServiceProvider, "Dea", AccountStatuses.Deactivated);
        source.Rows[suspended.UserId] = new() { Row("Your booking #1 is now Accepted.", 1) };

        foreach (var id in new[] { suspended.UserId, deactivated.UserId, 9999 })
        {
            Assert.Null(await svc.GetPanelAsync(id));
            Assert.Null(await svc.GetPageAsync(id, 1));
            Assert.Null(await svc.MarkAllReadAsync(id));
        }
        Assert.Null((await db.Users.SingleAsync(u => u.UserId == suspended.UserId)).NotificationsSeenAt);
    }

    [Fact]
    public async Task MarkAllRead_OnlyMarksMyBellAsSeen()
    {
        var (svc, db, _) = Create();
        var me = await AddUserAsync(db, Roles.Customer);
        var other = await AddUserAsync(db, Roles.Customer, "Ben");

        var unread = await svc.MarkAllReadAsync(me.UserId);

        Assert.Equal(0, unread);
        Assert.Equal(NowUtc, (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == me.UserId)).NotificationsSeenAt);
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.UserId == other.UserId)).NotificationsSeenAt);
    }

    [Theory]
    [InlineData(Roles.Customer, "Customer", "Customer Account")]
    [InlineData(Roles.ServiceProvider, "Provider", "Marco Works")]
    [InlineData(Roles.Administrator, "Admin", "System Administrator")]
    public async Task Page_HasTheRightSidebarForEachRole(string role, string shellRole, string subtitle)
    {
        var (svc, db, source) = Create();
        var me = await AddUserAsync(db, role);
        source.Rows[me.UserId] = new() { Row("x", 1), Row("y", 2, unread: false) };

        var page = (await svc.GetPageAsync(me.UserId, 1))!;

        Assert.Equal(shellRole, page.Shell.Role);
        Assert.Equal(subtitle, page.Shell.Subtitle);
        Assert.Equal(1, page.Shell.UnreadNotifications);
    }

    [Fact]
    public async Task TheRealSource_ReturnsNothing_OnTheInMemoryTestDatabase()
    {
        var (_, db, _) = Create();
        var me = await AddUserAsync(db, Roles.Customer);

        Assert.Empty(await new StoredProcedureNotificationSource(db).GetAsync(me.UserId));
        Assert.Equal(0, await db.CountUnreadNotificationsAsync(me.UserId));
        Assert.Empty(await db.GetBookingsNeedingAttentionAsync());
    }
}

/// The stored procedures and the C# classes that read them must use the same column names (EF matches them by name).
public class DerivedQueryColumnTests
{
    /// The procedure as the database has it now: the v2.7 version when database v2.7 changed it, otherwise v2.3.
    private static string ProcedureText(string name)
    {
        bool Is(string s) => s.Contains("CREATE OR ALTER PROCEDURE dbo." + name + "\n") || s.Contains("CREATE OR ALTER PROCEDURE dbo." + name + "\r\n");
        var sql = DayOffRequestsSql.CreateAll.SingleOrDefault(Is) ?? Assert.Single(DerivedDataSql.CreateAll, Is);
        return sql.Replace("''", "'");
    }

    private static List<string> FinalAliases(string sql)
    {
        // the last SELECT TOP (@Top) ... FROM of the procedure: its "AS Name" aliases
        var last = sql.LastIndexOf("SELECT TOP (@Top)", StringComparison.Ordinal);
        Assert.True(last >= 0);
        var from = sql.IndexOf("\n    FROM ", last, StringComparison.Ordinal);
        return Regex.Matches(sql[last..from], @"\bAS ([A-Za-z]+)\s*(?:,|$)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
    }

    private static IEnumerable<string> Properties<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

    [Fact]
    public void Notifications_ColumnsMatchNotificationRow()
    {
        var sql = ProcedureText("usp_GetNotifications");
        Assert.Equal(Properties<NotificationRow>().OrderBy(x => x), FinalAliases(sql).Distinct().OrderBy(x => x));
        Assert.Contains("SELECT TOP (@Top)", sql);
        Assert.Contains("IF @Top IS NULL OR @Top < 1 OR @Top > 25 SET @Top = 25;", sql);     // never more than 25
    }

    [Fact]
    public void Attention_ColumnsMatchAttentionRow()
    {
        var sql = ProcedureText("usp_GetBookingsNeedingAttention");
        Assert.Equal(Properties<AttentionRow>().OrderBy(x => x), FinalAliases(sql).Distinct().OrderBy(x => x));
        Assert.Contains("IF @Top IS NULL OR @Top < 1 OR @Top > 25 SET @Top = 25;", sql);
    }

    [Fact]
    public void TheNotificationKinds_OfTheProcedure_AreTheOnesTheAppKnows()
    {
        var sql = ProcedureText("usp_GetNotifications");
        var kinds = Regex.Matches(sql, @"N'(booking-[a-z]+|application(?:-new)?|general|day-off(?:-new)?)'").Select(m => m.Groups[1].Value).Distinct();
        Assert.All(kinds, k => Assert.Contains(k, NotificationKinds.All));
        Assert.Equal(NotificationKinds.All.OrderBy(x => x), kinds.OrderBy(x => x));
    }
}

/// Registration no longer writes anything for the administrators' bell: a Pending application IS the notification.
public class AdminApplicationNotificationTests
{
    [Fact]
    public async Task ANewProviderApplication_IsPending_AndNothingElseIsStored()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var svc = new AuthService(db, new PasswordHasher<User>());

        var result = await svc.RegisterProviderAsync(new RegisterProviderViewModel
        {
            FirstName = "Juan", MiddleName = "Reyes", LastName = "Dela Cruz", Email = "juan@email.com", PhoneNumber = "09171234567",
            Password = "Passw0rd1", ConfirmPassword = "Passw0rd1", BusinessName = "Juan Plumbing Services",
            Description = "Pipe repair and installation.", Address = "Lahug, Cebu City", BaseFee = 500m
        });

        Assert.True(result.Succeeded);
        Assert.Equal(ApprovalStatuses.Pending, (await db.Providers.SingleAsync()).ApprovalStatus);
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.GetTableName() is "NOTIFICATIONS" or "BOOKING_REPORTS");
    }
}
