using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ProviderDetailsTests
{
    private static (ProviderManagementService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ServisyoDbContext(options);
        return (new ProviderManagementService(db), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active, string? phone = null)
        => new() { FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status, PhoneNumber = phone };

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var u = NewUser("Ada", "Admin", Roles.Administrator, status);
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string account = AccountStatuses.Active,
        string approval = ApprovalStatuses.Approved)
    {
        var p = new Provider
        {
            BusinessName = "Vincent Plumbing", City = "Cebu City", Address = "12 Colon St", Description = "Good in Plumbing and Electrical",
            ApprovalStatus = approval, User = NewUser("Vincent", "Valencia", Roles.ServiceProvider, account, "+639178889999")
        };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    // ------------------------------------------------------------------ the details

    [Fact]
    public async Task Details_ShowTheWholeProfile()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);
        var cat = new ServiceCategory { CategoryName = "Plumbing", IsActive = true };
        db.Services.AddRange(
            new Service { ProviderId = p.ProviderId, Category = cat, ServiceName = "Pipe Repair", Price = 500m, DurationMinutes = 60, IsActive = true },
            new Service { ProviderId = p.ProviderId, Category = cat, ServiceName = "Old Service", Price = 100m, DurationMinutes = 30, IsActive = false });
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        foreach (var status in new[] { BookingStatuses.Completed, BookingStatuses.Completed, BookingStatuses.Pending, BookingStatuses.Cancelled })
            db.Bookings.Add(new Booking
            {
                CustomerId = customer.CustomerId, ProviderId = p.ProviderId, BookingDate = new DateOnly(2026, 10, 20),
                BookingTime = new TimeOnly(9, 0), TotalDurationMinutes = 60, TotalFee = 500m, Status = status, RowVersion = new byte[] { 1 }
            });
        await db.SaveChangesAsync();

        var lookup = await svc.GetDetailsAsync(admin.UserId, p.ProviderId);

        Assert.Equal(ProviderDetailsOutcome.Ok, lookup.Outcome);
        var d = lookup.Details!;
        Assert.Equal("Vincent Valencia", d.Name);
        Assert.Equal("Vincent Plumbing", d.BusinessName);
        Assert.Equal("Plumbing · Cebu City", d.Location);
        Assert.Equal("+639178889999", d.Phone);
        Assert.Equal("12 Colon St", d.Address);
        Assert.Equal("Good in Plumbing and Electrical", d.Description);
        Assert.Equal(2, d.CompletedJobs);                         // only Completed bookings
        Assert.Equal("No reviews yet", d.RatingText);
        var line = Assert.Single(d.Services);                     // the inactive service is left out
        Assert.Equal(new ProviderServiceLine("Pipe Repair", "Plumbing", "₱500", "1 hr"), line);
        Assert.Contains(d.Badges, b => b.Text == "Approved");
        Assert.Matches(@"^[A-Z][a-z]{2} \d{1,2}, \d{4}$", d.MemberSince);
    }

    [Fact]
    public async Task Details_OfferOnlyTheActionsThatFitTheCurrentStatus()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var active = await AddProviderAsync(db);
        var suspended = await AddProviderAsync(db, AccountStatuses.Suspended);
        var deactivated = await AddProviderAsync(db, AccountStatuses.Deactivated);

        async Task<string[]> Actions(Provider p) =>
            (await svc.GetDetailsAsync(admin.UserId, p.ProviderId)).Details!.Actions.Select(a => a.Action).ToArray();

        Assert.Equal(new[] { "Suspend", "Deactivate" }, await Actions(active));
        Assert.Equal(new[] { "Reactivate", "Deactivate" }, await Actions(suspended));
        Assert.Equal(new[] { "Restore" }, await Actions(deactivated));
        Assert.Contains((await svc.GetDetailsAsync(admin.UserId, suspended.ProviderId)).Details!.Badges, b => b.Text == "Suspended");
    }

    [Fact]
    public async Task Details_AreOnlyForActiveAdministrators()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        Assert.Equal(ProviderDetailsOutcome.NotAllowed, (await svc.GetDetailsAsync(suspendedAdmin.UserId, p.ProviderId)).Outcome);
        Assert.Equal(ProviderDetailsOutcome.NotAllowed, (await svc.GetDetailsAsync(customer.UserId, p.ProviderId)).Outcome);
        Assert.Equal(ProviderDetailsOutcome.NotAllowed, (await svc.GetDetailsAsync(p.UserId, p.ProviderId)).Outcome);
        Assert.Equal(ProviderDetailsOutcome.NotAllowed, (await svc.GetDetailsAsync(9999, p.ProviderId)).Outcome);
    }

    [Fact]
    public async Task Details_UnknownProvider_IsNotFound()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var lookup = await svc.GetDetailsAsync(admin.UserId, 9999);

        Assert.Equal(ProviderDetailsOutcome.NotFound, lookup.Outcome);
        Assert.Null(lookup.Details);
    }

    // ------------------------------------------------------------------ changing the account

    [Fact]
    public async Task Suspend_ChangesTheStatus_AndWritesTheAdminLog()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        var result = await svc.ChangeStatusAsync(admin.UserId, p.ProviderId, AccountAction.Suspend, "  Too many complaints  ");

        Assert.Equal(ChangeStatusOutcome.Success, result.Outcome);
        Assert.Equal("Vincent Valencia was suspended.", result.Message);
        Assert.Equal(AccountStatuses.Suspended, (await db.Users.FindAsync(p.UserId))!.AccountStatus);

        var log = await db.AdminActions.SingleAsync();
        Assert.Equal(admin.UserId, log.AdminUserId);
        Assert.Equal("Account Suspended", log.ActionType);
        Assert.Equal("USER", log.TargetType);
        Assert.Equal(p.UserId, log.TargetId);
        Assert.Equal("Too many complaints", log.Reason);
    }

    [Fact]
    public async Task Deactivate_Reactivate_AndRestore_Work()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        async Task<string> Status() => (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == p.UserId)).AccountStatus;

        Assert.Equal(ChangeStatusOutcome.Success, (await svc.ChangeStatusAsync(admin.UserId, p.ProviderId, AccountAction.Suspend, null)).Outcome);
        Assert.Equal(ChangeStatusOutcome.Success, (await svc.ChangeStatusAsync(admin.UserId, p.ProviderId, AccountAction.Reactivate, null)).Outcome);
        Assert.Equal(AccountStatuses.Active, await Status());
        Assert.Equal(ChangeStatusOutcome.Success, (await svc.ChangeStatusAsync(admin.UserId, p.ProviderId, AccountAction.Deactivate, null)).Outcome);
        Assert.Equal(AccountStatuses.Deactivated, await Status());
        Assert.Equal(ChangeStatusOutcome.Success, (await svc.ChangeStatusAsync(admin.UserId, p.ProviderId, AccountAction.Restore, null)).Outcome);
        Assert.Equal(AccountStatuses.Active, await Status());
        Assert.Equal(4, await db.AdminActions.CountAsync());
    }

    [Theory]
    [InlineData(AccountStatuses.Active, AccountAction.Reactivate)]
    [InlineData(AccountStatuses.Active, AccountAction.Restore)]
    [InlineData(AccountStatuses.Suspended, AccountAction.Suspend)]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Suspend)]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Deactivate)]
    public async Task ImpossibleChanges_AreRefused_AndNothingIsSavedOrLogged(string current, AccountAction action)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db, current);

        var result = await svc.ChangeStatusAsync(admin.UserId, p.ProviderId, action, null);

        Assert.Equal(ChangeStatusOutcome.InvalidTransition, result.Outcome);
        Assert.Equal(current, (await db.Users.FindAsync(p.UserId))!.AccountStatus);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanChangeAnAccount()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var other = await AddProviderAsync(db);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        foreach (var actor in new[] { suspendedAdmin.UserId, customer.UserId, other.UserId, 9999 })
        {
            var result = await svc.ChangeStatusAsync(actor, p.ProviderId, AccountAction.Suspend, null);
            Assert.Equal(ChangeStatusOutcome.NotAllowed, result.Outcome);
        }
        Assert.Equal(AccountStatuses.Active, (await db.Users.FindAsync(p.UserId))!.AccountStatus);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task AnUnknownProvider_IsNotFound_AndACustomerCanNeverBeTargeted()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // a customer's id used as a provider id finds no provider, so the customer account is untouched
        var r1 = await svc.ChangeStatusAsync(admin.UserId, customer.CustomerId, AccountAction.Suspend, null);
        var r2 = await svc.ChangeStatusAsync(admin.UserId, 9999, AccountAction.Suspend, null);

        Assert.Equal(ChangeStatusOutcome.NotFound, r1.Outcome);
        Assert.Equal(ChangeStatusOutcome.NotFound, r2.Outcome);
        Assert.Equal(AccountStatuses.Active, (await db.Users.FindAsync(customer.UserId))!.AccountStatus);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task ATooLongReason_IsRefused()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        var result = await svc.ChangeStatusAsync(admin.UserId, p.ProviderId, AccountAction.Suspend, new string('x', 256));

        Assert.Equal(ChangeStatusOutcome.ReasonTooLong, result.Outcome);
        Assert.Equal(AccountStatuses.Active, (await db.Users.FindAsync(p.UserId))!.AccountStatus);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task ASuspendedProvider_MovesToTheSuspendedTab()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);
        await svc.ChangeStatusAsync(admin.UserId, p.ProviderId, AccountAction.Suspend, null);

        var page = await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery { Status = "Suspended" });

        Assert.Equal(1, page!.CountSuspended);
        Assert.Equal(0, page.CountApproved);
        Assert.Single(page.Cards);
    }
}
