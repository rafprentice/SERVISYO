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
    [Fact]
    public void NothingWaiting_GivesNoRows()
        => Assert.Empty(AdminAttention.Build(0, 0, 0, 0));

    [Fact]
    public void OneOfEach_UsesSingularWording_InFixedOrder()
    {
        var rows = AdminAttention.Build(1, 1, 1, 1);

        Assert.Equal(4, rows.Count);
        Assert.Equal("1 provider application waiting for review", rows[0].Text);
        Assert.Equal("1 flagged review needs moderation", rows[1].Text);
        Assert.Equal("1 suspended account requiring review", rows[2].Text);
        Assert.Equal("1 reported booking issue pending resolution", rows[3].Text);
        Assert.Equal("View Booking", rows[3].ActionLabel);
    }

    [Fact]
    public void Many_UsesPluralWording()
    {
        var rows = AdminAttention.Build(2, 3, 2, 4);

        Assert.Equal("2 provider applications waiting for review", rows[0].Text);
        Assert.Equal("3 flagged reviews need moderation", rows[1].Text);
        Assert.Equal("2 suspended accounts requiring review", rows[2].Text);
        Assert.Equal("4 reported booking issues pending resolution", rows[3].Text);
        Assert.Equal("View Bookings", rows[3].ActionLabel);
    }

    [Fact]
    public void OnlyRowsWithWork_AreShown()
    {
        var rows = AdminAttention.Build(0, 2, 0, 0);

        var row = Assert.Single(rows);
        Assert.Equal("Moderate Reviews", row.ActionLabel);
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
        string? city = null, DateTime? applied = null)
        => new()
        {
            BusinessName = business, City = city, ApprovalStatus = approval,
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

        // one open report and one resolved report: only the open one counts
        db.BookingReports.Add(new BookingReport { Booking = other, ReportedBy = ana.User, Reason = "No show", Status = ReportStatuses.Open });
        db.BookingReports.Add(new BookingReport { Booking = done, ReportedBy = ana.User, Reason = "Old", Status = ReportStatuses.Resolved });
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(admin.UserId))!;

        Assert.Equal(2, vm.Customers);
        Assert.Equal(3, vm.Providers);
        Assert.Equal(5, vm.TotalUsers);          // the administrator is not counted
        Assert.Equal(2, vm.PendingApprovals);
        Assert.Equal(3, vm.TotalBookings);
        Assert.Equal(1, vm.FlaggedReviews);

        Assert.Collection(vm.Attention,
            a => Assert.Equal("2 provider applications waiting for review", a.Text),
            a => Assert.Equal("1 flagged review needs moderation", a.Text),
            a => Assert.Equal("1 suspended account requiring review", a.Text),
            a => Assert.Equal("1 reported booking issue pending resolution", a.Text));
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
    public async Task PendingApplications_OnlyPending_OldestFirst_WithCategoryAndCity()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var felix = NewProvider("Felix", "Torres", "Felix Plumbing", ApprovalStatuses.Pending, "Mandaue", DateTime.UtcNow.AddDays(-2));
        var maricel = NewProvider("Maricel", "Bautista", "Maricel Cleaning", ApprovalStatuses.Pending, "Poblacion", DateTime.UtcNow.AddDays(-1));
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
        Assert.Equal("Plumbing · Mandaue", first.Details);

        var second = vm.PendingApplications[1];
        Assert.Equal("Maricel Bautista", second.Name);
        Assert.Equal("MB", second.Initials);
        Assert.Equal("Maricel Cleaning · Poblacion", second.Details);   // no category yet -> business name
    }
}
