using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Admin > Bookings > "View": the booking card (BookingMonitorService.GetDetailsAsync).
public class BookingDetailsTests
{
    private static (BookingMonitorService svc, ServisyoDbContext db) Create()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new BookingMonitorService(db), db);
    }

    private static User NewUser(string first, string last, string role, string? phone = null, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, LastName = last, Email = $"{first.ToLowerInvariant()}.{Guid.NewGuid():N}@test.com", PasswordHash = "x",
        PhoneNumber = phone, Role = role, AccountStatus = status
    };

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator, status: status);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    /// A booking with two services (Plumbing), made Nov 3, 2026 10:15 UTC (= 6:15 PM in Cebu), for Nov 20 at 9:00 for 90 minutes.
    private static async Task<Booking> AddBookingAsync(ServisyoDbContext db, PaymentDetail? payment = null, string status = BookingStatuses.Accepted,
        string bookingPayment = PaymentStatuses.Unpaid)
    {
        var plumbing = new ServiceCategory { CategoryName = "Plumbing" };
        var customer = new Customer { User = NewUser("Ana", "Cruz", Roles.Customer, phone: "09171234567") };
        var provider = new Provider
        {
            BusinessName = "Marco's Plumbing Works", ApprovalStatus = ApprovalStatuses.Approved,
            User = NewUser("Marco", "Reyes", Roles.ServiceProvider, phone: "09181234567")
        };
        var booking = new Booking
        {
            Customer = customer, Provider = provider, BookingDate = new DateOnly(2026, 11, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 90, TotalFee = 1350m, Status = status, PaymentStatus = bookingPayment,
            ServiceLocation = "Salinas Drive, Brgy. Lahug, Cebu City", ServiceDescription = "Kitchen sink leaks under the cabinet.",
            CreatedAt = new DateTime(2026, 11, 3, 10, 15, 0, DateTimeKind.Utc), RowVersion = new byte[] { 1 },
            PaymentDetail = payment
        };
        booking.Services.Add(new BookingService
        {
            Service = new Service { Provider = provider, Category = plumbing, ServiceName = "Pipe Repair", Description = "Fix leaking pipes.", Price = 850m },
            PriceSnapshot = 850m, DurationMinutesSnapshot = 60
        });
        booking.Services.Add(new BookingService
        {
            Service = new Service { Provider = provider, Category = plumbing, ServiceName = "Drain Unclogging", Price = 500m },
            PriceSnapshot = 500m, DurationMinutesSnapshot = 30
        });
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    [Theory]
    [InlineData(Roles.Customer, AccountStatuses.Active)]
    [InlineData(Roles.ServiceProvider, AccountStatuses.Active)]
    [InlineData(Roles.Administrator, AccountStatuses.Suspended)]
    public async Task OnlyAnActiveAdministrator_CanOpenABooking(string role, string status)
    {
        var (svc, db) = Create();
        var booking = await AddBookingAsync(db);
        var someone = NewUser("Someone", "Else", role, status: status);
        db.Users.Add(someone);
        await db.SaveChangesAsync();

        var lookup = await svc.GetDetailsAsync(someone.UserId, booking.BookingId);

        Assert.False(lookup.Allowed);
        Assert.Null(lookup.Details);
    }

    [Fact]
    public async Task AMissingBooking_IsAllowedButEmpty()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var lookup = await svc.GetDetailsAsync(admin.UserId, 999_999);

        Assert.True(lookup.Allowed);
        Assert.Null(lookup.Details);
    }

    [Fact]
    public async Task TheCard_HasEverySection()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var booking = await AddBookingAsync(db, new PaymentDetail
        {
            PaymentId = PaymentIds.GCash, PaymentStatus = PaymentRecordStatuses.Paid,
            TransactionReference = "GCASH-48213907", CreatedAt = DateTime.UtcNow, PaidAt = DateTime.UtcNow
        }, bookingPayment: PaymentStatuses.Paid);

        var d = (await svc.GetDetailsAsync(admin.UserId, booking.BookingId)).Details!;

        // header
        Assert.Equal($"BK-2026-{booking.BookingId:D4}", d.Reference);
        Assert.Equal("Accepted", d.Status.Badge);
        // 1. general
        Assert.Equal(new DateOnly(2026, 11, 20), d.Date);
        Assert.Equal(new TimeOnly(9, 0), d.StartTime);
        Assert.Equal(new TimeOnly(10, 30), d.EndTime);
        Assert.Equal(new DateTime(2026, 11, 3, 18, 15, 0), d.CreatedAtCebu);          // 10:15 UTC = 6:15 PM in Cebu
        // 2. services, in the order they were booked, with category, description and the price at booking time
        Assert.Equal(new[] { "Pipe Repair", "Drain Unclogging" }, d.Services.Select(s => s.Name));
        Assert.All(d.Services, s => Assert.Equal("Plumbing", s.Category));
        Assert.Equal("Fix leaking pipes.", d.Services[0].Description);
        Assert.Null(d.Services[1].Description);
        Assert.Equal(new[] { 850m, 500m }, d.Services.Select(s => s.Price));
        Assert.Equal("Kitchen sink leaks under the cabinet.", d.RequestDescription);
        Assert.Equal(1350m, d.QuotedTotal);
        // 3. customer
        Assert.Equal("Ana Cruz", d.CustomerName);
        Assert.StartsWith("ana.", d.CustomerEmail);
        Assert.Equal("09171234567", d.CustomerPhone);
        Assert.Equal("Salinas Drive, Brgy. Lahug, Cebu City", d.ServiceAddress);
        // 4. provider
        Assert.Equal("Marco Reyes", d.ProviderName);
        Assert.Equal("Marco's Plumbing Works", d.BusinessName);
        Assert.StartsWith("marco.", d.ProviderEmail);
        Assert.Equal("09181234567", d.ProviderPhone);
        // 5. money
        Assert.Equal(1350m, d.TotalAmount);
        Assert.Equal("Mock E-Wallet (GCash)", d.PaymentMethod);
        Assert.Equal("GCASH-48213907", d.TransactionReference);
        Assert.Equal("Paid", d.Payment.Text);
    }

    [Fact]
    public async Task CashOnCompletion_NotCollectedYet_ShowsCashDue()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var booking = await AddBookingAsync(db, new PaymentDetail
        {
            PaymentId = PaymentIds.Cash, PaymentStatus = PaymentRecordStatuses.PendingCollection,
            TransactionReference = "CASH-2026-0042", CreatedAt = DateTime.UtcNow
        });

        var d = (await svc.GetDetailsAsync(admin.UserId, booking.BookingId)).Details!;

        Assert.Equal("Cash on Completion", d.PaymentMethod);
        Assert.Equal("CASH-2026-0042", d.TransactionReference);
        Assert.Equal("Cash due", d.Payment.Text);
    }

    [Fact]
    public async Task BeforeAnyPayment_TheTotalIsTheBookingFee_AndNothingIsChosenYet()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var booking = await AddBookingAsync(db, payment: null, status: BookingStatuses.Pending);

        var d = (await svc.GetDetailsAsync(admin.UserId, booking.BookingId)).Details!;

        Assert.Equal("Pending", d.Status.Badge);
        Assert.Equal(1350m, d.TotalAmount);
        Assert.Null(d.PaymentMethod);
        Assert.Null(d.TransactionReference);
        Assert.Equal("Unpaid", d.Payment.Text);
    }

    [Theory]
    [InlineData(PaymentMethods.CashOnCompletion, "CASH-2026-0001", "Cash on Completion")]
    [InlineData(PaymentMethods.MockEWallet, "GCASH-12345678", "Mock E-Wallet (GCash)")]
    [InlineData(PaymentMethods.MockEWallet, "MAYA-12345678", "Mock E-Wallet (Maya)")]
    [InlineData(null, null, null)]
    public void PaymentMethodText_IsReadable(string? method, string? reference, string? expected)
        => Assert.Equal(expected, BookingDetailsViewModel.PaymentMethodText(method, reference));
}
