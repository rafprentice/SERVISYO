using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class AccountTransitionsTests
{
    [Theory]
    [InlineData(AccountStatuses.Active, AccountAction.Suspend, AccountStatuses.Suspended)]
    [InlineData(AccountStatuses.Active, AccountAction.Deactivate, AccountStatuses.Deactivated)]
    [InlineData(AccountStatuses.Suspended, AccountAction.Reactivate, AccountStatuses.Active)]
    [InlineData(AccountStatuses.Suspended, AccountAction.Deactivate, AccountStatuses.Deactivated)]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Restore, AccountStatuses.Active)]
    public void AllowedChanges_GiveTheNewStatus(string from, AccountAction action, string expected)
        => Assert.Equal(expected, AccountTransitions.TargetStatus(from, action));

    [Theory]
    [InlineData(AccountStatuses.Active, AccountAction.Reactivate)]
    [InlineData(AccountStatuses.Active, AccountAction.Restore)]
    [InlineData(AccountStatuses.Suspended, AccountAction.Suspend)]
    [InlineData(AccountStatuses.Suspended, AccountAction.Restore)]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Suspend)]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Deactivate)]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Reactivate)]
    [InlineData("Nonsense", AccountAction.Suspend)]
    public void NotAllowedChanges_GiveNull(string from, AccountAction action)
        => Assert.Null(AccountTransitions.TargetStatus(from, action));

    [Fact]
    public void Buttons_MatchTheDesign()
    {
        Assert.Equal(new[] { AccountAction.Suspend, AccountAction.Deactivate },
            AccountTransitions.AvailableFor(AccountStatuses.Active));
        Assert.Equal(new[] { AccountAction.Reactivate, AccountAction.Deactivate },
            AccountTransitions.AvailableFor(AccountStatuses.Suspended));
        Assert.Equal(new[] { AccountAction.Restore },
            AccountTransitions.AvailableFor(AccountStatuses.Deactivated));
    }
}

public class UserManagementServiceTests
{
    private static (UserManagementService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new UserManagementService(db, NullLogger<UserManagementService>.Instance), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active, string? email = null)
        => new()
        {
            FirstName = first, LastName = last, PasswordHash = "x", Role = role, AccountStatus = status,
            Email = email ?? $"{first}.{last}.{Guid.NewGuid():N}@test.com".ToLowerInvariant()
        };

