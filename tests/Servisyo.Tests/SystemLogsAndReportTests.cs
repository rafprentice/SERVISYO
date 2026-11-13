using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.10: Admin > System Logs (IAuditLogService) - 20 lines a page, newest first, one pill per action type
/// with its count, filters kept; the app's own LOGIN lines (signed in / failed) in the standard format.
public class AuditLogServiceTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 2, 0, 0, TimeSpan.Zero);     // Oct 28, 10:00 Cebu
    }

    private static ServisyoDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User NewUser(string first, string role, string status = AccountStatuses.Active) =>
        new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status };

    private static async Task<User> AdminAsync(ServisyoDbContext db)
    {
        var admin = NewUser("Ada", Roles.Administrator);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    private static void Log(ServisyoDbContext db, string action, string role, string text, DateTime utc, int? userId = null, string entity = "Booking", int? entityId = 1) =>
        db.SystemLogs.Add(new SystemLog { ActionType = action, UserRole = role, Description = text, CreatedAt = utc, UserId = userId, EntityName = entity, EntityId = entityId });

    private static AuditLogService Svc(ServisyoDbContext db) => new(db, new FixedClock());

    [Fact]
    public async Task OnlyActiveAdministrators_CanSeeTheLogs()
    {
        var db = NewDb();
        var customer = NewUser("Ana", Roles.Customer);
        var suspendedAdmin = NewUser("Old", Roles.Administrator, AccountStatuses.Suspended);
        db.Users.AddRange(customer, suspendedAdmin);
        await db.SaveChangesAsync();

        Assert.False((await Svc(db).GetPageAsync(customer.UserId, new SystemLogQuery())).Allowed);
        Assert.False((await Svc(db).GetPageAsync(suspendedAdmin.UserId, new SystemLogQuery())).Allowed);
    }

    [Fact]
    public async Task Filters_ByActionType_Role_Search_AndCebuDates_NewestFirst()
    {
        var db = NewDb();
        var admin = await AdminAsync(db);
        Log(db, "INSERT", "Customer", "Customer Ana Test created Booking #1: Pending request", new DateTime(2026, 10, 26, 15, 0, 0));          // Oct 26 23:00 Cebu
        Log(db, "UPDATE", "Service Provider", "Service Provider Mary Test updated Booking #1: status 'Pending' -> 'Accepted'.", new DateTime(2026, 10, 26, 17, 0, 0)); // Oct 27 01:00 Cebu
        Log(db, "UPDATE", "Customer", "Customer Ana Test updated Payment #3: status 'PendingCollection' -> 'Paid'.", new DateTime(2026, 10, 27, 3, 0, 0), entity: "Payment");
        Log(db, "LOGIN", "Administrator", "Ada Test signed in.", new DateTime(2026, 10, 27, 4, 0, 0), admin.UserId, "User", admin.UserId);
        await db.SaveChangesAsync();
        var svc = Svc(db);

        var all = (await svc.GetPageAsync(admin.UserId, new SystemLogQuery())).Page!;
        Assert.Equal(4, all.Logs.TotalCount);
        Assert.Equal("LOGIN", all.Logs.Items[0].Action);                            // newest first
        Assert.Equal($"Ada Test (#{admin.UserId})", all.Logs.Items[0].UserText);
        Assert.Equal("Oct 27, 2026 12:00:00 PM", all.Logs.Items[0].TimeText);       // shown in Cebu time

        Assert.Equal(2, (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { ActionType = "update" })).Page!.Logs.TotalCount);
        Assert.Equal("UPDATE", (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { ActionType = " update " })).Page!.Query.ActionType);
        Assert.Equal(2, (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Role = "Customer" })).Page!.Logs.TotalCount);
        Assert.Single((await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Search = "Payment #3" })).Page!.Logs.Items);

        // Oct 27 (Cebu) = from Oct 26 16:00 UTC: the 23:00-Cebu log of the 26th is left out
        var day = (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { From = new DateOnly(2026, 10, 27), To = new DateOnly(2026, 10, 27) })).Page!;
        Assert.Equal(3, day.Logs.TotalCount);
    }

    [Fact]
    public async Task ThePills_AreAll_Insert_Update_Delete_Login_WithCounts_InsideTheOtherFilters()
    {
        var db = NewDb();
        var admin = await AdminAsync(db);
        var t = new DateTime(2026, 10, 20, 2, 0, 0);
        Log(db, "INSERT", "Customer", "created Booking #1", t);
        Log(db, "UPDATE", "Customer", "updated Customer #1: address 'Lahug' -> 'Mabolo'.", t.AddMinutes(1), entity: "Customer");
        Log(db, "UPDATE", "Customer", "updated User #2: phone '09171234567' -> '09181234567'.", t.AddMinutes(2), entity: "User");
        Log(db, "UPDATE", "Service Provider", "updated Service #4: price 'PHP 500.00' -> 'PHP 650.00'.", t.AddMinutes(3), entity: "Service");
        Log(db, "DELETE", "Administrator", "deleted Service #4: Wiring (PHP 650.00).", t.AddMinutes(4), entity: "Service");
        Log(db, "LOGIN", "Guest", "Failed login attempt for x@test.com.", t.AddMinutes(5), entity: "User", entityId: null);
        await db.SaveChangesAsync();

        var page = (await Svc(db).GetPageAsync(admin.UserId, new SystemLogQuery { ActionType = "UPDATE" })).Page!;
        Assert.Equal(new[] { "All", "INSERT", "UPDATE", "DELETE", "LOGIN" }, page.Tabs.Select(x => x.Label));
        Assert.Equal(new int[] { 6, 1, 3, 1, 1 }, page.Tabs.Select(x => x.Count));
        Assert.Equal(new string?[] { null, "INSERT", "UPDATE", "DELETE", "LOGIN" }, page.Tabs.Select(x => x.ActionType));
        Assert.Equal(3, page.Logs.TotalCount);
        Assert.All(page.Logs.Items, r => Assert.Equal("accepted", r.ActionTone));

        // the counts follow the other filters (a role), the pill stays
        var customers = (await Svc(db).GetPageAsync(admin.UserId, new SystemLogQuery { ActionType = "UPDATE", Role = "Customer" })).Page!;
        Assert.Equal(new int[] { 3, 1, 2, 0, 0 }, customers.Tabs.Select(x => x.Count));
        Assert.Equal(2, customers.Logs.TotalCount);
    }

    [Fact]
    public async Task UnknownFilterValues_AreIgnored_PagesHold20_AndTheLastPageIsKept()
    {
        var db = NewDb();
        var admin = await AdminAsync(db);
        for (var i = 0; i < 30; i++) Log(db, "INSERT", "Customer", $"created Booking #{i}", new DateTime(2026, 10, 1).AddMinutes(i));
        await db.SaveChangesAsync();
        var svc = Svc(db);

        var page1 = (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { ActionType = "REPORT", Role = "Hacker" })).Page!;
        Assert.Equal(20, SystemLogsPageViewModel.PageSize);
        Assert.Equal(30, page1.Logs.TotalCount);
        Assert.Equal(SystemLogsPageViewModel.PageSize, page1.Logs.Items.Count);
        Assert.Null(page1.Query.ActionType);                                       // REPORT is not an action type any more
        Assert.Null(page1.Query.Role);
        Assert.Equal((1, 2, false, true), (page1.Logs.Page, page1.Logs.PageCount, page1.Logs.HasPrevious, page1.Logs.HasNext));
        Assert.Equal("created Booking #29", page1.Logs.Items[0].Description);        // newest first

        var last = (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Page = 99 })).Page!;
        Assert.Equal(2, last.Logs.Page);                                           // clamped to the last page
        Assert.Equal(10, last.Logs.Items.Count);
        Assert.Equal("created Booking #9", last.Logs.Items[0].Description);
        Assert.Equal(1, (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Page = -5 })).Page!.Logs.Page);
    }

    [Fact]
    public async Task EveryLink_KeepsTheFilters()
    {
        var db = NewDb();
        var admin = await AdminAsync(db);
        var vm = (await Svc(db).GetPageAsync(admin.UserId, new SystemLogQuery
        {
            ActionType = "delete", Search = "  Service #4 ", Role = "administrator", From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31)
        })).Page!;

        Assert.Equal(("DELETE", "Service #4", "Administrator"), (vm.Query.ActionType, vm.Query.Search, vm.Query.Role));
        var link = vm.Link("LOGIN", 3);
        Assert.Equal("LOGIN", Prop(link, "actionType"));
        Assert.Equal("Service #4", Prop(link, "search"));
        Assert.Equal("Administrator", Prop(link, "role"));
        Assert.Equal("2026-10-01", Prop(link, "from"));
        Assert.Equal("2026-10-31", Prop(link, "to"));
        Assert.Equal(3, Prop(link, "page"));
        Assert.Null(Prop(vm.Link(null, 1), "page"));                               // page 1 needs no number
        Assert.Null(Prop(vm.Link(null, 1), "actionType"));                         // the All pill

        static object? Prop(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);
    }

    [Fact]
    public async Task LogLogin_WritesTheStandardLoginLine()
    {
        var db = NewDb();
        var ana = NewUser("Ana", Roles.Customer);
        ana.MiddleName = "Cruz";
        db.Users.Add(ana);
        await db.SaveChangesAsync();

        await Svc(db).LogLoginAsync(ana);

        var line = await db.SystemLogs.SingleAsync();
        Assert.Equal((AuditActionType.Login, Roles.Customer, (int?)ana.UserId, "User", (int?)ana.UserId),
            (line.ActionType, line.UserRole, line.UserId, line.EntityName, line.EntityId));
        Assert.Equal($"Ana Cruz Test ({ana.Email}) signed in.", line.Description);
        Assert.Equal(new DateTime(2026, 10, 28, 2, 0, 0), line.CreatedAt);
    }

    [Fact]
    public async Task AFailedLogin_IsALoginLine_ForAGuest_WithWhatWasTyped()
    {
        var db = NewDb();

        await Svc(db).LogFailedLoginAsync("  Ana@Gmail.COM ");
        await Svc(db).LogFailedLoginAsync(new string('a', 300) + "@x.com");
        await Svc(db).LogFailedLoginAsync(null);

        var lines = await db.SystemLogs.OrderBy(l => l.SystemLogId).ToListAsync();
        Assert.All(lines, l =>
        {
            Assert.Equal(AuditActionType.Login, l.ActionType);
            Assert.Equal("Guest", l.UserRole);
            Assert.Null(l.UserId);
            Assert.Null(l.EntityId);
        });
        Assert.Equal("Failed login attempt for ana@gmail.com.", lines[0].Description);
        Assert.Equal("Failed login attempt for " + new string('a', 97) + "....", lines[1].Description);   // at most 100 characters of it
        Assert.Equal("Failed login attempt for (none).", lines[2].Description);
    }

    [Fact]
    public void TheStandardFormat_FitsTheColumn()
    {
        Assert.Equal("System deleted User #12: gone.", AuditFormat.Deleted("System", "User", 12, "gone"));
        Assert.Equal("Ana Reyes", AuditFormat.FullName(" Ana ", "", "Reyes "));
        Assert.Equal("Ana Cruz Reyes", AuditFormat.FullName("Ana", "Cruz", "Reyes"));
        var longLine = AuditFormat.Deleted("System", "User", 1, new string('x', 600));
        Assert.Equal(500, longLine.Length);
        Assert.EndsWith("...", longLine);
    }

    [Fact]
    public void OnlyTheFourActionTypes_Exist()
    {
        Assert.Equal(new[] { "INSERT", "UPDATE", "DELETE", "LOGIN" }, AuditActionType.All);
        Assert.Equal("[ACTION_TYPE] IN ('INSERT','UPDATE','DELETE','LOGIN')", AuditActionType.Check);
        Assert.Null(typeof(AuditActionType).GetField("Report"));
    }
}

