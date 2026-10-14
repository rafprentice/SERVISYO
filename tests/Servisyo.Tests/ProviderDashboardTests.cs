using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ProfileStrengthTests
{
    [Fact]
    public void Everything_Done_Is100Percent_WithNoNextStep()
    {
        var (percent, next) = ProfileStrength.Calculate(true, true, true, true, true, true);
        Assert.Equal(100, percent);
        Assert.Null(next);
    }

    [Fact]
    public void Nothing_Done_Is0Percent_AndAsksForFirstService()
    {
        var (percent, next) = ProfileStrength.Calculate(false, false, false, false, false, false);
        Assert.Equal(0, percent);
        Assert.Equal("Add your first service", next);
    }

    [Fact]
    public void MissingOnlyPhoto_Is83Percent_AndAsksForPhoto()
    {
        var (percent, next) = ProfileStrength.Calculate(true, true, true, false, true, true);
        Assert.Equal(83, percent);
        Assert.Equal("Add a profile photo", next);
    }
}

public class ProviderBookingDisplayTests
{
    [Theory]
    [InlineData(BookingStatuses.Pending, "New Request", "Review Request")]
    [InlineData(BookingStatuses.Accepted, "Scheduled", "Start Service")]
    [InlineData(BookingStatuses.InProgress, "In Progress", "Complete Service")]
    [InlineData(BookingStatuses.Completed, "Completed", null)]
    [InlineData(BookingStatuses.Rejected, "Declined", null)]
    [InlineData(BookingStatuses.Cancelled, "Cancelled", null)]
    public void For_MapsStatusToBadgeAndAction(string status, string badge, string? action)
    {
        var d = ProviderBookingDisplay.For(status);
        Assert.Equal(badge, d.Badge);
        Assert.Equal(action, d.ActionLabel);
    }
}

public class NameFormatTests
{
    [Fact] public void ShortName_UsesFirstNameAndLastInitial() => Assert.Equal("Elena C.", NameFormat.ShortName("Elena", "Cruz"));
    [Fact] public void Initials_UseFirstAndLastWord() => Assert.Equal("MR", NameFormat.Initials("Marco Reyes"));
    [Fact] public void Truncate_ShortensLongText() => Assert.Equal("abcd…", NameFormat.Truncate("abcdefghij", 5));
}

