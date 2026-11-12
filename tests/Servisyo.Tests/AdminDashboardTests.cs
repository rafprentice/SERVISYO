using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class PluralTests
{
    [Theory]
    [InlineData(1, "customer", "1 customer")]
    [InlineData(0, "customer", "0 customers")]
    [InlineData(6, "customer", "6 customers")]
    public void Of_UsesSingularOnlyForOne(int n, string word, string expected)
        => Assert.Equal(expected, Plural.Of(n, word));
}

public class AdminBookingDisplayTests
{
    [Theory]
    [InlineData(BookingStatuses.Pending, "Pending", "pending")]
    [InlineData(BookingStatuses.Accepted, "Accepted", "accepted")]
    [InlineData(BookingStatuses.InProgress, "In Progress", "progress")]
    [InlineData(BookingStatuses.Completed, "Completed", "completed")]
    [InlineData(BookingStatuses.Rejected, "Rejected", "danger")]
    [InlineData(BookingStatuses.Cancelled, "Cancelled", "muted")]
    public void For_MapsStatusToBadge(string status, string badge, string tone)
    {
        var d = AdminBookingDisplay.For(status);
        Assert.Equal(badge, d.Badge);
        Assert.Equal(tone, d.Tone);
    }
}

public class AdminAttentionTests
{
    private static List<AdminAttentionItem> Build(int dayOffs = 0, int nameChanges = 0, int applications = 0, int suspendedCustomers = 0,
        int suspendedProviders = 0, int flagged = 0, int bookings = 0) =>
        AdminAttention.Build(new AdminAttentionCounts
        {
            DayOffRequests = dayOffs, NameChangeRequests = nameChanges, PendingApplications = applications,
            SuspendedCustomers = suspendedCustomers, SuspendedProviders = suspendedProviders, FlaggedReviews = flagged,
            BookingsNeedingAttention = bookings
        });

    [Fact]
    public void NothingWaiting_GivesNoRows_AndTheEmptyTextIsTheRequestedOne()
    {
        Assert.Empty(Build());
        Assert.Equal("All caught up! No pending requests requiring admin attention.", AdminAttention.AllCaughtUp);
    }

    [Fact]
    public void OneOfEach_UsesSingularWording_InFixedOrder()
    {
        var rows = Build(1, 1, 1, 1, 0, 1, 1);

        Assert.Equal(6, rows.Count);
        Assert.Equal("1 provider day-off request awaiting approval", rows[0].Text);
        Assert.Equal("Review Day Offs", rows[0].ActionLabel);
        Assert.Equal("1 name change request pending review", rows[1].Text);
        Assert.Equal("Review Requests", rows[1].ActionLabel);
        Assert.Equal("1 new service provider application pending", rows[2].Text);
        Assert.Equal("Review Providers", rows[2].ActionLabel);
        Assert.Equal("1 suspended account requiring review", rows[3].Text);
        Assert.Equal("View Accounts", rows[3].ActionLabel);
        Assert.Equal("1 flagged review needs moderation", rows[4].Text);
        Assert.Equal("1 booking needs attention (low rating or date passed)", rows[5].Text);
        Assert.Equal("View Booking", rows[5].ActionLabel);
        Assert.Equal(AdminAttentionAction.BookingList, rows[5].Action);
    }

    [Fact]
    public void Many_UsesPluralWording()
    {
        var rows = Build(3, 2, 2, 2, 0, 3, 4);

        Assert.Equal("3 provider day-off requests awaiting approval", rows[0].Text);
        Assert.Equal("2 name change requests pending review", rows[1].Text);
        Assert.Equal("2 new service provider applications pending", rows[2].Text);
        Assert.Equal("2 suspended accounts requiring review", rows[3].Text);
        Assert.Equal("3 flagged reviews need moderation", rows[4].Text);
        Assert.Equal("4 bookings need attention (low ratings or dates passed)", rows[5].Text);
        Assert.Equal("View Bookings", rows[5].ActionLabel);
    }

    [Fact]
    public void OnlyRowsWithWork_AreShown()
    {
        var row = Assert.Single(Build(flagged: 2));
        Assert.Equal("Moderate Reviews", row.ActionLabel);
    }

    [Fact]
    public void RequestsAreAmber_AccountsAndApplicantsAreRed()
    {
        var rows = Build(1, 1, 1, 1);
        Assert.Equal(new[] { "warning", "warning", "danger", "danger" }, rows.Select(r => r.Tone));
        Assert.Equal(new[] { "day-off", "name-change", "application", "suspended" }, rows.Select(r => r.Key));
    }

