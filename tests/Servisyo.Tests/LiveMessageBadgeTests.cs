using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class LiveMessageBadgeTests
{
    private static User NewUser(string first, string role) =>
        new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = AccountStatuses.Active };

    [Fact]
    public async Task Sending_PushesTheReceiversNewUnreadCount_AndReadingPushesItBackDown()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        var nina = new Provider { BusinessName = "Nina Santiago", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Nina", Roles.ServiceProvider) };
        db.AddRange(ana, nina);
        await db.SaveChangesAsync();
        db.Bookings.Add(new Booking
        {
            CustomerId = ana.CustomerId, ProviderId = nina.ProviderId, BookingDate = new DateOnly(2026, 10, 30), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 500m, Status = BookingStatuses.Accepted, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();

        var live = new FakeLiveUpdates();
        var svc = new MessagingService(db, null, null, live);

        Assert.Equal(SendOutcome.Sent, (await svc.SendAsync(ana.UserId, nina.UserId, "Hello po")).Outcome);
        Assert.Equal(SendOutcome.Sent, (await svc.SendAsync(ana.UserId, nina.UserId, "Are you coming at 9?")).Outcome);

        Assert.Equal(new[] { (nina.UserId, ana.UserId, 1), (nina.UserId, ana.UserId, 2) }, live.MessagesReceived.ToArray());

        await svc.MarkReadAsync(nina.UserId, ana.UserId);          // Nina opens the chat with Ana

        Assert.Equal((nina.UserId, 0), Assert.Single(live.MessagesRead));
        Assert.Equal(0, await db.Messages.CountAsync(m => m.ReceiverId == nina.UserId && !m.IsRead));
    }
}
