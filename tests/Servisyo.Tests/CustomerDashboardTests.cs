using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class DashboardStatesTests
{
    [Theory]
    [InlineData(BookingStatuses.Pending, PaymentStatuses.Unpaid, DashboardState.WaitingForQuotation)]
    [InlineData(BookingStatuses.Accepted, PaymentStatuses.Unpaid, DashboardState.QuotationReceived)]
    [InlineData(BookingStatuses.Accepted, PaymentStatuses.Paid, DashboardState.BookingConfirmed)]
    [InlineData(BookingStatuses.InProgress, PaymentStatuses.Paid, DashboardState.ServiceInProgress)]
    [InlineData(BookingStatuses.Completed, PaymentStatuses.Paid, DashboardState.ServiceCompleted)]
    [InlineData(BookingStatuses.Rejected, PaymentStatuses.Unpaid, DashboardState.NoBooking)]
    [InlineData(BookingStatuses.Cancelled, PaymentStatuses.Unpaid, DashboardState.NoBooking)]
    public void FromBooking_MapsRealStatusToDashboardState(string status, string payment, DashboardState expected)
        => Assert.Equal(expected, DashboardStates.FromBooking(status, payment));

    [Fact]
    public void Describe_QuotationReceived_ShowsAmountAndApproveButton()
    {
        var info = DashboardStates.Describe(DashboardState.QuotationReceived, 850m, "Juan");
        Assert.Contains("₱850", info.Message);
        Assert.Equal("Approve & Pay", info.ActionLabel);
        Assert.Equal(2, info.ActiveStep);
    }

    [Fact]
    public void Describe_Completed_MarksEveryStepDone()
    {
        var info = DashboardStates.Describe(DashboardState.ServiceCompleted, 850m, "Juan");
        Assert.True(info.AllDone);
        Assert.Equal("Leave a Review", info.ActionLabel);
        Assert.Contains("Juan", info.Message);
    }
}

public class AvailabilityHintTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14);   // a Wednesday

    private static Dictionary<byte, TimeOnly> Days(params byte[] days)
        => days.ToDictionary(d => d, _ => new TimeOnly(17, 0));

    [Fact]
    public void TodayWithHoursLeft_IsAvailableToday()
        => Assert.Equal("Available Today",
            AvailabilityHint.Next(0, Days(3), new HashSet<DateOnly>(), Wednesday, new TimeOnly(9, 0)));

    [Fact]
    public void TodayAfterClosing_MovesToNextWorkingDay()
        => Assert.Equal("Available Tomorrow",
            AvailabilityHint.Next(0, Days(3, 4), new HashSet<DateOnly>(), Wednesday, new TimeOnly(18, 0)));

    [Fact]
    public void MinimumNotice_SkipsEarlyDays()
        => Assert.Equal("Available This Week",
            AvailabilityHint.Next(2, Days(3, 4, 5), new HashSet<DateOnly>(), Wednesday, new TimeOnly(9, 0)));

    [Fact]
    public void UnavailableDate_IsSkipped()
        => Assert.Equal("Available Tomorrow",
            AvailabilityHint.Next(0, Days(3, 4), new HashSet<DateOnly> { Wednesday }, Wednesday, new TimeOnly(9, 0)));

    [Fact]
    public void NoSchedule_GivesNoHint()
        => Assert.Null(AvailabilityHint.Next(0, Days(), new HashSet<DateOnly>(), Wednesday, new TimeOnly(9, 0)));
}

