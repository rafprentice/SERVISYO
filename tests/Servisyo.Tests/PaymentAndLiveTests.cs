using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Records what would have been pushed to the browsers.
internal sealed class FakeLiveUpdates : IBookingLiveUpdates
{
    public List<(BookingChange Change, int[] Users)> Sent { get; } = new();

    public Task BookingChangedAsync(BookingChange change, params int[] userIds)
    {
        Sent.Add((change, userIds));
        return Task.CompletedTask;
    }

    public List<(int Receiver, int From, int Unread)> MessagesReceived { get; } = new();
    public List<(int User, int Unread)> MessagesRead { get; } = new();

    public Task MessageReceivedAsync(int receiverUserId, int fromUserId, int unreadTotal)
    {
        MessagesReceived.Add((receiverUserId, fromUserId, unreadTotal));
        return Task.CompletedTask;
    }

    public Task MessagesReadAsync(int userId, int unreadTotal)
    {
        MessagesRead.Add((userId, unreadTotal));
        return Task.CompletedTask;
    }
}

public class PaymentAndLiveTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime NowUtc = new(2026, 10, 26, 2, 0, 0);

    /// "Mock E-Wallet" with a test GCash number: paid at once (the cash choice is tested in DualPaymentTests).
    private static readonly PaymentChoiceForm TestWallet = new()
    {
        Method = PaymentMethods.MockEWallet, Wallet = MockWallets.GCash, MobileNumber = "0917-000-0000"
    };

    private sealed class World
    {
        public ServisyoDbContext Db { get; } = new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public FakeLiveUpdates Live { get; } = new();
        public Customer Ana { get; set; } = null!;
        public Customer Ben { get; set; } = null!;
        public Provider Mary { get; set; } = null!;
        public BookingPaymentService Payments => new(Db, new FixedClock(), Live);
        public ProviderBookingsService ProviderBookings => new(Db, new FixedClock(), null, Live);
        public CustomerBookingsService CustomerBookings => new(Db, new FixedClock(), null, Live);
    }

    private static User NewUser(string first, string role) =>
        new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = AccountStatuses.Active };

    private static async Task<World> CreateAsync()
    {
        var w = new World();
        w.Ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        w.Ben = new Customer { User = NewUser("Ben", Roles.Customer) };
        w.Mary = new Provider { BusinessName = "MaryCas", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Mary", Roles.ServiceProvider) };
        w.Db.AddRange(w.Ana, w.Ben, w.Mary);
        await w.Db.SaveChangesAsync();
        return w;
    }

    private static async Task<Booking> BookAsync(World w, Customer who, string status, string payment = PaymentStatuses.Unpaid)
    {
        var b = new Booking
        {
            CustomerId = who.CustomerId, ProviderId = w.Mary.ProviderId, BookingDate = new DateOnly(2026, 10, 30), BookingTime = new TimeOnly(14, 0),
            TotalDurationMinutes = 60, TotalFee = 650m, Status = status, PaymentStatus = payment,
            CreatedAt = NowUtc.AddDays(-1), UpdatedAt = NowUtc.AddDays(-1), RowVersion = new byte[] { 1 }
        };
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        return b;
    }

    // ------------------------------------------------------------------ Approve & Pay

    [Fact]
    public async Task ApproveAndPay_MarksTheQuotationPaid_TellsTheProvider_AndPushesALiveUpdate()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana, BookingStatuses.Accepted);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, TestWallet);

        Assert.True(result.Succeeded);
        var saved = await w.Db.Bookings.AsNoTracking().SingleAsync();
        Assert.Equal(BookingStatuses.Accepted, saved.Status);          // the six PDF statuses stay as they are
        Assert.Equal(PaymentStatuses.Paid, saved.PaymentStatus);
        Assert.Equal(NowUtc, saved.UpdatedAt);

        var note = await w.Db.Notifications.SingleAsync();
        Assert.Equal(w.Mary.UserId, note.UserId);
        Assert.Contains("₱650", note.Message);

        var (change, users) = Assert.Single(w.Live.Sent);
        Assert.Equal(b.BookingId, change.BookingId);
        Assert.Equal(PaymentStatuses.Paid, change.PaymentStatus);
        Assert.Contains(w.Mary.UserId, users);
        Assert.Contains(w.Ana.UserId, users);
    }

    [Fact]
    public async Task ApproveAndPay_MovesTheMeterFromQuotationToScheduled()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana, BookingStatuses.Accepted);
        Assert.Equal(DashboardState.QuotationReceived, DashboardStates.FromBooking(b.Status, b.PaymentStatus));

        await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, TestWallet);

        var saved = await w.Db.Bookings.AsNoTracking().SingleAsync();
        var state = DashboardStates.FromBooking(saved.Status, saved.PaymentStatus);
        Assert.Equal(DashboardState.BookingConfirmed, state);
        Assert.Equal(4, DashboardStates.Describe(state, saved.TotalFee, "MaryCas").ActiveStep);   // Requested, Quotation, Paid done; Scheduled now
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Rejected)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task OnlyAnAcceptedUnpaidBooking_CanBePaid(string status)
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana, status);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, TestWallet);

        Assert.Equal(PaymentOutcome.NotPayable, result.Outcome);
        Assert.Equal(PaymentStatuses.Unpaid, (await w.Db.Bookings.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(w.Db.Notifications);
        Assert.Empty(w.Live.Sent);
    }

    [Fact]
    public async Task PayingTwice_IsRefused_TheSecondTime()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana, BookingStatuses.Accepted);

        Assert.True((await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, TestWallet)).Succeeded);
        Assert.Equal(PaymentOutcome.AlreadyPaid, (await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, TestWallet)).Outcome);
        Assert.Single(w.Db.Notifications);
    }

    [Fact]
    public async Task SomeoneElsesBooking_CannotBePaid_AndLooksMissing()
    {
        var w = await CreateAsync();
        var anas = await BookAsync(w, w.Ana, BookingStatuses.Accepted);

        Assert.Equal(PaymentOutcome.NotFound, (await w.Payments.ApproveAndPayAsync(w.Ben.UserId, anas.BookingId, TestWallet)).Outcome);
        Assert.Equal(PaymentOutcome.NotAllowed, (await w.Payments.ApproveAndPayAsync(w.Mary.UserId, anas.BookingId, TestWallet)).Outcome);   // a provider
        Assert.Equal(PaymentStatuses.Unpaid, (await w.Db.Bookings.AsNoTracking().SingleAsync()).PaymentStatus);
    }

    // ------------------------------------------------------------------ provider side

    [Fact]
    public async Task Provider_CannotStart_BeforeTheCustomerPays_AndSeesNoStartButton()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana, BookingStatuses.Accepted);

        Assert.Empty(BookingActionInfo.ForBooking(BookingStatuses.Accepted, PaymentStatuses.Unpaid));
        var result = await w.ProviderBookings.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start);

        Assert.Equal(BookingActionOutcome.InvalidTransition, result.Outcome);
        Assert.Contains("pay", result.Message);
        Assert.Equal(BookingStatuses.Accepted, (await w.Db.Bookings.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task FullFlow_Quotation_Paid_InProgress_Completed_PushesEveryStepToTheCustomer()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana, BookingStatuses.Pending);

        Assert.True((await w.ProviderBookings.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Accept)).Succeeded);  // quotation
        Assert.True((await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, TestWallet)).Succeeded);                               // paid -> scheduled
        Assert.Single(BookingActionInfo.ForBooking(BookingStatuses.Accepted, PaymentStatuses.Paid));                            // "Mark In Progress"
        Assert.True((await w.ProviderBookings.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start)).Succeeded);   // in progress
        Assert.True((await w.ProviderBookings.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Complete)).Succeeded);// completed

        var saved = await w.Db.Bookings.AsNoTracking().SingleAsync();
        Assert.Equal(BookingStatuses.Completed, saved.Status);
        Assert.Equal(DashboardState.ServiceCompleted, DashboardStates.FromBooking(saved.Status, saved.PaymentStatus));

        Assert.Equal(4, w.Live.Sent.Count);
        Assert.All(w.Live.Sent, s => Assert.Contains(w.Ana.UserId, s.Users));
        Assert.All(w.Live.Sent, s => Assert.Equal(b.BookingId, s.Change.BookingId));
    }

    [Fact]
    public async Task CustomerCancel_PushesTheChangeToTheProvider()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana, BookingStatuses.Pending);

        await w.CustomerBookings.CancelAsync(w.Ana.UserId, b.BookingId);

        var (change, users) = Assert.Single(w.Live.Sent);
        Assert.Equal(BookingStatuses.Cancelled, change.Status);
        Assert.Contains(w.Mary.UserId, users);
    }

    [Fact]
    public async Task MyBookings_OffersApproveAndPay_OnlyForAnAcceptedUnpaidBooking()
    {
        var w = await CreateAsync();
        var quote = await BookAsync(w, w.Ana, BookingStatuses.Accepted);
        var paid = await BookAsync(w, w.Ana, BookingStatuses.Accepted, PaymentStatuses.Paid);
        var pending = await BookAsync(w, w.Ana, BookingStatuses.Pending);

        var page = (await w.CustomerBookings.GetAsync(w.Ana.UserId, null, 1)).Page!;
        var cards = page.Cards.ToDictionary(c => c.BookingId);

        Assert.True(cards[quote.BookingId].CanPay);
        Assert.Equal(650m, cards[quote.BookingId].Fee);
        Assert.False(cards[paid.BookingId].CanPay);
        Assert.True(cards[paid.BookingId].IsPaid);
        Assert.False(cards[pending.BookingId].CanPay);
    }
}