    [Fact]
    public void EveryButton_GoesSomewhere()
    {
        var rows = Build(1, 1, 1, 1, 1, 1, 1);
        Assert.DoesNotContain(rows, r => r.Action == AdminAttentionAction.None);
    }

    [Fact]
    public void DayOffs_OpenTheOldestRequestsProvider_NameChanges_OpenTheList()
    {
        var rows = AdminAttention.Build(new AdminAttentionCounts { DayOffRequests = 2, FirstDayOffProviderId = 7, NameChangeRequests = 1 });

        Assert.Equal(AdminAttentionAction.ReviewDayOffs, rows[0].Action);
        Assert.Equal(7, rows[0].TargetId);
        Assert.Equal(AdminAttentionAction.NameChangeList, rows[1].Action);
    }

    [Fact]
    public void SuspendedAccounts_GoWhereTheyAreManaged()
    {
        // only customers: Admin > Users, Suspended tab
        var customers = Assert.Single(Build(suspendedCustomers: 2));
        Assert.Equal(AdminAttentionAction.SuspendedCustomers, customers.Action);
        Assert.Null(customers.SecondaryLabel);

        // only providers: Admin > Service Providers, Suspended tab
        var providers = Assert.Single(Build(suspendedProviders: 1));
        Assert.Equal(AdminAttentionAction.SuspendedProviders, providers.Action);
        Assert.Equal("1 suspended account requiring review", providers.Text);

        // both: the customers first, plus a link to the providers
        var both = Assert.Single(Build(suspendedCustomers: 2, suspendedProviders: 1));
        Assert.Equal("3 suspended accounts requiring review (2 customers, 1 provider)", both.Text);
        Assert.Equal(AdminAttentionAction.SuspendedCustomers, both.Action);
        Assert.Equal(AdminAttentionAction.SuspendedProviders, both.SecondaryAction);
        Assert.Equal("Suspended providers", both.SecondaryLabel);
    }
}

public class AdminDashboardServiceTests
{
    private static (AdminDashboardService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new AdminDashboardService(db), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static Customer NewCustomer(string first = "Ana", string last = "Reyes", string status = AccountStatuses.Active)
        => new() { User = NewUser(first, last, Roles.Customer, status) };

    private static Provider NewProvider(string first = "Marco", string last = "Reyes",
        string business = "Marco's Plumbing Works", string approval = ApprovalStatuses.Approved,
        DateTime? applied = null)
        => new()
        {
            BusinessName = business, ApprovalStatus = approval,
            DateApplied = applied ?? DateTime.UtcNow,
            User = NewUser(first, last, Roles.ServiceProvider)
        };

    private static Booking NewBooking(Customer c, Provider p, string status = BookingStatuses.Pending,
        string serviceName = "Pipe Repair", int createdMinutesAgo = 0)
    {
        var service = new Service
        {
            Provider = p, Category = new ServiceCategory { CategoryName = "Plumbing" },
            ServiceName = serviceName, Price = 850m, IsActive = true
        };
        var b = new Booking
        {
            Customer = c, Provider = p, BookingDate = new DateOnly(2026, 10, 15), BookingTime = new TimeOnly(10, 0),
            TotalDurationMinutes = 60, TotalFee = 850m, Status = status, RowVersion = new byte[] { 1 },
            CreatedAt = DateTime.UtcNow.AddMinutes(-createdMinutesAgo)
        };
        b.Services.Add(new BookingService { Service = service, PriceSnapshot = 850m, DurationMinutesSnapshot = 60 });
        return b;
    }

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator, status);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanSeeTheDashboard()
    {
        var (svc, db) = Create();
        var customer = NewCustomer();
        var provider = NewProvider();
        db.Customers.Add(customer);
        db.Providers.Add(provider);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        Assert.Null(await svc.GetAsync(customer.UserId));
        Assert.Null(await svc.GetAsync(provider.UserId));
        Assert.Null(await svc.GetAsync(suspendedAdmin.UserId));
        Assert.Null(await svc.GetAsync(99999));
    }

    [Fact]
    public async Task EmptyPlatform_ShowsZeroes_AndNothingToAttendTo()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var vm = await svc.GetAsync(admin.UserId);

