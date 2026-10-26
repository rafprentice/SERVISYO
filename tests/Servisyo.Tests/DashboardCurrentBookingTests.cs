using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class DashboardCurrentBookingTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static ServisyoDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User NewUser(string first, string role) =>
        new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = AccountStatuses.Active };

    [Fact]
    public async Task CurrentBooking_CarriesTheProvidersUserId_ForTheMessageProviderButton()
    {
        var db = NewDb();
        var customer = new Customer { User = NewUser("Ana", Roles.Customer) };
        var provider = new Provider { BusinessName = "Marco Works", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Marco", Roles.ServiceProvider) };
        db.Customers.Add(customer);
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        db.Bookings.Add(new Booking
        {
            CustomerId = customer.CustomerId, ProviderId = provider.ProviderId, BookingDate = new DateOnly(2026, 10, 16),
            BookingTime = new TimeOnly(13, 0), TotalDurationMinutes = 60, TotalFee = 500m,
            Status = BookingStatuses.Pending, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();

        var vm = await new CustomerDashboardService(db, new FixedClock()).GetAsync(customer.UserId);

        Assert.NotNull(vm?.Current);
        Assert.Equal(provider.UserId, vm!.Current!.ProviderUserId);
        Assert.NotEqual(0, vm.Current.ProviderUserId);
        Assert.Equal(DashboardState.WaitingForQuotation, vm.State);
    }
}