public class ProviderDashboardServiceTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateOnly Today = new(2026, 10, 14);

    private static (ProviderDashboardService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new ProviderDashboardService(db, new FixedClock()), db);
    }

    private static Provider NewProvider(string business = "Marco's Plumbing Works",
        string approval = ApprovalStatuses.Approved, string account = AccountStatuses.Active)
        => new()
        {
            BusinessName = business, City = "Cebu City", ApprovalStatus = approval,
            User = new User
            {
                FirstName = "Marco", LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
                Role = Roles.ServiceProvider, AccountStatus = account
            }
        };

    private static Customer NewCustomer(string first = "Elena", string last = "Cruz")
        => new()
        {
            City = "Metro Cebu",
            User = new User
            {
                FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
                Role = Roles.Customer, AccountStatus = AccountStatuses.Active
            }
        };

    private static (Booking booking, Service service) NewBooking(
        Customer c, Provider p, string status, DateOnly date, TimeOnly time,
        string serviceName = "Drain Unclogging", string? location = "12 Mango Ave")
    {
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        var service = new Service { Provider = p, Category = cat, ServiceName = serviceName, Price = 600m, IsActive = true };
        var b = new Booking
        {
            Customer = c, Provider = p, BookingDate = date, BookingTime = time,
            TotalDurationMinutes = 60, TotalFee = 600m, Status = status,
            ServiceLocation = location, RowVersion = new byte[] { 1 }
        };
        b.Services.Add(new BookingService { Service = service, PriceSnapshot = 600m, DurationMinutesSnapshot = 60 });
        return (b, service);
    }

    [Fact]
    public async Task PendingProvider_IsSentToPendingPage()
    {
        var (svc, db) = Create();
        var p = NewProvider(approval: ApprovalStatuses.Pending);
        db.Providers.Add(p);
        await db.SaveChangesAsync();

        var result = await svc.GetAsync(p.UserId);

        Assert.Equal(ProviderDashboardAccess.Pending, result.Access);
        Assert.Null(result.Dashboard);
    }

    [Fact]
    public async Task RejectedProvider_GetsRejectedResult()
    {
        var (svc, db) = Create();
        var p = NewProvider(approval: ApprovalStatuses.Rejected);
        db.Providers.Add(p);
        await db.SaveChangesAsync();

        Assert.Equal(ProviderDashboardAccess.Rejected, (await svc.GetAsync(p.UserId)).Access);
    }

    [Fact]
    public async Task SuspendedProvider_AndNonProvider_AreNotAllowed()
    {
        var (svc, db) = Create();
        var suspended = NewProvider(account: AccountStatuses.Suspended);
        var customer = NewCustomer();
        db.Providers.Add(suspended);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetAsync(suspended.UserId)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetAsync(customer.UserId)).Access);
    }

    [Fact]
    public async Task NewApprovedProvider_SeesEmptyDashboard()
    {
        var (svc, db) = Create();
        var p = NewProvider();
        db.Providers.Add(p);
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(p.UserId)).Dashboard!;

        Assert.Equal(ProviderDashboardAccess.Allowed, (await svc.GetAsync(p.UserId)).Access);
        Assert.Equal("Marco", vm.Shell.FirstName);
        Assert.Equal("MR", vm.Shell.Initials);
        Assert.Equal("Marco's Plumbing Works", vm.Shell.Subtitle);
        Assert.Equal(0, vm.NewRequests);
        Assert.Null(vm.AvgRating);
        Assert.Empty(vm.Attention);
        Assert.Empty(vm.RecentRequests);
        Assert.Empty(vm.Today);
        Assert.Empty(vm.Reviews);
        Assert.Equal("No working hours set for today", vm.AvailabilityText);
        Assert.Equal("Add your first service", vm.ProfileNextStep);
    }

    [Fact]
    public async Task Counts_AndAttention_ReflectRealBookings_OfThisProviderOnly()
    {
        var (svc, db) = Create();
        var me = NewProvider();
        var other = NewProvider("Someone Else");
        var elena = NewCustomer();

        db.Bookings.Add(NewBooking(elena, me, BookingStatuses.Pending, Today.AddDays(1), new TimeOnly(10, 0)).booking);
        db.Bookings.Add(NewBooking(elena, me, BookingStatuses.Pending, Today.AddDays(1), new TimeOnly(14, 0)).booking);
        db.Bookings.Add(NewBooking(elena, me, BookingStatuses.Accepted, Today, new TimeOnly(10, 30)).booking);   // in 30 min
        db.Bookings.Add(NewBooking(elena, me, BookingStatuses.InProgress, Today, new TimeOnly(9, 0), "Pipe Repair").booking);
        db.Bookings.Add(NewBooking(elena, other, BookingStatuses.Pending, Today.AddDays(1), new TimeOnly(10, 0)).booking);
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(me.UserId)).Dashboard!;

        Assert.Equal(2, vm.NewRequests);
        Assert.Equal(1, vm.Upcoming);
        Assert.Equal(1, vm.InProgress);

        Assert.Equal(3, vm.Attention.Count);
        Assert.Equal("2 new booking requests waiting for your review", vm.Attention[0].Text);
        Assert.Equal("Upcoming job in 30 min — Drain Unclogging, Elena Cruz", vm.Attention[1].Text);
        Assert.StartsWith("In progress: Pipe Repair, Elena Cruz", vm.Attention[2].Text);

        Assert.Equal(4, vm.RecentRequests.Count);     // the other provider's booking is not included
        Assert.Equal(2, vm.Today.Count);              // today: accepted 10:30 + in progress 09:00
        Assert.Equal(new TimeOnly(9, 0), vm.Today[0].Time);
    }

    [Fact]
    public async Task OverdueAcceptedJob_IsFlaggedAsDue()
    {
        var (svc, db) = Create();
        var me = NewProvider();
        db.Bookings.Add(NewBooking(NewCustomer(), me, BookingStatuses.Accepted, Today, new TimeOnly(9, 0)).booking);
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(me.UserId)).Dashboard!;

        Assert.StartsWith("Job was due at 9:00 AM", vm.Attention.Single().Text);
    }

    [Fact]
    public async Task AddressIsHiddenUntilAccepted_ThenShown()
    {
        var (svc, db) = Create();
        var me = NewProvider();
        var elena = NewCustomer();
        db.Bookings.Add(NewBooking(elena, me, BookingStatuses.Pending, Today.AddDays(1), new TimeOnly(10, 0), location: "12 Mango Ave").booking);
        db.Bookings.Add(NewBooking(elena, me, BookingStatuses.Accepted, Today.AddDays(2), new TimeOnly(10, 0), location: "99 Secret St").booking);
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(me.UserId)).Dashboard!;

        var pending = vm.RecentRequests.Single(r => r.Status == BookingStatuses.Pending);
        var accepted = vm.RecentRequests.Single(r => r.Status == BookingStatuses.Accepted);
        Assert.Equal("Metro Cebu", pending.Location);   // only the customer's city
        Assert.Equal("99 Secret St", accepted.Location);
    }

    [Fact]
    public async Task Reviews_OnlyVisibleOnesOfThisProvider_WithShortAuthorName()
    {
        var (svc, db) = Create();
        var me = NewProvider();
        var other = NewProvider("Someone Else");
        var elena = NewCustomer();

        var mine = NewBooking(elena, me, BookingStatuses.Completed, Today.AddDays(-3), new TimeOnly(9, 0)).booking;
        var removed = NewBooking(elena, me, BookingStatuses.Completed, Today.AddDays(-4), new TimeOnly(9, 0)).booking;
        var theirs = NewBooking(elena, other, BookingStatuses.Completed, Today.AddDays(-3), new TimeOnly(9, 0)).booking;
        db.Reviews.Add(new Review { Booking = mine, Rating = 4, Comment = "Very professional and fast." });
        db.Reviews.Add(new Review { Booking = removed, Rating = 1, Comment = "Hidden", Status = ReviewStatuses.Removed });
        db.Reviews.Add(new Review { Booking = theirs, Rating = 5, Comment = "Not mine" });
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(me.UserId)).Dashboard!;

        var review = Assert.Single(vm.Reviews);
        Assert.Equal("Elena C.", review.Author);
        Assert.Equal("Very professional and fast.", review.Comment);
        Assert.Equal((byte)4, review.Rating);
    }

    [Fact]
    public async Task AvailabilityText_UsesTodaysSchedule_AndUnavailableDate()
    {
        var (svc, db) = Create();
        var me = NewProvider();
        db.ProviderAvailabilities.Add(new ProviderAvailability
        {
            Provider = me, DayOfWeek = 3, IsAvailable = true,                      // Wednesday = today
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0)
        });
        await db.SaveChangesAsync();

        Assert.Equal("Available today until 5:00 PM", (await svc.GetAsync(me.UserId)).Dashboard!.AvailabilityText);

        db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { Provider = me, UnavailableDate = Today });
        await db.SaveChangesAsync();

        Assert.Equal("You marked today as unavailable", (await svc.GetAsync(me.UserId)).Dashboard!.AvailabilityText);
    }

    [Fact]
    public async Task ProfileStrength_ImprovesWhenServiceAndScheduleExist()
    {
        var (svc, db) = Create();
        var me = NewProvider();
        me.Description = "Licensed plumber";
        me.Address = "Mabolo";
        me.User.PhoneNumber = "09171234567";
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        db.Services.Add(new Service { Provider = me, Category = cat, ServiceName = "Leak Repair", Price = 500m, IsActive = true });
        db.ProviderAvailabilities.Add(new ProviderAvailability
        {
            Provider = me, DayOfWeek = 1, IsAvailable = true,
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0)
        });
        await db.SaveChangesAsync();

        var vm = (await svc.GetAsync(me.UserId)).Dashboard!;

        Assert.Equal(83, vm.ProfilePercent);                       // only the photo is missing
        Assert.Equal("Add a profile photo", vm.ProfileNextStep);
    }
}
