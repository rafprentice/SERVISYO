using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ApplicantNumberTests
{
    [Fact]
    public void IsYearAndPaddedProviderId()
        => Assert.Equal("PRV-2026-001", ApplicantNumber.For(1, new DateTime(2026, 10, 14, 2, 0, 0)));

    [Fact]
    public void UsesTheManilaYear_NotTheUtcYear()
        // 31 Dec 2025, 17:00 UTC is already 1 Jan 2026, 01:00 in Manila
        => Assert.Equal("PRV-2026-007", ApplicantNumber.For(7, new DateTime(2025, 12, 31, 17, 0, 0)));

    [Fact]
    public void BigIdsAreNotCutOff()
        => Assert.Equal("PRV-2026-1234", ApplicantNumber.For(1234, new DateTime(2026, 3, 1, 0, 0, 0)));
}

public class ApplicationReturnTests
{
    [Theory]
    [InlineData("providers", "providers")]
    [InlineData(" Providers ", "providers")]
    [InlineData("dashboard", "dashboard")]
    [InlineData("https://evil.example/steal", "dashboard")]
    [InlineData("", "dashboard")]
    [InlineData(null, "dashboard")]
    public void OnlyTheTwoKnownPlacesAreAccepted(string? input, string expected)
        => Assert.Equal(expected, ApplicationReturn.Normalize(input));
}

public class ApplicationViewModelTests
{
    [Fact]
    public void OnlyPendingApplicationsCanBeDecided()
    {
        Assert.True(new ProviderApplicationViewModel { ApprovalStatus = ApprovalStatuses.Pending }.CanDecide);
        Assert.False(new ProviderApplicationViewModel { ApprovalStatus = ApprovalStatuses.Approved }.CanDecide);
        Assert.False(new ProviderApplicationViewModel { ApprovalStatus = ApprovalStatuses.Rejected }.CanDecide);
    }

    [Fact]
    public void FeeIsShownInPesos_OrNotAtAll()
    {
        Assert.Equal("₱1,500.00", new ProviderApplicationViewModel { BaseFee = 1500m }.BaseFeeText);
        Assert.Equal(string.Empty, new ProviderApplicationViewModel { BaseFee = null }.BaseFeeText);
    }
}

public class AdminAttentionLinkTests
{
    [Fact]
    public void WaitingApplications_OpenTheOldestOne()
    {
        var row = Assert.Single(AdminAttention.Build(2, 0, 0, 0, firstPendingProviderId: 42));

        Assert.Equal(AdminAttentionAction.ReviewApplication, row.Action);
        Assert.Equal(42, row.TargetId);
    }

    [Fact]
    public void WithoutAnId_TheButtonOpensThePendingList()
    {
        var row = Assert.Single(AdminAttention.Build(2, 0, 0, 0));

        Assert.Equal(AdminAttentionAction.ApplicationList, row.Action);
        Assert.Null(row.TargetId);
    }

    [Fact]
    public void FlaggedReviews_GoToModeration_AndUnbuiltScreensStayDisabled()
    {
        var rows = AdminAttention.Build(0, 1, 1, 1);

        Assert.Equal(AdminAttentionAction.ModerateReviews, rows[0].Action);
        Assert.Equal(AdminAttentionAction.None, rows[1].Action);     // suspended accounts
        Assert.Equal(AdminAttentionAction.None, rows[2].Action);     // reported issues
    }
}

