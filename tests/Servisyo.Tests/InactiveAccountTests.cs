using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;      // IDesignTimeModel
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.9: an account with no sign-in for a year is warned by email at 335 days and removed 30 days after the
/// warning (never earlier). Administrators and accounts with an unfinished booking are never touched. An account
/// with finished bookings is erased (the person) but its bookings, payments and reviews stay for the other person.
public class InactiveAccountTests
{
    private static readonly DateTime Now = new(2027, 12, 1, 2, 0, 0, DateTimeKind.Utc);    // Dec 1, 2027, 10:00 in Cebu

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeEmail : IEmailService
    {
        public bool Works { get; set; } = true;
        public List<(string To, string Subject, string Text)> Sent { get; } = new();
        public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, string textBody)
        {
            if (Works) Sent.Add((toEmail, subject, textBody));
            return Task.FromResult(Works);
        }
    }

    private sealed record World(ServisyoDbContext Db, InactiveAccountService Svc, FakeEmail Email, User Admin);

    private static async Task<World> WorldAsync()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", Roles.Administrator, daysAgo: 800);       // administrators are never removed, however old
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var email = new FakeEmail();
        return new(db, new InactiveAccountService(db, email, new FixedClock()), email, admin);
    }

    private static User NewUser(string first, string role, int daysAgo, int? warnedDaysAgo = null, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, LastName = "Reyes", Email = $"{first.ToLowerInvariant()}{Guid.NewGuid():N}@test.com", PasswordHash = "hash",
        PhoneNumber = "09171234567", Role = role, AccountStatus = status, CreatedAt = Now.AddDays(-900),
        LastActivityAt = Now.AddDays(-daysAgo), InactivityWarnedAt = warnedDaysAgo is int w ? Now.AddDays(-w) : null
    };

    private static async Task<Customer> CustomerAsync(World w, int daysAgo, int? warnedDaysAgo = null)
    {
        var c = new Customer { Address = "Salinas Drive, Lahug", ProfilePhoto = "/uploads/avatars/c.png", User = NewUser("Ana", Roles.Customer, daysAgo, warnedDaysAgo) };
        w.Db.Customers.Add(c);
        await w.Db.SaveChangesAsync();
        return c;
    }

    private static async Task<Provider> ProviderAsync(World w, int daysAgo, int? warnedDaysAgo = null)
    {
        var p = new Provider
        {
            BusinessName = "Mary Plumbing", Description = "Pipes", Address = "Mabolo", ApprovalStatus = ApprovalStatuses.Approved,
            User = NewUser("Mary", Roles.ServiceProvider, daysAgo, warnedDaysAgo)
        };
        w.Db.Providers.Add(p);
        await w.Db.SaveChangesAsync();
        return p;
    }

    private static async Task<Booking> BookingAsync(World w, Customer c, Provider p, string status, string? cash = null)
    {
        var category = new ServiceCategory { CategoryName = "Plumbing" };
        var b = new Booking
        {
            CustomerId = c.CustomerId, ProviderId = p.ProviderId, BookingDate = new DateOnly(2026, 11, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 850m, Status = status, PaymentStatus = PaymentStatuses.Paid,
            ServiceLocation = "Salinas Drive, Lahug", ServiceDescription = "Sink leaks", RowVersion = new byte[] { 1 },
            PaymentDetail = cash is null ? null : new PaymentDetail { PaymentId = PaymentIds.Cash, PaymentStatus = cash, TransactionReference = "CASH-1" }
        };
        b.Services.Add(new BookingService
        {
            Service = new Service { ProviderId = p.ProviderId, Category = category, ServiceName = "Pipe Repair", Price = 850m },
            PriceSnapshot = 850m, DurationMinutesSnapshot = 60
        });
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        return b;
    }

    private static Task<bool> UserExists(World w, int userId) => w.Db.Users.AsNoTracking().AnyAsync(u => u.UserId == userId);

    // ------------------------------------------------------------ signing in is activity

    [Fact]
    public async Task SigningIn_RestartsTheYear_AndCancelsTheWarning()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hasher = new PasswordHasher<User>();
        var user = NewUser("Ana", Roles.Customer, daysAgo: 350, warnedDaysAgo: 10);
        user.Email = "ana@test.com";
        user.PasswordHash = hasher.HashPassword(user, "Secret123");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await new AuthService(db, hasher).ValidateLoginAsync("ana@test.com", "Secret123");

        Assert.True(result.Succeeded);
        var saved = await db.Users.AsNoTracking().SingleAsync();
        Assert.True(saved.LastActivityAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.Null(saved.InactivityWarnedAt);
    }

    [Fact]
    public async Task AWrongPassword_IsNotActivity()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hasher = new PasswordHasher<User>();
        var user = NewUser("Ana", Roles.Customer, daysAgo: 350, warnedDaysAgo: 10);
        user.Email = "ana@test.com";
        user.PasswordHash = hasher.HashPassword(user, "Secret123");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new AuthService(db, hasher).ValidateLoginAsync("ana@test.com", "Wrong999");

        var saved = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(Now.AddDays(-350), saved.LastActivityAt);
        Assert.NotNull(saved.InactivityWarnedAt);
    }

    [Fact]
    public void ANewAccount_StartsActiveNow()
        => Assert.True(new User().LastActivityAt > DateTime.UtcNow.AddMinutes(-1));

    // ------------------------------------------------------------ report only (the default)

    [Fact]
    public async Task ReportOnly_ChangesNothing_SendsNothing_AndStoresNothing()
    {
        var w = await WorldAsync();
        var ana = await CustomerAsync(w, daysAgo: 340);
        var mary = await ProviderAsync(w, daysAgo: 500, warnedDaysAgo: 60);

        var first = await w.Svc.RunAsync(deleteEnabled: false);
        var second = await w.Svc.RunAsync(deleteEnabled: false);

        Assert.Equal((1, 1), (first.WouldWarn, first.OverOneYear));      // Ana would be warned; Mary is over a year (already warned)
        Assert.Equal(first, second);
        Assert.Empty(w.Email.Sent);
        Assert.True(await UserExists(w, ana.UserId) && await UserExists(w, mary.UserId));
        Assert.Null((await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == ana.UserId)).InactivityWarnedAt);
        // database v2.10: no REPORT line any more - the admin dashboard works the numbers out when it opens
        Assert.Empty(w.Db.SystemLogs);
    }

    [Fact]
    public async Task ReportOnly_WithNothingToSay_WritesNothing()
    {
        var w = await WorldAsync();
        await CustomerAsync(w, daysAgo: 10);

        await w.Svc.RunAsync(deleteEnabled: false);

        Assert.Empty(w.Db.SystemLogs);
    }

    // ------------------------------------------------------------ the warning

    [Fact]
    public async Task At335Days_TheWarningEmailGoesOut_Once()
    {
        var w = await WorldAsync();
        var fresh = await CustomerAsync(w, daysAgo: 334);
        var ana = await CustomerAsync(w, daysAgo: 335);

        var run = await w.Svc.RunAsync(deleteEnabled: true);
        await w.Svc.RunAsync(deleteEnabled: true);

        Assert.Equal(1, run.Warned);
        var mail = Assert.Single(w.Email.Sent);
        Assert.Equal(ana.User.Email, mail.To);
        Assert.Equal("Your SERVISYO account will be deleted on December 31, 2027", mail.Subject);
        Assert.Contains("sign in before December 31, 2027", mail.Text);
        Assert.Equal(Now, (await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == ana.UserId)).InactivityWarnedAt);
        Assert.Null((await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == fresh.UserId)).InactivityWarnedAt);
    }

    [Fact]
    public async Task AWarningThatCouldNotBeSent_IsNotSaved_SoNobodyIsRemovedWithoutNotice()
    {
        var w = await WorldAsync();
        var ana = await CustomerAsync(w, daysAgo: 500);
        w.Email.Works = false;

        var run = await w.Svc.RunAsync(deleteEnabled: true);

        Assert.Equal((0, 1), (run.Warned, run.WarningsNotSent));
        Assert.Null((await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == ana.UserId)).InactivityWarnedAt);
        Assert.True(await UserExists(w, ana.UserId));
    }

    [Fact]
    public async Task ALateWarning_StillGivesTheFull30Days()
    {
        var w = await WorldAsync();
        await CustomerAsync(w, daysAgo: 400);          // first seen by the job after more than a year

        await w.Svc.RunAsync(deleteEnabled: true);

        Assert.Contains("December 31, 2027", Assert.Single(w.Email.Sent).Subject);   // today + 30 days, not "already passed"
    }

    // ------------------------------------------------------------ who is removed, and when

    [Theory]
    [InlineData(400, 29, true)]      // warned 29 days ago: wait
    [InlineData(400, 30, false)]     // 30 days after the warning: removed
    [InlineData(364, 40, true)]      // not a full year yet
    public async Task RemovalNeedsAYear_AndThe30DaysAfterTheWarning(int inactiveDays, int warnedDaysAgo, bool stays)
    {
        var w = await WorldAsync();
        var ana = await CustomerAsync(w, inactiveDays, warnedDaysAgo);

        await w.Svc.RunAsync(deleteEnabled: true);

        Assert.Equal(stays, await UserExists(w, ana.UserId));
    }

    [Fact]
    public async Task AWarningFromBeforeTheLastSignIn_DoesNotCount()
    {
        var w = await WorldAsync();
        var ana = await CustomerAsync(w, daysAgo: 400, warnedDaysAgo: 450);     // warned in an earlier inactive period

        var run = await w.Svc.RunAsync(deleteEnabled: true);

        Assert.True(await UserExists(w, ana.UserId));
        Assert.Equal(1, run.Warned);                                             // warned again for this period instead
    }

    [Fact]
    public async Task Administrators_AreNeverWarnedOrRemoved()
    {
        var w = await WorldAsync();
        w.Admin.InactivityWarnedAt = Now.AddDays(-60);
        await w.Db.SaveChangesAsync();

        await w.Svc.RunAsync(deleteEnabled: true);

        Assert.True(await UserExists(w, w.Admin.UserId));
        Assert.Empty(w.Email.Sent);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending, null)]
    [InlineData(BookingStatuses.Accepted, null)]
    [InlineData(BookingStatuses.InProgress, null)]
    [InlineData(BookingStatuses.Completed, PaymentRecordStatuses.PendingCollection)]   // the cash is not collected yet
    public async Task AnUnfinishedBooking_ProtectsBothPeople(string status, string? cash)
    {
        var w = await WorldAsync();
        var ana = await CustomerAsync(w, daysAgo: 500, warnedDaysAgo: 60);
        var mary = await ProviderAsync(w, daysAgo: 500, warnedDaysAgo: 60);
        await BookingAsync(w, ana, mary, status, cash);

        var run = await w.Svc.RunAsync(deleteEnabled: true);

        Assert.Equal((0, 0, 0), (run.Deleted, run.Erased, run.Warned));
        Assert.Equal("Ana", (await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == ana.UserId)).FirstName);
        Assert.Equal("Mary", (await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == mary.UserId)).FirstName);
    }

    // ------------------------------------------------------------ removing an account that shares nothing

    [Fact]
    public async Task WithoutBookings_TheAccountAndEverythingItOwnsIsDeleted_WithTheAuditLine()
    {
        var w = await WorldAsync();
        var mary = await ProviderAsync(w, daysAgo: 400, warnedDaysAgo: 31);
        var other = await CustomerAsync(w, daysAgo: 1);
        var maryEmail = mary.User.Email;
        var pid = mary.ProviderId;

        var day = new ProviderAvailability { ProviderId = pid, DayOfWeek = 1, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0) };
        day.Breaks.Add(new ProviderBreak { BreakStart = new TimeOnly(12, 0), BreakEnd = new TimeOnly(13, 0) });
        w.Db.ProviderAvailabilities.Add(day);
        var decision = AdminAction.ForProvider(w.Admin.UserId, "Day Off Approved", pid, "Nov 20, 2026");
        var request = new ProviderDayOffRequest { ProviderId = pid, Reason = "Trip", Status = DayOffStatuses.Approved, DecidedBy = decision };
        request.Days.Add(new ProviderUnavailableDate { ProviderId = pid, UnavailableDate = new DateOnly(2026, 11, 20) });
        w.Db.DayOffRequests.Add(request);
        w.Db.NameChangeRequests.Add(new ProviderNameChangeRequest { ProviderId = pid, FirstName = "Maria", LastName = "Reyes", Reason = "Married" });
        w.Db.ProviderCategories.Add(new ProviderCategory { ProviderId = pid, Category = new ServiceCategory { CategoryName = "Plumbing" } });
        w.Db.Services.Add(new Service { ProviderId = pid, Category = new ServiceCategory { CategoryName = "Electrical" }, ServiceName = "Wiring", Price = 500m });
        w.Db.Messages.Add(new Message { SenderId = other.UserId, ReceiverId = mary.UserId, MessageText = "Hi" });
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();

        var run = await w.Svc.RunAsync(deleteEnabled: true);

        Assert.Equal((1, 0), (run.Deleted, run.Erased));
        Assert.False(await UserExists(w, mary.UserId));
        Assert.False(await w.Db.Providers.AnyAsync());
        Assert.False(await w.Db.ProviderAvailabilities.AnyAsync() || await w.Db.ProviderBreaks.AnyAsync());
        Assert.False(await w.Db.ProviderUnavailableDates.AnyAsync() || await w.Db.DayOffRequests.AnyAsync() || await w.Db.NameChangeRequests.AnyAsync());
        Assert.False(await w.Db.ProviderCategories.AnyAsync() || await w.Db.Services.AnyAsync() || await w.Db.Messages.AnyAsync());
        Assert.False(await w.Db.AdminActions.AnyAsync());
        Assert.True(await UserExists(w, other.UserId));                          // nobody else is touched

        var log = Assert.Single(w.Db.SystemLogs);
        Assert.Null(log.UserId);
        Assert.Equal(("System", "DELETE", "User", (int?)mary.UserId), (log.UserRole, log.ActionType, log.EntityName, log.EntityId));
        // database v2.10: the standard DELETE format, "{Role} deleted {Entity} #{Id}: {summary}."
        Assert.Equal($"System deleted User #{mary.UserId}: Automatically purged inactive account (> 1 year): {maryEmail}.", log.Description);
    }

    // ------------------------------------------------------------ removing an account that has finished bookings

    [Fact]
    public async Task WithFinishedBookings_ThePersonIsErased_TheBookingsPaymentsAndReviewsStay()
    {
        var w = await WorldAsync();
        var ana = await CustomerAsync(w, daysAgo: 400, warnedDaysAgo: 31);
        var mary = await ProviderAsync(w, daysAgo: 1);
        var booking = await BookingAsync(w, ana, mary, BookingStatuses.Completed, PaymentRecordStatuses.Paid);
        w.Db.Reviews.Add(new Review { BookingId = booking.BookingId, Rating = 5, Comment = "Great" });
        w.Db.Messages.Add(new Message { SenderId = ana.UserId, ReceiverId = mary.UserId, MessageText = "Thanks!" });
        await w.Db.SaveChangesAsync();
        var anaEmail = ana.User.Email;
        w.Db.ChangeTracker.Clear();

        var run = await w.Svc.RunAsync(deleteEnabled: true);
        Assert.Equal((0, 1), (run.Deleted, run.Erased));

        var user = await w.Db.Users.AsNoTracking().Include(u => u.Customer).SingleAsync(u => u.UserId == ana.UserId);
        Assert.Equal("Deleted", user.FirstName);
        Assert.Null(user.MiddleName);
        Assert.Equal("User", user.LastName);
        Assert.Equal(InactiveAccountRules.DeletedEmail(ana.UserId), user.Email);
        Assert.Null(user.PhoneNumber);
        Assert.Equal("", user.PasswordHash);                                      // can never sign in again
        Assert.Equal(AccountStatuses.Deactivated, user.AccountStatus);
        Assert.Null(user.Customer!.Address);
        Assert.Null(user.Customer.ProfilePhoto);

        var kept = await w.Db.Bookings.AsNoTracking().Include(b => b.Review).Include(b => b.PaymentDetail).SingleAsync();
        Assert.Equal(BookingStatuses.Completed, kept.Status);
        Assert.Equal(InactiveAccountRules.RemovedLocation, kept.ServiceLocation);
        Assert.Equal(5, kept.Review!.Rating);
        Assert.NotNull(kept.PaymentDetail);
        Assert.Single(w.Db.Messages);

        var log = Assert.Single(w.Db.SystemLogs);
        Assert.Equal($"System deleted User #{ana.UserId}: Automatically purged inactive account (> 1 year): {anaEmail} (finished bookings kept under \"Deleted User\").", log.Description);

        // an erased account is never processed again
        var again = await w.Svc.RunAsync(deleteEnabled: true);
        Assert.Equal((0, 0, 0), (again.Warned, again.Deleted, again.Erased));
        Assert.Empty(w.Email.Sent);
    }

    [Fact]
    public async Task AnErasedProvider_StopsBeingBookable_AndKeepsOnlyTheServicesItsBookingsNeed()
    {
        var w = await WorldAsync();
        var ana = await CustomerAsync(w, daysAgo: 1);
        var mary = await ProviderAsync(w, daysAgo: 400, warnedDaysAgo: 31);
        await BookingAsync(w, ana, mary, BookingStatuses.Completed, PaymentRecordStatuses.Paid);
        w.Db.Services.Add(new Service { ProviderId = mary.ProviderId, Category = new ServiceCategory { CategoryName = "Electrical" }, ServiceName = "Never booked", Price = 100m });
        w.Db.ProviderAvailabilities.Add(new ProviderAvailability { ProviderId = mary.ProviderId, DayOfWeek = 2, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0) });
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();

        await w.Svc.RunAsync(deleteEnabled: true);

        var provider = await w.Db.Providers.AsNoTracking().SingleAsync();
        Assert.Equal(InactiveAccountRules.DeletedBusinessName, provider.BusinessName);
        Assert.Equal(AccountStatuses.Deactivated, provider.AccountStatus);
        Assert.Null(provider.Description);
        var service = await w.Db.Services.AsNoTracking().SingleAsync();          // "Never booked" is gone
        Assert.Equal("Pipe Repair", service.ServiceName);
        Assert.False(service.IsActive);
        Assert.False(await w.Db.ProviderAvailabilities.AnyAsync());
        Assert.Equal("Ana", (await w.Db.Users.AsNoTracking().SingleAsync(u => u.UserId == ana.UserId)).FirstName);
    }

    // ------------------------------------------------------------ the model, the worker, the SQL and the files

    [Fact]
    public void TheColumns_AreInTheModel()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var users = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(User))!;
        var last = users.FindProperty(nameof(User.LastActivityAt))!;
        Assert.Equal("LAST_ACTIVITY_AT", last.GetColumnName());
        Assert.False(last.IsNullable);
        Assert.Equal("SYSUTCDATETIME()", last.GetDefaultValueSql());
        var warned = users.FindProperty(nameof(User.InactivityWarnedAt))!;
        Assert.Equal("INACTIVITY_WARNED_AT", warned.GetColumnName());
        Assert.True(warned.IsNullable);
        Assert.Contains(users.GetIndexes(), i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(User.LastActivityAt));
    }

    [Fact]
    public void TheWorker_RunsSoonAfterStart_ThenDaily_AndStartsInReportOnlyMode()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), InactiveAccountCleanupService.FirstRunDelay);
        Assert.Equal(TimeSpan.FromHours(24), InactiveAccountCleanupService.Interval);
        Assert.False(new InactiveAccountOptions().DeleteEnabled);
        Assert.Equal((365, 30, 335), (InactiveAccountRules.InactiveDays, InactiveAccountRules.WarningDays, InactiveAccountRules.WarnAfterDays));

        var root = RepoRoot();
        var settings = File.ReadAllText(Path.Combine(root, "src", "Servisyo.Web", "appsettings.json"));
        Assert.Matches("\"InactiveAccounts\"\\s*:\\s*\\{\\s*\"DeleteEnabled\"\\s*:\\s*false", settings);
        var program = File.ReadAllText(Path.Combine(root, "src", "Servisyo.Web", "Program.cs"));
        Assert.Contains("AddHostedService<InactiveAccountCleanupService>()", program);
    }

    [Fact]
    public void TheMigrationStep_StartsEachAccountFromItsLastSignIn()
    {
        Assert.StartsWith("EXEC(N'UPDATE u", InactiveAccountsSql.InitLastActivity);
        Assert.Contains("MAX(l.CREATED_AT) FROM dbo.SYSTEM_LOGS l WHERE l.USER_ID = u.USER_ID AND l.ACTION_TYPE = N''LOGIN''", InactiveAccountsSql.InitLastActivity);
        Assert.Contains("u.CREATED_AT)", InactiveAccountsSql.InitLastActivity);
    }

    [Fact]
    public void TheDesignFile_AndTheTools_HaveV29()
    {
        var root = RepoRoot();
        var design = File.ReadAllText(Path.Combine(root, "docs", "sql", "DatabaseDesign-v2.sql")).Replace("\r\n", "\n");
        Assert.Contains("DATABASE DESIGN v2.10", design);                        // v2.10 came after
        Assert.Contains("WHAT CHANGED IN v2.9 (EF migration \"InactiveAccounts\")", design);
        Assert.Contains("INACTIVITY_WARNED_AT         DATETIME2       NULL", design);
        Assert.Contains("LAST_ACTIVITY_AT             DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()", design);
        Assert.Contains("CREATE INDEX IX_USERS_LAST_ACTIVITY_AT ON USERS (LAST_ACTIVITY_AT);", design);
        Assert.Contains("MigrationId LIKE N'%[_]AuditStandard'", design);          // PART 0: the newest migration (v2.10)

        var tool = File.ReadAllText(Path.Combine(root, "tools", "add-inactive-accounts.ps1"));
        Assert.Contains("InactiveAccountsSql.InitLastActivity", tool);
        Assert.Contains("IX_USERS_LAST_ACTIVITY_AT", tool);
        var deploy = File.ReadAllText(Path.Combine(root, "tools", "make-monster-deploy.ps1"));
        Assert.Contains("$Latest = \"AuditStandard\"", deploy);                   // v2.10 is the newest now
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