public class CustomerDashboardServiceTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static (CustomerDashboardService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new CustomerDashboardService(db, new FixedClock()), db);
    }

    private static Customer NewCustomer(string first = "Ana", string email = "ana@test.com", string status = AccountStatuses.Active)
        => new()
        {
            User = new User
            {
                FirstName = first, LastName = "Reyes", Email = email, PasswordHash = "x",
                Role = Roles.Customer, AccountStatus = status
            }
        };

    private static Provider NewProvider(string name = "Juan Plumbing",
        string approval = ApprovalStatuses.Approved, string account = AccountStatuses.Active)
        => new()
        {
            BusinessName = name, ApprovalStatus = approval, MinNoticeDays = 0,
            User = new User
            {
                FirstName = name, LastName = "Owner", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
                Role = Roles.ServiceProvider, AccountStatus = account
            }
        };

    private static Service NewService(Provider p, ServiceCategory cat, string name = "Pipe Repair")
        => new() { Provider = p, Category = cat, ServiceName = name, Price = 850m, IsActive = true };

    private static Booking NewBooking(Customer c, Provider p, Service s, string status,
        string payment = PaymentStatuses.Unpaid, int daysFromToday = 1)
    {
        var b = new Booking
        {
            Customer = c, Provider = p,
            BookingDate = new DateOnly(2026, 10, 14).AddDays(daysFromToday),
            BookingTime = new TimeOnly(14, 0),
            TotalDurationMinutes = 120, TotalFee = 850m,
            Status = status, PaymentStatus = payment,
            RowVersion = new byte[] { 1 }
        };
        b.Services.Add(new BookingService { Service = s, PriceSnapshot = 850m, DurationMinutesSnapshot = 120 });
        return b;
    }

    [Fact]
    public async Task NoBookings_ShowsEmptyState()
    {
        var (svc, db) = Create();
        var customer = NewCustomer();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var vm = await svc.GetAsync(customer.UserId);

        Assert.NotNull(vm);
        Assert.Equal(DashboardState.NoBooking, vm!.State);
        Assert.Null(vm.Current);
        Assert.Empty(vm.Recent);
        Assert.Equal("Ana", vm.Shell.FirstName);
        Assert.Equal("AR", vm.Shell.Initials);
    }

    [Fact]
    public async Task PendingBooking_ShowsWaitingWithTitleAndProvider()
    {
        var (svc, db) = Create();
        var customer = NewCustomer();
        var provider = NewProvider();
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        var booking = NewBooking(customer, provider, NewService(provider, cat), BookingStatuses.Pending);
        booking.Services.Add(new BookingService { Service = NewService(provider, cat, "Waterproofing"), PriceSnapshot = 0, DurationMinutesSnapshot = 60 });
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var vm = await svc.GetAsync(customer.UserId);

        Assert.Equal(DashboardState.WaitingForQuotation, vm!.State);
        Assert.Equal("Pipe Repair & Waterproofing", vm.Current!.Title);
        Assert.Equal("Juan Plumbing", vm.Current.ProviderName);
        Assert.StartsWith("BK-", vm.Current.Reference);
    }

    [Fact]
    public async Task AcceptedAndPaid_ShowsConfirmedWithTomorrowText()
    {
        var (svc, db) = Create();
        var customer = NewCustomer();
        var provider = NewProvider();
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        db.Bookings.Add(NewBooking(customer, provider, NewService(provider, cat),
            BookingStatuses.Accepted, PaymentStatuses.Paid, daysFromToday: 1));
        await db.SaveChangesAsync();

        var vm = await svc.GetAsync(customer.UserId);

        Assert.Equal(DashboardState.BookingConfirmed, vm!.State);
        Assert.StartsWith("Tomorrow · 2:00 PM", vm.Current!.UpcomingText);
    }

    [Fact]
    public async Task InProgressBooking_IsPreferredOverOtherActiveBookings()
    {
        var (svc, db) = Create();
        var customer = NewCustomer();
        var provider = NewProvider();
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        var service = NewService(provider, cat);
        db.Bookings.Add(NewBooking(customer, provider, service, BookingStatuses.Pending, daysFromToday: 1));
        db.Bookings.Add(NewBooking(customer, provider, service, BookingStatuses.InProgress, daysFromToday: 5));
        await db.SaveChangesAsync();

        var vm = await svc.GetAsync(customer.UserId);

        Assert.Equal(DashboardState.ServiceInProgress, vm!.State);
    }

    [Fact]
    public async Task CompletedWithoutReview_ShowsCompleted_ButReviewedOneDoesNot()
    {
        var (svc, db) = Create();
        var customer = NewCustomer();
        var provider = NewProvider();
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        var booking = NewBooking(customer, provider, NewService(provider, cat),
            BookingStatuses.Completed, PaymentStatuses.Paid, daysFromToday: -1);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        Assert.Equal(DashboardState.ServiceCompleted, (await svc.GetAsync(customer.UserId))!.State);

        db.Reviews.Add(new Review { Booking = booking, Rating = 5 });
        await db.SaveChangesAsync();

        Assert.Equal(DashboardState.NoBooking, (await svc.GetAsync(customer.UserId))!.State);
    }

    [Fact]
    public async Task RejectedAndCancelledBookings_AreNotCurrent_ButAppearInRecent()
    {
        var (svc, db) = Create();
        var customer = NewCustomer();
        var provider = NewProvider();
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        var service = NewService(provider, cat);
        db.Bookings.Add(NewBooking(customer, provider, service, BookingStatuses.Rejected, daysFromToday: 2));
        db.Bookings.Add(NewBooking(customer, provider, service, BookingStatuses.Cancelled, daysFromToday: 3));
        await db.SaveChangesAsync();

        var vm = await svc.GetAsync(customer.UserId);

        Assert.Equal(DashboardState.NoBooking, vm!.State);
        Assert.Equal(2, vm.Recent.Count);
    }

    [Fact]
    public async Task OtherCustomersBookings_AreNeverShown()
    {
        var (svc, db) = Create();
        var ana = NewCustomer("Ana", "ana@test.com");
        var ben = NewCustomer("Ben", "ben@test.com");
        var provider = NewProvider();
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        db.Bookings.Add(NewBooking(ben, provider, NewService(provider, cat), BookingStatuses.Pending));
        db.Customers.Add(ana);
        await db.SaveChangesAsync();

        var vm = await svc.GetAsync(ana.UserId);

        Assert.Equal(DashboardState.NoBooking, vm!.State);
        Assert.Empty(vm.Recent);
    }

    [Fact]
    public async Task SuspendedCustomer_GetsNoDashboard()
    {
        var (svc, db) = Create();
        var customer = NewCustomer(status: AccountStatuses.Suspended);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        Assert.Null(await svc.GetAsync(customer.UserId));
    }

    [Fact]
    public async Task NonCustomerUserId_GetsNoDashboard()
    {
        var (svc, db) = Create();
        var provider = NewProvider();
        db.Providers.Add(provider);
        await db.SaveChangesAsync();

        Assert.Null(await svc.GetAsync(provider.UserId));
    }

    [Fact]
    public async Task Recommended_OnlyApprovedActiveProviders_WithAvailabilityHint()
    {
        var (svc, db) = Create();
        var customer = NewCustomer();
        var cat = new ServiceCategory { CategoryName = "Plumbing" };

        var good = NewProvider("Good Plumbing");
        db.Services.Add(NewService(good, cat));
        db.ProviderAvailabilities.Add(new ProviderAvailability
        {
            Provider = good, DayOfWeek = 3, IsAvailable = true,                // Wednesday = today
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0)
        });

        var pending = NewProvider("Pending Co", ApprovalStatuses.Pending);
        db.Services.Add(NewService(pending, cat));
        var suspended = NewProvider("Suspended Co", account: AccountStatuses.Suspended);
        db.Services.Add(NewService(suspended, cat));

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var vm = await svc.GetAsync(customer.UserId);

        var rec = Assert.Single(vm!.Recommended);
        Assert.Equal("Good Plumbing", rec.Name);
        Assert.Equal("Plumbing", rec.CategoryName);
        Assert.Equal("Available Today", rec.AvailabilityText);
    }

    [Fact]
    public async Task UnreadCounts_OnlyCountThisUsersUnreadItems()
    {
        var (svc, db) = Create();
        var ana = NewCustomer("Ana", "ana@test.com");
        var ben = NewCustomer("Ben", "ben@test.com");
        db.Customers.AddRange(ana, ben);
        db.Notifications.AddRange(
            new Notification { User = ana.User, Message = "unread 1" },
            new Notification { User = ana.User, Message = "unread 2" },
            new Notification { User = ana.User, Message = "read", IsRead = true },
            new Notification { User = ben.User, Message = "someone else's" });
        db.Messages.Add(new Message { Sender = ben.User, Receiver = ana.User, MessageText = "hi" });
        await db.SaveChangesAsync();

        var vm = await svc.GetAsync(ana.UserId);

        Assert.Equal(2, vm!.Shell.UnreadNotifications);
        Assert.Equal(1, vm.Shell.UnreadMessages);
    }
}