        Assert.NotNull(vm);
        Assert.Equal(0, vm!.TotalUsers);
        Assert.Equal(0, vm.PendingApprovals);
        Assert.Equal(0, vm.TotalBookings);
        Assert.Equal(0, vm.FlaggedReviews);
        Assert.Empty(vm.Attention);
        Assert.Empty(vm.RecentBookings);
        Assert.Empty(vm.PendingApplications);
    }

    [Fact]
    public async Task Shell_ShowsAdminIdentity()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var vm = (await svc.GetAsync(admin.UserId))!;

        Assert.Equal("Admin", vm.Shell.Role);
        Assert.Equal("Admin", vm.Shell.FullName);
        Assert.Equal("A", vm.Shell.Initials);
        Assert.Equal("System Administrator", vm.Shell.Subtitle);
    }

    [Fact]
    public async Task Numbers_AndAttention_ComeFromTheRealData()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var ana = NewCustomer("Ana", "Reyes");
        var brent = NewCustomer("Brent", "Lim", AccountStatuses.Suspended);
        var marco = NewProvider();
        var felix = NewProvider("Felix", "Torres", "Felix Plumbing", ApprovalStatuses.Pending);
        var maricel = NewProvider("Maricel", "Bautista", "Maricel Cleaning", ApprovalStatuses.Pending);

        var done = NewBooking(ana, marco, BookingStatuses.Completed);
        var other = NewBooking(brent, marco, BookingStatuses.Pending, "Deep Cleaning");
        var third = NewBooking(ana, marco, BookingStatuses.Accepted, "AC Service");
        db.Bookings.AddRange(done, other, third);
        db.Providers.AddRange(felix, maricel);

        // one flagged review and one normal one: only the flagged one counts
        db.Reviews.Add(new Review { Booking = done, Rating = 1, Comment = "x", Status = ReviewStatuses.Flagged });
        db.Reviews.Add(new Review { Booking = third, Rating = 5, Comment = "y", Status = ReviewStatuses.Visible });

        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(admin.UserId))!;

        Assert.Equal(2, vm.Customers);
        Assert.Equal(3, vm.Providers);
        Assert.Equal(5, vm.TotalUsers);          // the administrator is not counted
        Assert.Equal(2, vm.PendingApprovals);
        Assert.Equal(3, vm.TotalBookings);
        Assert.Equal(1, vm.FlaggedReviews);

        Assert.Collection(vm.Attention,
            a => Assert.Equal("2 new service provider applications pending", a.Text),
            a =>
            {
                Assert.Equal("1 suspended account requiring review", a.Text);
                Assert.Equal(AdminAttentionAction.SuspendedCustomers, a.Action);      // Brent is a customer
            },
            a => Assert.Equal("1 flagged review needs moderation", a.Text));
        // database v2.3: "bookings that need attention" comes from usp_GetBookingsNeedingAttention (SQL self-test 33);
        // the in-memory test database has no stored procedures, so that row is not here
    }

    [Fact]
    public async Task RecentBookings_AreNewestFirst_WithTitleAndBothNames()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var ana = NewCustomer("Ana", "Reyes");
        var marco = NewProvider("Marco", "Reyes");

        db.Bookings.Add(NewBooking(ana, marco, BookingStatuses.Pending, "Oldest", createdMinutesAgo: 30));
        db.Bookings.Add(NewBooking(ana, marco, BookingStatuses.Accepted, "Pipe Repair", createdMinutesAgo: 5));
        db.Bookings.Add(NewBooking(ana, marco, BookingStatuses.InProgress, "Middle", createdMinutesAgo: 15));
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(admin.UserId))!;

        Assert.Equal(new[] { "Pipe Repair", "Middle", "Oldest" }, vm.RecentBookings.Select(b => b.Title));
        var first = vm.RecentBookings[0];
        Assert.Equal("Ana Reyes", first.CustomerName);
        Assert.Equal("Marco Reyes", first.ProviderName);
        Assert.Equal("Accepted", first.Display.Badge);
    }

    [Fact]
    public async Task RecentBookings_AreLimitedToFive()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var ana = NewCustomer();
        var marco = NewProvider();
        for (var i = 0; i < 7; i++)
            db.Bookings.Add(NewBooking(ana, marco, BookingStatuses.Pending, $"Job {i}", createdMinutesAgo: i));
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(admin.UserId))!;

        Assert.Equal(7, vm.TotalBookings);
        Assert.Equal(5, vm.RecentBookings.Count);
    }

    [Fact]
    public async Task PendingApplications_OnlyPending_OldestFirst_WithCategory()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var felix = NewProvider("Felix", "Torres", "Felix Plumbing", ApprovalStatuses.Pending, DateTime.UtcNow.AddDays(-2));
        var maricel = NewProvider("Maricel", "Bautista", "Maricel Cleaning", ApprovalStatuses.Pending, DateTime.UtcNow.AddDays(-1));
        var approved = NewProvider("Done", "Already", "Approved Co", ApprovalStatuses.Approved);
        var rejected = NewProvider("Not", "Accepted", "Rejected Co", ApprovalStatuses.Rejected);
        felix.ProviderCategories.Add(new ProviderCategory { Category = new ServiceCategory { CategoryName = "Plumbing" } });
        db.Providers.AddRange(maricel, felix, approved, rejected);
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(admin.UserId))!;

        Assert.Equal(2, vm.PendingApplications.Count);

        var first = vm.PendingApplications[0];
        Assert.Equal("Felix Torres", first.Name);
        Assert.Equal("FT", first.Initials);
        Assert.Equal("Plumbing", first.Details);

        var second = vm.PendingApplications[1];
        Assert.Equal("Maricel Bautista", second.Name);
        Assert.Equal("MB", second.Initials);
        Assert.Equal("Maricel Cleaning", second.Details);   // no category yet -> business name
    }
}

