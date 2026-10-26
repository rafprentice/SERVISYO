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
    [InlineData("You have a new booking request (Booking #5).", NotificationKinds.BookingNew)]
    [InlineData("Booking #5 was cancelled by the customer.", NotificationKinds.BookingCancelled)]
    [InlineData("Your booking #5 is now Accepted.", NotificationKinds.BookingUpdate)]
    [InlineData("New provider application from Juan Plumbing (Juan Dela Cruz).", NotificationKinds.ApplicationNew)]
    [InlineData("Your provider application was Approved.", NotificationKinds.Application)]
    [InlineData("Something else entirely", NotificationKinds.General)]
    [InlineData("", NotificationKinds.General)]
    [InlineData(null, NotificationKinds.General)]
    public void Classify_UsesTheWordingTheSystemWrites(string? message, string expected)
        => Assert.Equal(expected, NotificationKinds.Classify(message));

    [Fact]
    public void EveryKind_HasATitleAndAnIcon()
    {
        foreach (var kind in new[] { NotificationKinds.BookingNew, NotificationKinds.BookingCancelled, NotificationKinds.BookingUpdate,
                                     NotificationKinds.ApplicationNew, NotificationKinds.Application, NotificationKinds.General })
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

public class NotificationServiceTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime NowUtc = new(2026, 10, 14, 2, 0, 0);

    private static (NotificationService svc, ServisyoDbContext db) Create()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new NotificationService(db, new FixedClock()), db);
    }

    private static async Task<User> AddUserAsync(ServisyoDbContext db, string role, string first = "Ana", string status = AccountStatuses.Active)
    {
        var u = new User { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status };
        if (role == Roles.ServiceProvider) u.Provider = new Provider { BusinessName = "Marco Works", ApprovalStatus = ApprovalStatuses.Approved };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static async Task<Notification> NotifyAsync(ServisyoDbContext db, User to, string message, bool read = false, int minutesAgo = 5)
    {
        var n = new Notification { UserId = to.UserId, Message = message, IsRead = read, CreatedAt = NowUtc.AddMinutes(-minutesAgo) };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n;
    }

    // ------------------------------------------------------------------ the dropdown

    [Fact]
    public async Task Panel_ShowsOnlyMyNotifications_NewestFirst_WithTheUnreadNumber()
    {
        var (svc, db) = Create();
        var me = await AddUserAsync(db, Roles.Customer);
        var other = await AddUserAsync(db, Roles.Customer, "Ben");
        await NotifyAsync(db, me, "Your booking #1 is now Accepted.", read: true, minutesAgo: 120);
        await NotifyAsync(db, me, "Your booking #2 is now Rejected.", minutesAgo: 10);
        await NotifyAsync(db, me, "Your booking #3 is now Completed.", minutesAgo: 1);
        await NotifyAsync(db, other, "Your booking #9 is now Accepted.");

        var panel = await svc.GetPanelAsync(me.UserId);

        Assert.NotNull(panel);
        Assert.Equal(2, panel!.Unread);
        Assert.Equal(new[] { "Your booking #3 is now Completed.", "Your booking #2 is now Rejected.", "Your booking #1 is now Accepted." },
            panel.Items.Select(i => i.Message).ToArray());
        Assert.Equal("Booking Update", panel.Items[0].Title);
        Assert.Equal("1 min ago", panel.Items[0].TimeText);
        Assert.Equal("2 hrs ago", panel.Items[2].TimeText);
        Assert.False(panel.Items[0].IsRead);
        Assert.True(panel.Items[2].IsRead);
    }

    [Fact]
    public async Task Panel_ShowsEight_ButTheNumberCountsEveryUnreadOne()
    {
        var (svc, db) = Create();
        var me = await AddUserAsync(db, Roles.ServiceProvider, "Marco");
        for (var i = 1; i <= 12; i++) await NotifyAsync(db, me, $"You have a new booking request (Booking #{i}).", minutesAgo: i);

        var panel = (await svc.GetPanelAsync(me.UserId))!;

        Assert.Equal(12, panel.Unread);
        Assert.Equal(NotificationService.PanelSize, panel.Items.Count);
        Assert.Equal("You have a new booking request (Booking #1).", panel.Items[0].Message);
    }

    [Fact]
    public async Task Panel_IsEmptyForSomeoneWithNoNotifications()
    {
        var (svc, db) = Create();
        var me = await AddUserAsync(db, Roles.Administrator, "Ada");

        var panel = (await svc.GetPanelAsync(me.UserId))!;

        Assert.Equal(0, panel.Unread);
        Assert.Empty(panel.Items);
    }

    [Fact]
    public async Task SuspendedDeactivatedOrUnknownAccounts_GetNothing()
    {
        var (svc, db) = Create();
        var suspended = await AddUserAsync(db, Roles.Customer, "Sus", AccountStatuses.Suspended);
        var deactivated = await AddUserAsync(db, Roles.ServiceProvider, "Dea", AccountStatuses.Deactivated);
        await NotifyAsync(db, suspended, "Your booking #1 is now Accepted.");

        foreach (var id in new[] { suspended.UserId, deactivated.UserId, 9999 })
        {
            Assert.Null(await svc.GetPanelAsync(id));
            Assert.Null(await svc.GetPageAsync(id, 1));
            Assert.Null(await svc.MarkAllReadAsync(id));
            Assert.False((await svc.OpenAsync(id, 1)).Allowed);
        }
    }

    // ------------------------------------------------------------------ marking read

    [Fact]
    public async Task MarkAllRead_OnlyTouchesMyNotifications()
    {
        var (svc, db) = Create();
        var me = await AddUserAsync(db, Roles.Customer);
        var other = await AddUserAsync(db, Roles.Customer, "Ben");
        await NotifyAsync(db, me, "Your booking #1 is now Accepted.");
        await NotifyAsync(db, me, "Your booking #2 is now Accepted.");
        await NotifyAsync(db, other, "Your booking #3 is now Accepted.");

        var unread = await svc.MarkAllReadAsync(me.UserId);

        Assert.Equal(0, unread);
        Assert.All(db.Notifications.Where(n => n.UserId == me.UserId), n => Assert.True(n.IsRead));
        Assert.All(db.Notifications.Where(n => n.UserId == other.UserId), n => Assert.False(n.IsRead));
        Assert.Equal(0, (await svc.GetPanelAsync(me.UserId))!.Unread);
        Assert.Equal(1, (await svc.GetPanelAsync(other.UserId))!.Unread);
    }

    [Fact]
    public async Task Open_MarksOneReadAndReturnsIt()
    {
        var (svc, db) = Create();
        var me = await AddUserAsync(db, Roles.ServiceProvider, "Marco");
        var first = await NotifyAsync(db, me, "You have a new booking request (Booking #1).");
        var second = await NotifyAsync(db, me, "You have a new booking request (Booking #2).");

        var result = await svc.OpenAsync(me.UserId, first.NotificationId);

        Assert.True(result.Allowed);
        Assert.Equal(NotificationKinds.BookingNew, result.Item!.Kind);
        Assert.True((await db.Notifications.FindAsync(first.NotificationId))!.IsRead);
        Assert.False((await db.Notifications.FindAsync(second.NotificationId))!.IsRead);
    }

    [Fact]
    public async Task Open_SomeoneElsesNotification_IsNotFound_AndStaysUnread()
    {
        var (svc, db) = Create();
        var me = await AddUserAsync(db, Roles.Customer);
        var other = await AddUserAsync(db, Roles.Customer, "Ben");
        var theirs = await NotifyAsync(db, other, "Your booking #1 is now Accepted.");

        var result = await svc.OpenAsync(me.UserId, theirs.NotificationId);
        var missing = await svc.OpenAsync(me.UserId, 9999);

        Assert.True(result.Allowed);
        Assert.Null(result.Item);
        Assert.Null(missing.Item);
        Assert.False((await db.Notifications.FindAsync(theirs.NotificationId))!.IsRead);
    }

    // ------------------------------------------------------------------ the full page

    [Fact]
    public async Task Page_IsPaged_AndClampsTheNumber()
    {
        var (svc, db) = Create();
        var me = await AddUserAsync(db, Roles.Customer);
        for (var i = 1; i <= 20; i++) await NotifyAsync(db, me, $"Your booking #{i} is now Accepted.", minutesAgo: i);

        var first = (await svc.GetPageAsync(me.UserId, 1))!;
        var second = (await svc.GetPageAsync(me.UserId, 2))!;
        var beyond = (await svc.GetPageAsync(me.UserId, 99))!;
        var below = (await svc.GetPageAsync(me.UserId, -3))!;

        Assert.Equal(20, first.Total);
        Assert.Equal(2, first.PageCount);
        Assert.Equal(NotificationService.PageSize, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal(2, beyond.Page);
        Assert.Equal(1, below.Page);
        Assert.Equal(20, first.Unread);
        Assert.Equal(20, first.Shell.UnreadNotifications);
    }

    [Theory]
    [InlineData(Roles.Customer, "Customer", "Customer Account")]
    [InlineData(Roles.ServiceProvider, "Provider", "Marco Works")]
    [InlineData(Roles.Administrator, "Admin", "System Administrator")]
    public async Task Page_HasTheRightSidebarForEachRole(string role, string shellRole, string subtitle)
    {
        var (svc, db) = Create();
        var me = await AddUserAsync(db, role);

        var page = (await svc.GetPageAsync(me.UserId, 1))!;

        Assert.Equal(shellRole, page.Shell.Role);
        Assert.Equal(subtitle, page.Shell.Subtitle);
    }
}

public class AdminApplicationNotificationTests
{
    private static (AuthService svc, ServisyoDbContext db) Create()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new AuthService(db, new PasswordHasher<User>()), db);
    }

    private static RegisterProviderViewModel Provider(string business = "Juan Plumbing Services", string email = "juan@email.com") => new()
    {
        FirstName = "Juan", LastName = "Dela Cruz", Email = email, Password = "Passw0rd1", ConfirmPassword = "Passw0rd1", BusinessName = business
    };

    private static User Admin(string status = AccountStatuses.Active) =>
        new() { FirstName = "Ada", LastName = "Admin", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = Roles.Administrator, AccountStatus = status };

    [Fact]
    public async Task ANewProviderApplication_NotifiesEveryActiveAdministrator()
    {
        var (svc, db) = Create();
        var a1 = Admin(); var a2 = Admin(); var gone = Admin(AccountStatuses.Suspended);
        db.Users.AddRange(a1, a2, gone);
        await db.SaveChangesAsync();

        var result = await svc.RegisterProviderAsync(Provider());

        Assert.True(result.Succeeded);
        var notes = await db.Notifications.ToListAsync();
        Assert.Equal(2, notes.Count);
        Assert.Equal(new[] { a1.UserId, a2.UserId }.OrderBy(x => x), notes.Select(n => n.UserId).OrderBy(x => x));
        Assert.All(notes, n =>
        {
            Assert.Equal("New provider application from Juan Plumbing Services (Juan Dela Cruz).", n.Message);
            Assert.False(n.IsRead);
            Assert.Equal(NotificationKinds.ApplicationNew, NotificationKinds.Classify(n.Message));
        });
    }

    [Fact]
    public async Task WithoutAnyAdministrator_RegistrationStillWorks()
    {
        var (svc, db) = Create();

        var result = await svc.RegisterProviderAsync(Provider());

        Assert.True(result.Succeeded);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task ALongBusinessName_IsCutToFitTheColumn()
    {
        var (svc, db) = Create();
        db.Users.Add(Admin());
        await db.SaveChangesAsync();

        var result = await svc.RegisterProviderAsync(Provider(new string('B', 155)));

        Assert.True(result.Succeeded);
        var message = (await db.Notifications.SingleAsync()).Message;
        Assert.True(message.Length <= 255);
        Assert.StartsWith("New provider application from BBB", message);
    }

    [Fact]
    public async Task CustomerSignUps_AndFailedSignUps_NotifyNobody()
    {
        var (svc, db) = Create();
        db.Users.Add(Admin());
        await db.SaveChangesAsync();

        await svc.RegisterCustomerAsync(new RegisterCustomerViewModel
        {
            FirstName = "Maria", LastName = "Santos", Email = "maria@email.com", Password = "Passw0rd1", ConfirmPassword = "Passw0rd1"
        });
        await svc.RegisterProviderAsync(Provider(email: "dup@email.com"));
        db.Notifications.RemoveRange(db.Notifications);
        await db.SaveChangesAsync();
        var duplicate = await svc.RegisterProviderAsync(Provider(email: "dup@email.com"));      // same email: refused

        Assert.False(duplicate.Succeeded);
        Assert.Empty(db.Notifications);
    }
}
