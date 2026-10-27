using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class SystemLogServiceTests
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

    [Fact]
    public async Task OnlyActiveAdministrators_CanSeeTheLogs()
    {
        var db = NewDb();
        var customer = NewUser("Ana", Roles.Customer);
        var suspendedAdmin = NewUser("Old", Roles.Administrator, AccountStatuses.Suspended);
        db.Users.AddRange(customer, suspendedAdmin);
        await db.SaveChangesAsync();
        var svc = new SystemLogService(db, new FixedClock());

        Assert.False((await svc.GetPageAsync(customer.UserId, new SystemLogQuery())).Allowed);
        Assert.False((await svc.GetPageAsync(suspendedAdmin.UserId, new SystemLogQuery())).Allowed);
    }

    [Fact]
    public async Task Filters_ByAction_Role_Search_AndCebuDates_NewestFirst()
    {
        var db = NewDb();
        var admin = await AdminAsync(db);
        Log(db, "INSERT", "Customer", "Booking #1 requested", new DateTime(2026, 10, 26, 15, 0, 0));      // Oct 26 23:00 Cebu
        Log(db, "UPDATE", "Service Provider", "Booking #1 status changed: Pending -> Accepted", new DateTime(2026, 10, 26, 17, 0, 0)); // Oct 27 01:00 Cebu
        Log(db, "UPDATE", "Customer", "Booking #1 payment: Unpaid -> Paid", new DateTime(2026, 10, 27, 3, 0, 0), entity: "Payment");
        Log(db, "LOGIN", "Administrator", "Ada Test signed in.", new DateTime(2026, 10, 27, 4, 0, 0), admin.UserId, "User", admin.UserId);
        await db.SaveChangesAsync();
        var svc = new SystemLogService(db, new FixedClock());

        var all = (await svc.GetPageAsync(admin.UserId, new SystemLogQuery())).Page!;
        Assert.Equal(4, all.TotalMatching);
        Assert.Equal("LOGIN", all.Rows[0].Action);                                 // newest first
        Assert.Equal($"Ada Test (#{admin.UserId})", all.Rows[0].UserText);
        Assert.Equal("Oct 27, 2026 12:00:00 PM", all.Rows[0].TimeText);            // shown in Cebu time

        Assert.Equal(2, (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Action = "update" })).Page!.TotalMatching);
        Assert.Equal(2, (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Role = "Customer" })).Page!.TotalMatching);
        Assert.Single((await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Q = "payment" })).Page!.Rows);

        // Oct 27 (Cebu) = from Oct 26 16:00 UTC: the 23:00-Cebu log of the 26th is left out
        var day = (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { From = new DateOnly(2026, 10, 27), To = new DateOnly(2026, 10, 27) })).Page!;
        Assert.Equal(3, day.TotalMatching);
    }

    [Fact]
    public async Task UnknownFilterValues_AreIgnored_AndPagesHold25()
    {
        var db = NewDb();
        var admin = await AdminAsync(db);
        for (var i = 0; i < 30; i++) Log(db, "INSERT", "Customer", $"Booking #{i} requested", new DateTime(2026, 10, 1).AddMinutes(i));
        await db.SaveChangesAsync();
        var svc = new SystemLogService(db, new FixedClock());

        var page1 = (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Action = "DROP TABLE", Role = "Hacker" })).Page!;
        Assert.Equal(30, page1.TotalMatching);
        Assert.Equal(SystemLogsPageViewModel.PageSize, page1.Rows.Count);
        Assert.Null(page1.Query.Action);
        Assert.Null(page1.Query.Role);

        var page2 = (await svc.GetPageAsync(admin.UserId, new SystemLogQuery { Page = 99 })).Page!;
        Assert.Equal(2, page2.Page);                                               // clamped to the last page
        Assert.Equal(5, page2.Rows.Count);
    }

    [Fact]
    public async Task LogLogin_WritesALoginLine()
    {
        var db = NewDb();
        var ana = NewUser("Ana", Roles.Customer);
        db.Users.Add(ana);
        await db.SaveChangesAsync();

        await new SystemLogService(db, new FixedClock()).LogLoginAsync(ana);

        var line = await db.SystemLogs.SingleAsync();
        Assert.Equal("LOGIN", line.ActionType);
        Assert.Equal(Roles.Customer, line.UserRole);
        Assert.Equal(ana.UserId, line.UserId);
        Assert.Contains("signed in", line.Description);
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