    private static Customer NewCustomer(string first, string last, string status = AccountStatuses.Active,
                                        string? city = null, string? email = null)
        => new() { City = city, User = NewUser(first, last, Roles.Customer, status, email) };

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator, status);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    // ---------------------------------------------------------------- list
    [Fact]
    public async Task OnlyAnActiveAdministrator_CanSeeTheList()
    {
        var (svc, db) = Create();
        var customer = NewCustomer("Ana", "Reyes");
        db.Customers.Add(customer);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        Assert.Null(await svc.GetCustomersAsync(customer.UserId, new UserListQuery()));
        Assert.Null(await svc.GetCustomersAsync(suspendedAdmin.UserId, new UserListQuery()));
        Assert.Null(await svc.GetCustomersAsync(99999, new UserListQuery()));
    }

    [Fact]
    public async Task List_ShowsOnlyCustomers_WithCountsPerStatus()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.Customers.AddRange(
            NewCustomer("Ana", "Reyes"), NewCustomer("Brent", "Lim"),
            NewCustomer("Carla", "Tan", AccountStatuses.Suspended),
            NewCustomer("Dennis", "Go"), NewCustomer("Elena", "Cruz"),
            NewCustomer("Rey", "Aquino", AccountStatuses.Deactivated));
        db.Providers.Add(new Provider { BusinessName = "Not a customer", User = NewUser("Marco", "Reyes", Roles.ServiceProvider) });
        await db.SaveChangesAsync();

        var vm = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery()))!;

        Assert.Equal(6, vm.CountAll);                 // providers and the administrator are not listed
        Assert.Equal(4, vm.CountActive);
        Assert.Equal(1, vm.CountSuspended);
        Assert.Equal(1, vm.CountDeactivated);
        Assert.Equal(6, vm.TotalMatching);
        Assert.Equal(new[] { "Ana Reyes", "Brent Lim", "Carla Tan", "Dennis Go", "Elena Cruz", "Rey Aquino" },
            vm.Rows.Select(r => r.Name));
        Assert.Equal("A", vm.Shell.Initials);
        Assert.Equal("Admin", vm.Shell.Role);
    }

    [Theory]
    [InlineData("Active", 4)]
    [InlineData("Suspended", 1)]
    [InlineData("Deactivated", 1)]
    [InlineData("All", 6)]
    [InlineData("suspended", 1)]       // case does not matter
    [InlineData("nonsense", 6)]        // unknown value falls back to All
    [InlineData(null, 6)]
    public async Task StatusTabs_FilterTheRows(string? tab, int expectedRows)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.Customers.AddRange(
            NewCustomer("Ana", "Reyes"), NewCustomer("Brent", "Lim"),
            NewCustomer("Carla", "Tan", AccountStatuses.Suspended),
            NewCustomer("Dennis", "Go"), NewCustomer("Elena", "Cruz"),
            NewCustomer("Rey", "Aquino", AccountStatuses.Deactivated));
        await db.SaveChangesAsync();

        var vm = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Status = tab }))!;

        Assert.Equal(expectedRows, vm.Rows.Count);
        Assert.Equal(6, vm.CountAll);      // tab counts do not change with the filter
    }

    [Fact]
    public async Task Search_MatchesNameOrEmail_IgnoringCase_AndCombinesWithTheTab()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.Customers.AddRange(
            NewCustomer("Ana", "Reyes", email: "ana.reyes@email.com"),
            NewCustomer("Brent", "Lim", email: "brent.lim@email.com"),
            NewCustomer("Carla", "Tan", AccountStatuses.Suspended, email: "carl.tan@email.com"));
        await db.SaveChangesAsync();

        var byName = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Q = "  ANA " }))!;
        Assert.Equal(new[] { "Ana Reyes" }, byName.Rows.Select(r => r.Name));

        var byFullName = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Q = "brent lim" }))!;
        Assert.Single(byFullName.Rows);

        var byEmail = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Q = "carl.tan@" }))!;
        Assert.Equal("Carla Tan", Assert.Single(byEmail.Rows).Name);

        var tabAndSearch = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Q = "a", Status = "Active" }))!;
        Assert.DoesNotContain(tabAndSearch.Rows, r => r.Name == "Carla Tan");

        var none = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Q = "zzz" }))!;
        Assert.Empty(none.Rows);
        Assert.Equal(0, none.TotalMatching);
        Assert.Equal(3, none.CountAll);
    }

    [Fact]
    public async Task Rows_OfferTheRightButtons_AndShowCity()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.Customers.AddRange(
            NewCustomer("Ana", "Reyes", city: "Mandaue City"),
            NewCustomer("Carla", "Tan", AccountStatuses.Suspended),
            NewCustomer("Rey", "Aquino", AccountStatuses.Deactivated));
        await db.SaveChangesAsync();

        var vm = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery()))!;

        var ana = vm.Rows.Single(r => r.Name == "Ana Reyes");
        Assert.Equal("Mandaue City", ana.City);
        Assert.Equal(new[] { "Suspend", "Deactivate" }, ana.Actions.Select(a => a.Label));
        Assert.Equal("completed", ana.StatusTone);

        var carla = vm.Rows.Single(r => r.Name == "Carla Tan");
        Assert.Null(carla.City);
        Assert.Equal(new[] { "Reactivate", "Deactivate" }, carla.Actions.Select(a => a.Label));

        var rey = vm.Rows.Single(r => r.Name == "Rey Aquino");
        Assert.Equal(new[] { "Restore" }, rey.Actions.Select(a => a.Label));
        Assert.Equal("danger", rey.StatusTone);
    }

    [Fact]
    public async Task Paging_ShowsTwentyPerPage_AndClampsThePageNumber()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        for (var i = 0; i < 25; i++)
            db.Customers.Add(NewCustomer($"User{i:00}", "Test"));
        await db.SaveChangesAsync();

        var first = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Page = 1 }))!;
        var second = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Page = 2 }))!;
        var tooFar = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Page = 99 }))!;
        var negative = (await svc.GetCustomersAsync(admin.UserId, new UserListQuery { Page = -4 }))!;

        Assert.Equal(20, first.Rows.Count);
        Assert.Equal(5, second.Rows.Count);
        Assert.Equal(2, first.PageCount);
        Assert.Equal(25, first.TotalMatching);
        Assert.Equal(2, tooFar.Page);
        Assert.Equal(1, negative.Page);
    }

    // -------------------------------------------------------- change status
    [Fact]
    public async Task Suspend_ChangesStatus_AndWritesTheAdminLog()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var ana = NewCustomer("Ana", "Reyes");
        db.Customers.Add(ana);
        await db.SaveChangesAsync();

        var result = await svc.ChangeStatusAsync(admin.UserId, ana.UserId, AccountAction.Suspend, "  Repeated no-shows  ");

        Assert.Equal(ChangeStatusOutcome.Success, result.Outcome);
        Assert.Equal("Ana Reyes was suspended.", result.Message);

        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == ana.UserId);
        Assert.Equal(AccountStatuses.Suspended, user.AccountStatus);

        var log = await db.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(admin.UserId, log.AdminUserId);
        Assert.Equal("Account Suspended", log.ActionType);
        Assert.Equal("USER", log.TargetType);
        Assert.Equal(ana.UserId, log.TargetId);
        Assert.Equal("Repeated no-shows", log.Reason);
    }

    [Theory]
    [InlineData(AccountStatuses.Suspended, AccountAction.Reactivate, AccountStatuses.Active, "Account Active", "reactivated")]
    [InlineData(AccountStatuses.Suspended, AccountAction.Deactivate, AccountStatuses.Deactivated, "Account Deactivated", "deactivated")]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Restore, AccountStatuses.Active, "Account Active", "restored")]
    [InlineData(AccountStatuses.Active, AccountAction.Deactivate, AccountStatuses.Deactivated, "Account Deactivated", "deactivated")]
    public async Task OtherAllowedChanges_Work(string from, AccountAction action, string expected, string logType, string word)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var c = NewCustomer("Carla", "Tan", from);
        db.Customers.Add(c);
        await db.SaveChangesAsync();

        var result = await svc.ChangeStatusAsync(admin.UserId, c.UserId, action, null);

        Assert.Equal(ChangeStatusOutcome.Success, result.Outcome);
        Assert.Equal($"Carla Tan was {word}.", result.Message);
        Assert.Equal(expected, (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == c.UserId)).AccountStatus);

        var log = await db.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(logType, log.ActionType);
        Assert.Null(log.Reason);              // blank reason is stored as null
    }

    [Theory]
    [InlineData(AccountStatuses.Active, AccountAction.Restore)]
    [InlineData(AccountStatuses.Active, AccountAction.Reactivate)]
    [InlineData(AccountStatuses.Suspended, AccountAction.Suspend)]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Suspend)]
    [InlineData(AccountStatuses.Deactivated, AccountAction.Deactivate)]
    public async Task InvalidChange_IsRefused_AndNothingIsSaved(string from, AccountAction action)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var c = NewCustomer("Ana", "Reyes", from);
        db.Customers.Add(c);
        await db.SaveChangesAsync();

        var result = await svc.ChangeStatusAsync(admin.UserId, c.UserId, action, null);

        Assert.Equal(ChangeStatusOutcome.InvalidTransition, result.Outcome);
        Assert.Equal(from, (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == c.UserId)).AccountStatus);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanChangeStatus()
    {
        var (svc, db) = Create();
        var customerActor = NewCustomer("Evil", "Customer");
        var provider = new Provider { BusinessName = "P", User = NewUser("Pro", "Vider", Roles.ServiceProvider) };
        var target = NewCustomer("Ana", "Reyes");
        db.Customers.AddRange(customerActor, target);
        db.Providers.Add(provider);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        foreach (var actorId in new[] { customerActor.UserId, provider.UserId, suspendedAdmin.UserId, 99999 })
        {
            var result = await svc.ChangeStatusAsync(actorId, target.UserId, AccountAction.Suspend, null);
            Assert.Equal(ChangeStatusOutcome.NotAllowed, result.Outcome);
        }

        Assert.Equal(AccountStatuses.Active, (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == target.UserId)).AccountStatus);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task ProvidersAndAdministrators_CannotBeChangedFromThisPage()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var otherAdmin = await AddAdminAsync(db);
        var provider = new Provider { BusinessName = "P", User = NewUser("Pro", "Vider", Roles.ServiceProvider) };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();

        foreach (var targetId in new[] { provider.UserId, otherAdmin.UserId, admin.UserId, 99999 })
        {
            var result = await svc.ChangeStatusAsync(admin.UserId, targetId, AccountAction.Suspend, null);
            Assert.Equal(ChangeStatusOutcome.NotFound, result.Outcome);
        }

        Assert.All(await db.Users.AsNoTracking().ToListAsync(), u => Assert.Equal(AccountStatuses.Active, u.AccountStatus));
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task TooLongReason_IsRefused()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var ana = NewCustomer("Ana", "Reyes");
        db.Customers.Add(ana);
        await db.SaveChangesAsync();

        var result = await svc.ChangeStatusAsync(admin.UserId, ana.UserId, AccountAction.Suspend, new string('x', 256));

        Assert.Equal(ChangeStatusOutcome.ReasonTooLong, result.Outcome);
        Assert.Equal(AccountStatuses.Active, (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == ana.UserId)).AccountStatus);
        Assert.Empty(db.AdminActions);
    }
}