public class AdminReportServiceTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 2, 0, 0, TimeSpan.Zero);
    }

    private static User NewUser(string first, string role, DateTime? created = null) =>
        new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role,
                AccountStatus = AccountStatuses.Active, CreatedAt = created ?? new DateTime(2026, 10, 5) };

    [Fact]
    public async Task MonthlyReport_SumsPaidBookings_SplitsFees_CountsStatuses_AndRanksCategories()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", Roles.Administrator);
        var ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        var old = new Customer { User = NewUser("Ben", Roles.Customer, new DateTime(2026, 8, 1)) };
        var nina = new Provider { BusinessName = "Nina", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Nina", Roles.ServiceProvider) };
        var waiting = new Provider { BusinessName = "New", ApprovalStatus = ApprovalStatuses.Pending, User = NewUser("Neo", Roles.ServiceProvider) };
        var cleaning = new ServiceCategory { CategoryName = "Cleaning" };
        var plumbing = new ServiceCategory { CategoryName = "Plumbing" };
        db.AddRange(admin, ana, old, nina, waiting, cleaning, plumbing);
        await db.SaveChangesAsync();
        var deep = new Service { ProviderId = nina.ProviderId, CategoryId = cleaning.CategoryId, ServiceName = "Deep Cleaning", Price = 1500m };
        var pipe = new Service { ProviderId = nina.ProviderId, CategoryId = plumbing.CategoryId, ServiceName = "Pipe Repair", Price = 600m };
        db.Services.AddRange(deep, pipe);
        await db.SaveChangesAsync();

        void Book(DateOnly day, string status, string pay, decimal fee, Service s)
        {
            var b = new Booking { CustomerId = ana.CustomerId, ProviderId = nina.ProviderId, BookingDate = day, BookingTime = new TimeOnly(9, 0),
                                  TotalDurationMinutes = 60, TotalFee = fee, Status = status, PaymentStatus = pay, RowVersion = new byte[] { 1 } };
            b.Services.Add(new BookingService { ServiceId = s.ServiceId, PriceSnapshot = fee, DurationMinutesSnapshot = 60 });
            db.Bookings.Add(b);
        }
        Book(new DateOnly(2026, 10, 3), BookingStatuses.Completed, PaymentStatuses.Paid, 1500m, deep);
        Book(new DateOnly(2026, 10, 9), BookingStatuses.Completed, PaymentStatuses.Paid, 1500m, deep);
        Book(new DateOnly(2026, 10, 12), BookingStatuses.Accepted, PaymentStatuses.Paid, 600m, pipe);
        Book(new DateOnly(2026, 10, 15), BookingStatuses.Cancelled, PaymentStatuses.Unpaid, 600m, pipe);
        Book(new DateOnly(2026, 10, 20), BookingStatuses.Pending, PaymentStatuses.Unpaid, 1500m, deep);
        Book(new DateOnly(2026, 9, 20), BookingStatuses.Completed, PaymentStatuses.Paid, 999m, deep);        // other month
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Reports:PlatformFeePercent"] = "12.5" }).Build();
        var r = (await new AdminReportService(db, config, new FixedClock()).GetMonthlyAsync(admin.UserId, "2026-10")).Report!;

        Assert.Equal("October 2026", r.MonthLabel);
        Assert.Equal(3600m, r.TotalRevenue);                 // 1500 + 1500 + 600 (paid only)
        Assert.Equal(3, r.PaidBookings);
        Assert.Equal(12.5m, r.PlatformFeePercent);
        Assert.Equal(450m, r.PlatformFees);
        Assert.Equal(3150m, r.ProviderEarnings);
        Assert.Equal(5, r.TotalBookings);
        Assert.Equal(2, r.Completed);
        Assert.Equal(1, r.Cancelled);
        Assert.Equal(66.7m, r.CompletionRatePercent);        // 2 of 3 finished
        Assert.Equal("Cleaning", r.TopCategories[0].Name);
        Assert.Equal(3, r.TopCategories[0].Bookings);
        Assert.Equal(60m, r.TopCategories[0].SharePercent);
        Assert.Equal(2, r.ActiveCustomers);
        Assert.Equal(1, r.ActiveProviders);
        Assert.Equal(1, r.PendingApplications);
        Assert.Equal(1, r.NewCustomers);                     // Ben joined in August
        Assert.Equal(6, r.Trend.Count);
        Assert.Equal(999m, r.Trend[4].Revenue);              // September
        Assert.Equal(3600m, r.Trend[5].Revenue);             // October
    }

    [Fact]
    public async Task NonAdmins_GetNothing_AndABadMonthMeansThisMonth()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ana = NewUser("Ana", Roles.Customer);
        var admin = NewUser("Ada", Roles.Administrator);
        db.Users.AddRange(ana, admin);
        await db.SaveChangesAsync();
        var svc = new AdminReportService(db, null, new FixedClock());

        Assert.False((await svc.GetMonthlyAsync(ana.UserId, null)).Allowed);
        var r = (await svc.GetMonthlyAsync(admin.UserId, "not-a-month")).Report!;
        Assert.Equal("2026-10", r.MonthKey);
        Assert.Equal(AdminReportService.DefaultPlatformFeePercent, r.PlatformFeePercent);
        Assert.Equal(0m, r.TotalRevenue);
    }
}
