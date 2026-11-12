using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Admin > Users > "View": one customer's contact details, saved address and booking activity.
public class CustomerDetailsTests
{
    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, LastName = last, Email = $"{first.ToLowerInvariant()}@test.com", PasswordHash = "x", Role = role,
        AccountStatus = status, CreatedAt = new DateTime(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc)        // Oct 2, 1:00 am in Cebu
    };

    private sealed record World(ServisyoDbContext Db, UserManagementService Users, User Admin, Customer Ana, Provider Marco, Service Pipe, Service Sink);

    private static async Task<World> WorldAsync()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", "Admin", Roles.Administrator);
        var ana = new Customer { Address = "  Lahug, Cebu City  ", User = NewUser("Ana", "Reyes", Roles.Customer) };
        ana.User.PhoneNumber = "09171234567";
        var marco = new Provider { BusinessName = "Marco's Plumbing", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Marco", "Cruz", Roles.ServiceProvider) };
        var category = new ServiceCategory { CategoryName = "Plumbing" };
        var pipe = new Service { Provider = marco, Category = category, ServiceName = "Pipe Leak Repair", Price = 800m };
        var sink = new Service { Provider = marco, Category = category, ServiceName = "Sink Unclogging", Price = 500m };
        db.Users.Add(admin);
        db.Customers.Add(ana);
        db.Services.AddRange(pipe, sink);
        await db.SaveChangesAsync();
        return new(db, new UserManagementService(db, NullLogger<UserManagementService>.Instance), admin, ana, marco, pipe, sink);
    }

    private static async Task<Booking> BookAsync(World w, Customer c, DateOnly day, string status, decimal fee, params Service[] services)
    {
        var b = new Booking
        {
            CustomerId = c.CustomerId, ProviderId = w.Marco.ProviderId, BookingDate = day, BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = fee, Status = status, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        };
        foreach (var s in services) b.Services.Add(new BookingService { ServiceId = s.ServiceId, PriceSnapshot = s.Price, DurationMinutesSnapshot = 60 });
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task TheDetails_ShowContact_Address_AndMemberSinceInCebuTime()
    {
        var w = await WorldAsync();

        var lookup = await w.Users.GetCustomerDetailsAsync(w.Admin.UserId, w.Ana.UserId);

        Assert.Equal(CustomerDetailsOutcome.Ok, lookup.Outcome);
        var d = lookup.Details!;
        Assert.Equal("Ana Reyes", d.Name);
        Assert.Equal("AR", d.Initials);
        Assert.Equal(AccountStatuses.Active, d.Status);
        Assert.Equal("completed", d.StatusTone);
        Assert.Equal("ana@test.com", d.Email);
        Assert.Equal("09171234567", d.Phone);
        Assert.Equal("Lahug, Cebu City", d.Address);
        Assert.Equal("Oct 2, 2026", d.MemberSince);
        Assert.Equal(0, d.TotalBookings);
        Assert.Empty(d.RecentBookings);
        Assert.Equal("Admin", d.Shell.Role);
    }

    [Fact]
    public async Task RecentBookings_AreTheNewestFive_WithServiceProviderDateAmountAndStatus()
    {
        var w = await WorldAsync();
        for (var day = 1; day <= 6; day++)
            await BookAsync(w, w.Ana, new DateOnly(2026, 11, day), BookingStatuses.Completed, 800m, w.Pipe);
        var newest = await BookAsync(w, w.Ana, new DateOnly(2026, 11, 20), BookingStatuses.Accepted, 1300m, w.Pipe, w.Sink);

        var d = (await w.Users.GetCustomerDetailsAsync(w.Admin.UserId, w.Ana.UserId)).Details!;

        Assert.Equal(7, d.TotalBookings);
        Assert.Equal(UserManagementService.RecentBookingsShown, d.RecentBookings.Count);
        var top = d.RecentBookings[0];
        Assert.Equal(newest.BookingId, top.BookingId);
        Assert.Equal("Pipe Leak Repair +1 more", top.ServiceTitle);
        Assert.Equal("Marco's Plumbing", top.ProviderName);
        Assert.Equal("Nov 20, 2026 · 9:00 AM", top.WhenText);
        Assert.Equal("₱1,300", top.AmountText);
        Assert.Equal(BookingStatuses.Accepted, top.Status);
        Assert.Equal("accepted", top.Tone);
        Assert.Equal(new[] { 20, 6, 5, 4, 3 }, d.RecentBookings.Select(b => int.Parse(b.WhenText.Split(' ')[1].TrimEnd(','))));
        Assert.Equal("Pipe Leak Repair", d.RecentBookings[1].ServiceTitle);
    }

    [Fact]
    public async Task OnlyThisCustomersBookings_AreCounted()
    {
        var w = await WorldAsync();
        var ben = new Customer { User = NewUser("Ben", "Lim", Roles.Customer) };
        w.Db.Customers.Add(ben);
        await w.Db.SaveChangesAsync();
        await BookAsync(w, ben, new DateOnly(2026, 11, 3), BookingStatuses.Pending, 800m, w.Pipe);
        await BookAsync(w, w.Ana, new DateOnly(2026, 11, 4), BookingStatuses.Pending, 500m, w.Sink);

        var d = (await w.Users.GetCustomerDetailsAsync(w.Admin.UserId, w.Ana.UserId)).Details!;

        Assert.Equal(1, d.TotalBookings);
        Assert.Equal("Sink Unclogging", Assert.Single(d.RecentBookings).ServiceTitle);
    }

    [Fact]
    public async Task OnlyCustomers_CanBeViewed_AndOnlyByAnActiveAdministrator()
    {
        var w = await WorldAsync();
        var suspendedAdmin = NewUser("Old", "Admin", Roles.Administrator, AccountStatuses.Suspended);
        w.Db.Users.Add(suspendedAdmin);
        await w.Db.SaveChangesAsync();

        Assert.Equal(CustomerDetailsOutcome.NotFound, (await w.Users.GetCustomerDetailsAsync(w.Admin.UserId, w.Marco.UserId)).Outcome);   // a provider
        Assert.Equal(CustomerDetailsOutcome.NotFound, (await w.Users.GetCustomerDetailsAsync(w.Admin.UserId, w.Admin.UserId)).Outcome);   // an admin
        Assert.Equal(CustomerDetailsOutcome.NotFound, (await w.Users.GetCustomerDetailsAsync(w.Admin.UserId, 99999)).Outcome);
        foreach (var who in new[] { w.Ana.UserId, w.Marco.UserId, suspendedAdmin.UserId })
            Assert.Equal(CustomerDetailsOutcome.NotAllowed, (await w.Users.GetCustomerDetailsAsync(who, w.Ana.UserId)).Outcome);
    }

    [Theory]
    [InlineData(AccountStatuses.Suspended, "pending")]
    [InlineData(AccountStatuses.Deactivated, "danger")]
    public async Task ASuspendedOrDeactivatedCustomer_ShowsTheirStatus_AndMissingDetailsAreEmpty(string status, string tone)
    {
        var w = await WorldAsync();
        var ben = new Customer { User = NewUser("Ben", "Lim", Roles.Customer, status) };
        w.Db.Customers.Add(ben);
        await w.Db.SaveChangesAsync();

        var d = (await w.Users.GetCustomerDetailsAsync(w.Admin.UserId, ben.UserId)).Details!;

        Assert.Equal(status, d.Status);
        Assert.Equal(tone, d.StatusTone);
        Assert.Null(d.Phone);
        Assert.Null(d.Address);
    }
}