public class ProviderApprovalServiceTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime Now = new(2026, 10, 14, 2, 0, 0);

    private static (ProviderApprovalService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new ProviderApprovalService(db, new FixedClock()), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static Provider NewProvider(string first = "Vincent", string last = "Valencia", string business = "Valencia Plumbing",
        string approval = ApprovalStatuses.Pending, string? city = "Cebu City", string? address = null)
        => new()
        {
            BusinessName = business, City = city, Address = address, ApprovalStatus = approval,
            DateApplied = new DateTime(2026, 10, 12, 3, 0, 0),
            User = NewUser(first, last, Roles.ServiceProvider)
        };

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator, status);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, Provider? provider = null)
    {
        provider ??= NewProvider();
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    // ------------------------------------------------------------- the dialog's content

    [Fact]
    public async Task Application_ShowsTheApplicantsDetails()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = NewProvider(address: "  12 Colon St.  ");
        p.Description = "  Fast, clean plumbing.  ";
        p.BaseFee = 1500m;
        p.User.PhoneNumber = "09171234567";
        p.ProviderCategories.Add(new ProviderCategory { Category = new ServiceCategory { CategoryName = "Plumbing" } });
        p.ProviderCategories.Add(new ProviderCategory { Category = new ServiceCategory { CategoryName = "Repair" } });
        await AddProviderAsync(db, p);

        var lookup = await svc.GetApplicationAsync(admin.UserId, p.ProviderId);

        Assert.Equal(ApprovalOutcome.Success, lookup.Outcome);
        var vm = lookup.Application!;
        Assert.Equal("PRV-2026-" + p.ProviderId.ToString("000"), vm.ApplicantNumber);
        Assert.Equal("Vincent Valencia", vm.Name);
        Assert.Equal("VV", vm.Initials);
        Assert.Equal("Valencia Plumbing", vm.BusinessName);
        Assert.Equal("12 Colon St., Cebu City", vm.Location);
        Assert.Equal(new[] { "Plumbing", "Repair" }, vm.Services);
        Assert.Equal("Fast, clean plumbing.", vm.Description);
        Assert.Equal(1500m, vm.BaseFee!.Value);
        Assert.Equal(p.User.Email, vm.Email);
        Assert.Equal("09171234567", vm.Phone);
        Assert.Equal("Oct 12, 2026", vm.AppliedText);
        Assert.True(vm.CanDecide);
        Assert.Equal("Admin", vm.Shell.FullName);
    }

    [Fact]
    public async Task Application_WithNoCategories_ListsActiveServiceNames_AndMissingFieldsAreNull()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = NewProvider(city: null);
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        p.Services.Add(new Service { Category = cat, ServiceName = "Pipe Repair", Price = 500m });
        p.Services.Add(new Service { Category = cat, ServiceName = "Old Hidden Service", Price = 500m, IsActive = false });
        await AddProviderAsync(db, p);

        var vm = (await svc.GetApplicationAsync(admin.UserId, p.ProviderId)).Application!;

        Assert.Equal(new[] { "Pipe Repair" }, vm.Services);
        Assert.Null(vm.Location);
        Assert.Null(vm.Description);
        Assert.Null(vm.Phone);
        Assert.Null(vm.BaseFee);
    }

    [Fact]
    public async Task Application_OfAnAlreadyDecidedProvider_CanBeOpenedButNotDecided()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db, NewProvider(approval: ApprovalStatuses.Approved));

        var vm = (await svc.GetApplicationAsync(admin.UserId, p.ProviderId)).Application!;

        Assert.False(vm.CanDecide);
        Assert.Equal(ApprovalStatuses.Approved, vm.ApprovalStatus);
    }

    [Fact]
    public async Task Application_UnknownProvider_IsNotFound()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var lookup = await svc.GetApplicationAsync(admin.UserId, 99999);

        Assert.Equal(ApprovalOutcome.NotFound, lookup.Outcome);
        Assert.Null(lookup.Application);
    }

    [Fact]
    public async Task Application_CanOnlyBeOpenedByAnActiveAdministrator()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Customers.Add(customer);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        foreach (var userId in new[] { p.UserId, customer.UserId, suspendedAdmin.UserId, 99999 })
        {
            var lookup = await svc.GetApplicationAsync(userId, p.ProviderId);
            Assert.Equal(ApprovalOutcome.NotAllowed, lookup.Outcome);
            Assert.Null(lookup.Application);
        }
    }

    // ----------------------------------------------------------------- approving

    [Fact]
    public async Task Approve_ActivatesTheProvider_LogsIt_AndNotifiesThem()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        var result = await svc.DecideAsync(admin.UserId, p.ProviderId, ApprovalDecision.Approve, "  ID checked  ");

        Assert.True(result.Succeeded);
        Assert.Equal("Vincent Valencia has been approved as an active service provider.", result.Message);

        var saved = await db.Providers.AsNoTracking().SingleAsync(x => x.ProviderId == p.ProviderId);
        Assert.Equal(ApprovalStatuses.Approved, saved.ApprovalStatus);
        Assert.Equal(Now, saved.DateApproved!.Value);

        var log = await db.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(admin.UserId, log.AdminUserId);
        Assert.Equal("Approved", log.ActionType);
        Assert.Equal("SERVICE_PROVIDER", log.TargetType);
        Assert.Equal(p.ProviderId, log.TargetId);
        Assert.Equal("ID checked", log.Reason);

        var note = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(p.UserId, note.UserId);
        Assert.Equal("Your provider application was Approved.", note.Message);
        Assert.False(note.IsRead);
    }

    [Fact]
    public async Task Approve_RemovesTheApplicationFromTheDashboardQueue_AndRaisesTheApprovedCount()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);
        var dashboard = new AdminDashboardService(db);
        var providers = new ProviderManagementService(db);

        var before = (await dashboard.GetAsync(admin.UserId))!;
        Assert.Equal(1, before.PendingApprovals);
        var banner = Assert.Single(before.Attention);
        Assert.Equal(AdminAttentionAction.ReviewApplication, banner.Action);
        Assert.Equal(p.ProviderId, banner.TargetId);
        Assert.Single(before.PendingApplications);
        Assert.Equal(0, (await providers.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!.CountApproved);

        await svc.DecideAsync(admin.UserId, p.ProviderId, ApprovalDecision.Approve, null);

        var after = (await dashboard.GetAsync(admin.UserId))!;
        Assert.Equal(0, after.PendingApprovals);
        Assert.Empty(after.Attention);
        Assert.Empty(after.PendingApplications);
        var page = (await providers.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!;
        Assert.Equal(1, page.CountApproved);
        Assert.Equal(0, page.CountPending);
    }

    [Fact]
    public async Task WithTwoWaiting_TheBannerAlwaysPointsAtTheOldest()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var older = NewProvider("Felix", "Torres", "Torres Plumbing");
        older.DateApplied = new DateTime(2026, 10, 10);
        var newer = NewProvider("Maricel", "Bautista", "MB Cleaning");
        newer.DateApplied = new DateTime(2026, 10, 13);
        db.Providers.AddRange(newer, older);          // saved newest-first on purpose
        await db.SaveChangesAsync();
        var dashboard = new AdminDashboardService(db);

        var first = (await dashboard.GetAsync(admin.UserId))!;
        Assert.Equal(older.ProviderId, Assert.Single(first.Attention).TargetId);

        await svc.DecideAsync(admin.UserId, older.ProviderId, ApprovalDecision.Approve, null);

        var second = (await dashboard.GetAsync(admin.UserId))!;
        Assert.Equal(1, second.PendingApprovals);
        Assert.Equal(newer.ProviderId, Assert.Single(second.Attention).TargetId);
    }

    // ----------------------------------------------------------------- rejecting

    [Fact]
    public async Task Reject_MarksItRejected_WithoutAnApprovalDate()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        var result = await svc.DecideAsync(admin.UserId, p.ProviderId, ApprovalDecision.Reject, "  ");

        Assert.True(result.Succeeded);
        Assert.Equal("Vincent Valencia's application has been rejected.", result.Message);

        var saved = await db.Providers.AsNoTracking().SingleAsync(x => x.ProviderId == p.ProviderId);
        Assert.Equal(ApprovalStatuses.Rejected, saved.ApprovalStatus);
        Assert.Null(saved.DateApproved);

        var log = await db.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal("Rejected", log.ActionType);
        Assert.Equal("SERVICE_PROVIDER", log.TargetType);
        Assert.Null(log.Reason);                      // a blank note is stored as "no note"

        var note = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal("Your provider application was Rejected.", note.Message);
    }

    // ---------------------------------------------------------------- the rules

    [Theory]
    [InlineData(ApprovalStatuses.Approved)]
    [InlineData(ApprovalStatuses.Rejected)]
    public async Task OnlyPendingApplications_CanBeDecided(string current)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db, NewProvider(approval: current));

        foreach (var decision in new[] { ApprovalDecision.Approve, ApprovalDecision.Reject })
        {
            var result = await svc.DecideAsync(admin.UserId, p.ProviderId, decision, null);

            Assert.Equal(ApprovalOutcome.NotPending, result.Outcome);
            Assert.Contains("already been decided", result.Message);
        }

        var saved = await db.Providers.AsNoTracking().SingleAsync(x => x.ProviderId == p.ProviderId);
        Assert.Equal(current, saved.ApprovalStatus);
        Assert.Empty(db.AdminActions);                // no log entry, no second notification
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task DecidingTwice_OnlyWorksTheFirstTime()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        var first = await svc.DecideAsync(admin.UserId, p.ProviderId, ApprovalDecision.Approve, null);
        var second = await svc.DecideAsync(admin.UserId, p.ProviderId, ApprovalDecision.Reject, null);

        Assert.True(first.Succeeded);
        Assert.Equal(ApprovalOutcome.NotPending, second.Outcome);
        Assert.Equal(ApprovalStatuses.Approved, (await db.Providers.AsNoTracking().SingleAsync()).ApprovalStatus);
        Assert.Single(db.AdminActions);
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task UnknownProvider_IsNotFound_AndNothingIsLogged()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var result = await svc.DecideAsync(admin.UserId, 99999, ApprovalDecision.Approve, null);

        Assert.Equal(ApprovalOutcome.NotFound, result.Outcome);
        Assert.Empty(db.AdminActions);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanDecide()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, NewProvider("Other", "Provider", "Other Co", ApprovalStatuses.Approved));
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Customers.Add(customer);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        // a provider can't approve themselves; customers and suspended admins can't approve anyone
        foreach (var userId in new[] { p.UserId, other.UserId, customer.UserId, suspendedAdmin.UserId, 99999 })
        {
            var result = await svc.DecideAsync(userId, p.ProviderId, ApprovalDecision.Approve, null);
            Assert.Equal(ApprovalOutcome.NotAllowed, result.Outcome);
        }

        var saved = await db.Providers.AsNoTracking().SingleAsync(x => x.ProviderId == p.ProviderId);
        Assert.Equal(ApprovalStatuses.Pending, saved.ApprovalStatus);
        Assert.Empty(db.AdminActions);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task ATooLongNote_IsRefused_AndNothingChanges()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        var result = await svc.DecideAsync(admin.UserId, p.ProviderId, ApprovalDecision.Approve, new string('x', ProviderApprovalService.MaxReasonLength + 1));

        Assert.Equal(ApprovalOutcome.Invalid, result.Outcome);
        Assert.Equal(ApprovalStatuses.Pending, (await db.Providers.AsNoTracking().SingleAsync(x => x.ProviderId == p.ProviderId)).ApprovalStatus);
        Assert.Empty(db.AdminActions);
    }

    [Fact]
    public async Task ANoteOfExactlyTheMaximumLength_IsAccepted()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        var result = await svc.DecideAsync(admin.UserId, p.ProviderId, ApprovalDecision.Reject, new string('x', ProviderApprovalService.MaxReasonLength));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AnUnknownDecision_IsRefused()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var p = await AddProviderAsync(db);

        var result = await svc.DecideAsync(admin.UserId, p.ProviderId, (ApprovalDecision)99, null);

        Assert.Equal(ApprovalOutcome.Invalid, result.Outcome);
        Assert.Empty(db.AdminActions);
    }
}
