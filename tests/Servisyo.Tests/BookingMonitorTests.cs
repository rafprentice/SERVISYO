using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class BookingFiltersTests
{
    [Theory]
    [InlineData("Pending", "Pending")]
    [InlineData("accepted", "Accepted")]
    [InlineData("In Progress", "In Progress")]
    [InlineData("in-progress", "In Progress")]
    [InlineData("INPROGRESS", "In Progress")]
    [InlineData("completed", "Completed")]
    [InlineData("Rejected", "Rejected")]
    [InlineData("Cancelled", "Cancelled")]
    [InlineData("canceled", "Cancelled")]
    [InlineData("All", "All")]
    [InlineData("nonsense", "All")]
    [InlineData("", "All")]
    [InlineData(null, "All")]
    public void NormalizeStatus_AcceptsKnownValues_AndFallsBackToAll(string? input, string expected)
        => Assert.Equal(expected, BookingFilters.NormalizeStatus(input));
}

public class PaymentDisplayTests
{
    [Theory]
    [InlineData(PaymentStatuses.Paid, "Paid", "completed")]
    [InlineData(PaymentStatuses.Refunded, "Refunded", "refunded")]
    [InlineData(PaymentStatuses.Unpaid, "Unpaid", "muted")]
    [InlineData("Something else", "Something else", "muted")]
    public void For_MapsPaymentStatusToBadge(string status, string text, string tone)
    {
        var d = PaymentDisplay.For(status);
        Assert.Equal(text, d.Text);
        Assert.Equal(tone, d.Tone);
    }
}

public class BookingMonitorServiceTests
{
    private static (BookingMonitorService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new BookingMonitorService(db), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static Customer NewCustomer(string first, string last)
        => new() { User = NewUser(first, last, Roles.Customer) };

    private static Provider NewProvider(string first, string last, string business)
        => new() { BusinessName = business, ApprovalStatus = ApprovalStatuses.Approved, User = NewUser(first, last, Roles.ServiceProvider) };

    private static Booking NewBooking(Customer c, Provider p, string status, string payment = PaymentStatuses.Unpaid,
        params string[] serviceNames)
    {
        var cat = new ServiceCategory { CategoryName = "General" };
        var b = new Booking
        {
            Customer = c, Provider = p, BookingDate = new DateOnly(2026, 9, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 850m, Status = status, PaymentStatus = payment,
            RowVersion = new byte[] { 1 }
        };
        foreach (var name in serviceNames.Length == 0 ? new[] { "Pipe Repair" } : serviceNames)
        {
            var service = new Service { Provider = p, Category = cat, ServiceName = name, Price = 850m, IsActive = true };
            b.Services.Add(new BookingService { Service = service, PriceSnapshot = 850m, DurationMinutesSnapshot = 60 });
        }
        return b;
    }

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator, status);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    /// 8 bookings like the design: Pending 2, Accepted 1, In Progress 1, Completed 2, Rejected 1, Cancelled 1.
    private static async Task SeedDesignAsync(ServisyoDbContext db)
    {
        var ana = NewCustomer("Ana", "Reyes");
        var brent = NewCustomer("Brent", "Lim");
        var dennis = NewCustomer("Dennis", "Go");
        var elena = NewCustomer("Elena", "Cruz");
        var carla = NewCustomer("Carla", "Tan");
        var marco = NewProvider("Marco", "Reyes", "Marco's Plumbing Works");
        var liza = NewProvider("Liza", "Santos", "Liza Clean Services");
        var danilo = NewProvider("Danilo", "Cruz", "Cruz General Repairs");
        var rhea = NewProvider("Rhea", "Villanueva", "Rhea's Home Care");

        db.Bookings.AddRange(
            NewBooking(ana, marco, BookingStatuses.Accepted, PaymentStatuses.Paid, "Pipe Repair"),            // 1
            NewBooking(brent, liza, BookingStatuses.Pending, PaymentStatuses.Unpaid, "Deep Cleaning"),         // 2
            NewBooking(dennis, danilo, BookingStatuses.InProgress, PaymentStatuses.Paid, "AC Service"),        // 3
            NewBooking(ana, rhea, BookingStatuses.Completed, PaymentStatuses.Paid, "Laundry"),                 // 4
            NewBooking(elena, marco, BookingStatuses.Completed, PaymentStatuses.Paid, "Drain Unclogging"),     // 5
            NewBooking(carla, danilo, BookingStatuses.Cancelled, PaymentStatuses.Refunded, "Appliance Repair"),// 6
            NewBooking(dennis, liza, BookingStatuses.Pending, PaymentStatuses.Unpaid, "Office Cleaning"),      // 7
            NewBooking(brent, marco, BookingStatuses.Rejected, PaymentStatuses.Unpaid, "Fixture Install"));    // 8
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanSeeTheList()
    {
        var (svc, db) = Create();
        var customer = NewCustomer("Ana", "Reyes");
        var provider = NewProvider("Marco", "Reyes", "Marco's");
        db.Customers.Add(customer);
        db.Providers.Add(provider);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        Assert.Null(await svc.GetBookingsAsync(customer.UserId, new BookingListQuery()));
        Assert.Null(await svc.GetBookingsAsync(provider.UserId, new BookingListQuery()));
        Assert.Null(await svc.GetBookingsAsync(suspendedAdmin.UserId, new BookingListQuery()));
        Assert.Null(await svc.GetBookingsAsync(99999, new BookingListQuery()));
    }

    [Fact]
    public async Task NoBookings_ShowsAnEmptyTable()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var vm = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery()))!;