/// The dashboard's "Needs Admin Attention" counts every kind of request that waits (day-off requests v2.7, name change
/// requests v2.8, applications, suspended accounts) from the real data.
public class AdminDashboardRequestCountTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 2, 0, 0, TimeSpan.Zero);     // Thu Nov 12, 10:00 in Cebu

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static User NewUser(string first, string role, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status
    };

    private static Provider Approved(string first, string status = AccountStatuses.Active) =>
        new() { BusinessName = first + " Works", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser(first, Roles.ServiceProvider, status) };

    private static ProviderDayOffRequest DayOff(Provider p, DateTime requestedAt, params DateOnly[] days)
    {
        var r = new ProviderDayOffRequest { Provider = p, Status = DayOffStatuses.Pending, RequestedAt = requestedAt, Reason = "Trip" };
        foreach (var d in days) r.Days.Add(new ProviderUnavailableDate { Provider = p, UnavailableDate = d });
        return r;
    }

    [Fact]
    public async Task DayOffs_NameChanges_AndSuspendedProviders_AreCounted_WithWhereEachButtonGoes()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", Roles.Administrator);
        var vince = Approved("Vince");
        var liza = Approved("Liza");
        var old = Approved("Olga");
        var off = Approved("Sam", AccountStatuses.Suspended);
        db.Users.Add(admin);
        db.Providers.AddRange(vince, liza, old, off);
        await db.SaveChangesAsync();

        db.DayOffRequests.AddRange(
            DayOff(liza, Now.UtcDateTime.AddHours(-1), new DateOnly(2026, 11, 20)),
            DayOff(vince, Now.UtcDateTime.AddHours(-5), new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 26)),   // waited longest
            DayOff(old, Now.UtcDateTime.AddDays(-9), new DateOnly(2026, 11, 2)));                                // its day has passed: not counted
        db.NameChangeRequests.Add(new ProviderNameChangeRequest { ProviderId = liza.ProviderId, FirstName = "Liza", MiddleName = "Lim", LastName = "Uy", Reason = "Married" });
        await db.SaveChangesAsync();

        var vm = (await new AdminDashboardService(db, new FixedClock()).GetAsync(admin.UserId))!;

        Assert.Collection(vm.Attention,
            a =>
            {
                Assert.Equal("2 provider day-off requests awaiting approval", a.Text);
                Assert.Equal(AdminAttentionAction.ReviewDayOffs, a.Action);
                Assert.Equal(vince.ProviderId, a.TargetId);
            },
            a =>
            {
                Assert.Equal("1 name change request pending review", a.Text);
                Assert.Equal(AdminAttentionAction.NameChangeList, a.Action);
            },
            a =>
            {
                Assert.Equal("1 suspended account requiring review", a.Text);
                Assert.Equal(AdminAttentionAction.SuspendedProviders, a.Action);       // Sam is a provider: Admin > Service Providers
            });
    }

    [Fact]
    public async Task AnsweredRequests_LeaveTheList()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", Roles.Administrator);
        var liza = Approved("Liza");
        db.Users.Add(admin);
        db.Providers.Add(liza);
        await db.SaveChangesAsync();
        var requests = new NameChangeRequestService(db);
        Assert.True((await requests.RequestAsync(liza.UserId, new NameChangeForm { FirstName = "Liza", MiddleName = "Lim", LastName = "Uy", Reason = "Married" })).Done);
        var dashboard = new AdminDashboardService(db, new FixedClock());

        Assert.Single((await dashboard.GetAsync(admin.UserId))!.Attention);

        await requests.DecideAsync(admin.UserId, (await db.NameChangeRequests.SingleAsync()).RequestId, approve: true);

        Assert.Empty((await dashboard.GetAsync(admin.UserId))!.Attention);
    }
}