        Assert.Equal(0, vm.CountAll);
        Assert.Empty(vm.Rows);
        Assert.Equal(7, vm.Tabs.Count);
        Assert.All(vm.Tabs, t => Assert.Equal(0, t.Count));
    }

    [Fact]
    public async Task TabNumbers_MatchTheDesign_InTheDesignOrder()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery()))!;

        Assert.Equal(
            new[] { "All:8", "Pending:2", "Accepted:1", "In Progress:1", "Completed:2", "Rejected:1", "Cancelled:1" },
            vm.Tabs.Select(t => $"{t.Label}:{t.Count}"));
        Assert.Equal(8, vm.Rows.Count);
        Assert.Equal(8, vm.TotalMatching);
    }

    [Theory]
    [InlineData("Pending", 2)]
    [InlineData("Accepted", 1)]
    [InlineData("In Progress", 1)]
    [InlineData("in-progress", 1)]
    [InlineData("Completed", 2)]
    [InlineData("Rejected", 1)]
    [InlineData("Cancelled", 1)]
    [InlineData("All", 8)]
    [InlineData("nonsense", 8)]
    public async Task Tabs_FilterTheRows_AndTheNumbersStayTheSame(string tab, int expectedRows)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Status = tab }))!;

        Assert.Equal(expectedRows, vm.Rows.Count);
        Assert.Equal(8, vm.CountAll);
        Assert.Equal(2, vm.Tabs.Single(t => t.Key == "Pending").Count);
    }

    [Fact]
    public async Task Rows_ShowServiceNamesDateAndBadges()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery()))!;

        var pipe = vm.Rows.Single(r => r.Service == "Pipe Repair");
        Assert.Equal("Ana Reyes", pipe.CustomerName);
        Assert.Equal("Marco Reyes", pipe.ProviderName);
        Assert.Equal(new DateOnly(2026, 9, 20), pipe.Date);
        Assert.Equal(new TimeOnly(9, 0), pipe.Time);
        Assert.Equal("Paid", pipe.Payment.Text);
        Assert.Equal("Accepted", pipe.Status.Badge);

        var cancelled = vm.Rows.Single(r => r.Service == "Appliance Repair");
        Assert.Equal("Refunded", cancelled.Payment.Text);
        Assert.Equal("Cancelled", cancelled.Status.Badge);

        var rejected = vm.Rows.Single(r => r.Service == "Fixture Install");
        Assert.Equal("Unpaid", rejected.Payment.Text);
        Assert.Equal("danger", rejected.Status.Tone);
    }

    [Fact]
    public async Task Rows_AreNewestFirst()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery()))!;

        Assert.Equal(vm.Rows.Select(r => r.BookingId).OrderByDescending(x => x), vm.Rows.Select(r => r.BookingId));
        Assert.Equal("Fixture Install", vm.Rows[0].Service);     // the last booking made
        Assert.Equal("Pipe Repair", vm.Rows[^1].Service);        // the first booking made
    }

    [Fact]
    public async Task BookingWithSeveralServices_ShowsThemJoined()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.Bookings.Add(NewBooking(NewCustomer("Ana", "Reyes"), NewProvider("Marco", "Reyes", "M"),
            BookingStatuses.Pending, PaymentStatuses.Unpaid, "Pipe Repair", "Waterproofing"));
        await db.SaveChangesAsync();

        var vm = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery()))!;

        Assert.Equal("Pipe Repair & Waterproofing", Assert.Single(vm.Rows).Service);
    }

    [Theory]
    [InlineData("deep", new[] { "Deep Cleaning" })]                       // service
    [InlineData("  AC SERVICE ", new[] { "AC Service" })]                 // service, any case, trimmed
    [InlineData("dennis", new[] { "Office Cleaning", "AC Service" })]     // customer (newest first)
    [InlineData("villanueva", new[] { "Laundry" })]                       // provider
    [InlineData("clean services", new[] { "Office Cleaning", "Deep Cleaning" })]   // provider business name
    [InlineData("zzz", new string[0])]                                    // nothing
    public async Task Search_FindsByServiceCustomerOrProvider(string term, string[] expectedServices)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Q = term }))!;

        Assert.Equal(expectedServices, vm.Rows.Select(r => r.Service));
        Assert.Equal(expectedServices.Length, vm.TotalMatching);
        Assert.Equal(8, vm.CountAll);                                     // tab numbers ignore the search
    }

    [Fact]
    public async Task Search_FindsABookingByItsNumber_WithOrWithoutTheHash()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);
        var acService = await db.Bookings.AsNoTracking()
            .Where(b => b.Services.Any(s => s.Service.ServiceName == "AC Service"))
            .Select(b => b.BookingId).SingleAsync();

        var plain = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Q = acService.ToString() }))!;
        var hashed = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Q = "#" + acService }))!;

        Assert.Equal("AC Service", Assert.Single(plain.Rows).Service);
        Assert.Equal("AC Service", Assert.Single(hashed.Rows).Service);
    }

    [Fact]
    public async Task Search_AndTab_WorkTogether()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Q = "dennis", Status = "Pending" }))!;

        Assert.Equal("Office Cleaning", Assert.Single(vm.Rows).Service);
    }

    [Fact]
    public async Task Paging_ShowsTwentyPerPage_AndClampsThePageNumber()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var ana = NewCustomer("Ana", "Reyes");
        var marco = NewProvider("Marco", "Reyes", "Marco's");
        for (var i = 0; i < 45; i++)
            db.Bookings.Add(NewBooking(ana, marco, BookingStatuses.Completed, PaymentStatuses.Paid, $"Job {i:00}"));
        await db.SaveChangesAsync();

        var first = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Page = 1 }))!;
        var last = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Page = 3 }))!;
        var tooFar = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Page = 99 }))!;
        var negative = (await svc.GetBookingsAsync(admin.UserId, new BookingListQuery { Page = -1 }))!;

        Assert.Equal(20, first.Rows.Count);
        Assert.Equal(5, last.Rows.Count);
        Assert.Equal(3, first.PageCount);
        Assert.Equal(45, first.TotalMatching);
        Assert.Equal(3, tooFar.Page);
        Assert.Equal(1, negative.Page);
    }
}
