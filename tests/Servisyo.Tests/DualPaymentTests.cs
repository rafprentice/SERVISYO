using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Approve & Pay with two choices (Cash on Service Completion / Mock E-Wallet) and the provider's "Confirm Cash Received".
public class DualPaymentTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime NowUtc = new(2026, 10, 26, 2, 0, 0);

    /// Oct 30, 2026 2:05 PM in Cebu (UTC+8): inside the start window of the 2 PM bookings below (database v2.12).
    private sealed class StartClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 30, 6, 5, 0, TimeSpan.Zero);
    }

    private sealed class World
    {
        public ServisyoDbContext Db { get; } = new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public FakeLiveUpdates Live { get; } = new();
        public Customer Ana { get; set; } = null!;
        public Customer Ben { get; set; } = null!;
        public Provider Mary { get; set; } = null!;
        public Provider Juan { get; set; } = null!;
        public User Admin { get; set; } = null!;
        public BookingPaymentService Payments => new(Db, new FixedClock(), Live);
        public MemoryPhotoStore Photos { get; } = new();                // database v2.11: proof photos
        public ProviderBookingsService ProviderBookings => new(Db, new FixedClock(), null, Live, Photos);
        /// Database v2.12: a provider can only start a job inside its start window: this one presses "Mark In Progress"
        /// at 2:05 PM on Oct 30 (Philippine time), five minutes after the 2 PM bookings of these tests.
        public ProviderBookingsService StartingProvider => new(Db, new StartClock(), null, Live, Photos);
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
        w.Juan = new Provider { BusinessName = "Juan Fix", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Juan", Roles.ServiceProvider) };
        w.Admin = NewUser("Admin", Roles.Administrator);
        w.Db.AddRange(w.Ana, w.Ben, w.Mary, w.Juan);
        w.Db.Users.Add(w.Admin);
        await w.Db.SaveChangesAsync();
        return w;
    }

    private static async Task<Booking> BookAsync(World w, string status = BookingStatuses.Accepted, Customer? who = null, int hour = 14)
    {
        var b = new Booking
        {
            CustomerId = (who ?? w.Ana).CustomerId, ProviderId = w.Mary.ProviderId, BookingDate = new DateOnly(2026, 10, 30), BookingTime = new TimeOnly(hour, 0),
            TotalDurationMinutes = 60, TotalFee = 650m, Status = status, PaymentStatus = PaymentStatuses.Unpaid,
            CreatedAt = NowUtc.AddDays(-1), UpdatedAt = NowUtc.AddDays(-1), RowVersion = new byte[] { 1 }
        };
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        return b;
    }

    private static PaymentChoiceForm Cash => new() { Method = PaymentMethods.CashOnCompletion };

    private static PaymentChoiceForm Wallet(string wallet = MockWallets.GCash, string mobile = "0917-000-0000", string? reference = null) =>
        new() { Method = PaymentMethods.MockEWallet, Wallet = wallet, MobileNumber = mobile, Reference = reference };

    private static Task<PaymentDetail> PaymentOf(World w, int bookingId) =>
        w.Db.PaymentDetails.AsNoTracking().SingleAsync(p => p.BookingId == bookingId);

    /// v2.3: "collected" is not stored (CASH_PAYMENT_DETAILS is gone): a cash payment whose status is Paid.
    private static bool IsCollected(PaymentDetail p) => p.PaymentId == PaymentIds.Cash && p.PaymentStatus == PaymentRecordStatuses.Paid;

    private static async Task<(Booking Booking, string Effective)> ReloadAsync(World w, int bookingId)
    {
        var b = await w.Db.Bookings.AsNoTracking().Include(x => x.PaymentDetail).SingleAsync(x => x.BookingId == bookingId);
        return (b, PaymentProgress.Effective(b.PaymentStatus, b.PaymentDetail?.PaymentStatus));
    }

    // ================================================================== Option 1: Cash on Service Completion

    [Fact]
    public async Task Cash_CreatesAPendingCollectionPayment_AndSchedulesTheBooking()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Cash);

        Assert.True(result.Succeeded);
        Assert.Equal($"CASH-2026-{b.BookingId:D4}", result.Reference);

        var pay = await PaymentOf(w, b.BookingId);
        Assert.Equal(PaymentIds.Cash, pay.PaymentId);                    // PAYMENTS row 1: Cash on Completion
        Assert.Equal(PaymentRecordStatuses.PendingCollection, pay.PaymentStatus);
        Assert.Equal(result.Reference, pay.TransactionReference);
        Assert.Equal(NowUtc, pay.CreatedAt);
        Assert.Null(pay.PaidAt);
        Assert.False(IsCollected(pay));
        // v2.3: the amount is not stored again; the booking's TOTAL_FEE (650) is the amount

        var (saved, effective) = await ReloadAsync(w, b.BookingId);
        Assert.Equal(BookingStatuses.Accepted, saved.Status);
        Assert.Equal(PaymentStatuses.Unpaid, saved.PaymentStatus);       // the PDF column: no money yet
        Assert.Equal(NowUtc, saved.UpdatedAt);

        // the meter: Requested, Quotation, Paid done -> Scheduled now
        var state = DashboardStates.FromBooking(saved.Status, effective);
        Assert.Equal(DashboardState.BookingConfirmed, state);
        var info = DashboardStates.Describe(state, saved.TotalFee, "MaryCas", cashOnCompletion: true);
        Assert.Equal(4, info.ActiveStep);
        Assert.Contains("in cash", info.Message);

        // database v2.3: no NOTIFICATIONS row; the bell is worked out by usp_GetNotifications (SQL self-test 10/30)

        var (change, users) = Assert.Single(w.Live.Sent);
        Assert.Equal(PaymentRecordStatuses.PendingCollection, change.PaymentStatus);
        Assert.Contains(w.Ana.UserId, users);
        Assert.Contains(w.Mary.UserId, users);
    }

    [Fact]
    public async Task Cash_LetsTheProviderStart_AndBothSidesSeeTheCashLine()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);
        await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Cash);

        Assert.Single(BookingActionInfo.ForBooking(BookingStatuses.Accepted, PaymentRecordStatuses.PendingCollection));   // "Mark In Progress"
        var providerCard = (await w.ProviderBookings.GetAsync(w.Mary.UserId, null, 1)).Page!.Cards.Single();
        Assert.True(providerCard.IsScheduled);
        Assert.False(providerCard.AwaitingPayment);
        Assert.False(providerCard.CanConfirmCash);                        // not completed yet
        Assert.Contains("Collect ₱650", providerCard.Payment!.Text);

        var customerCard = (await w.CustomerBookings.GetAsync(w.Ana.UserId, null, 1)).Page!.Cards.Single();
        Assert.False(customerCard.CanPay);                                // already chosen
        Assert.False(customerCard.IsPaid);
        Assert.StartsWith("Cash on completion", customerCard.Payment!.Text);
        Assert.Equal($"CASH-2026-{b.BookingId:D4}", customerCard.Payment.Reference);

        Assert.True((await w.StartingProvider.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start)).Succeeded);
    }

    // ================================================================== Option 2: Mock E-Wallet

    [Fact]
    public async Task Wallet_IsPaidAtOnce_AndKeepsAValidTestReference()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Wallet(reference: "GCASH-12345678"));

        Assert.True(result.Succeeded);
        Assert.Equal("GCASH-12345678", result.Reference);
        var pay = await PaymentOf(w, b.BookingId);
        Assert.Equal(PaymentIds.GCash, pay.PaymentId);                   // PAYMENTS row 2: GCash
        Assert.Equal(PaymentRecordStatuses.Paid, pay.PaymentStatus);
        Assert.Equal(NowUtc, pay.PaidAt);
        Assert.False(IsCollected(pay));                                  // not cash

        var (saved, effective) = await ReloadAsync(w, b.BookingId);
        Assert.Equal(PaymentStatuses.Paid, saved.PaymentStatus);
        Assert.Equal(DashboardState.BookingConfirmed, DashboardStates.FromBooking(saved.Status, effective));
        Assert.Equal(PaymentStatuses.Paid, Assert.Single(w.Live.Sent).Change.PaymentStatus);

        var card = (await w.CustomerBookings.GetAsync(w.Ana.UserId, null, 1)).Page!.Cards.Single();
        Assert.True(card.IsPaid);
        Assert.StartsWith("Paid via GCash (test)", card.Payment!.Text);
    }

    [Fact]
    public async Task Maya_GetsAMayaReference()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Wallet(MockWallets.Maya));

        Assert.Matches(new Regex("^MAYA-[0-9]{8}$"), result.Reference!);
        Assert.Equal(PaymentIds.Maya, (await PaymentOf(w, b.BookingId)).PaymentId);     // PAYMENTS row 3: Maya
    }

    [Theory]
    [InlineData("")]
    [InlineData("MAYA-12345678")]          // the other wallet's prefix
    [InlineData("GCASH-123")]
    [InlineData("GCASH-1234567X")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Wallet_ReplacesAMissingOrBadReference_WithOneMadeByTheServer(string reference)
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Wallet(reference: reference));

        Assert.True(result.Succeeded);
        Assert.Matches(new Regex("^GCASH-[0-9]{8}$"), result.Reference!);
        Assert.NotEqual(reference, result.Reference);
        Assert.Equal(result.Reference, (await PaymentOf(w, b.BookingId)).TransactionReference);
    }

    [Fact]
    public async Task Wallet_WithNoReference_GetsOneMadeByTheServer()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Wallet(reference: null));

        Assert.Matches(new Regex("^GCASH-[0-9]{8}$"), result.Reference!);
    }

    [Fact]
    public async Task Wallet_AReferenceAlreadyUsed_IsReplaced()
    {
        var w = await CreateAsync();
        var first = await BookAsync(w, hour: 9);
        var second = await BookAsync(w, hour: 14);

        await w.Payments.ApproveAndPayAsync(w.Ana.UserId, first.BookingId, Wallet(reference: "GCASH-11112222"));
        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, second.BookingId, Wallet(reference: "GCASH-11112222"));

        Assert.True(result.Succeeded);
        Assert.NotEqual("GCASH-11112222", result.Reference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("0817-000-0000")]          // not a mobile number
    [InlineData("0917-000-000")]           // one digit short
    [InlineData("abcdefghijk")]
    public async Task Wallet_NeedsAValidMobileNumber_AndNothingIsSaved(string mobile)
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Wallet(mobile: mobile));

        Assert.Equal(PaymentOutcome.InvalidInput, result.Outcome);
        Assert.Empty(w.Db.PaymentDetails);
        Assert.Empty(w.Live.Sent);
        Assert.Equal(PaymentStatuses.Unpaid, (await w.Db.Bookings.AsNoTracking().SingleAsync()).PaymentStatus);
    }

    [Theory]
    [InlineData("09170000000")]
    [InlineData("0917 000 0000")]
    [InlineData("0917-000-0000")]
    [InlineData("+639170000000")]
    public void MobileNumber_AcceptsTheUsualPhilippineForms(string mobile) => Assert.True(MockWallets.IsMobileNumber(mobile));

    [Theory]
    [InlineData(null, null)]
    [InlineData("Bitcoin", null)]
    [InlineData("cashoncompletion", null)]                       // exact values only
    [InlineData(PaymentMethods.MockEWallet, "PayPal")]
    [InlineData(PaymentMethods.MockEWallet, null)]
    public async Task UnknownMethodOrWallet_IsRefused(string? method, string? wallet)
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);

        var result = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId,
            new PaymentChoiceForm { Method = method, Wallet = wallet, MobileNumber = "0917-000-0000" });

        Assert.Equal(PaymentOutcome.InvalidInput, result.Outcome);
        Assert.Empty(w.Db.PaymentDetails);
    }

    [Fact]
    public async Task ChoosingAgain_AfterCash_IsRefused_AndThereIsStillOnePayment()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);

        Assert.True((await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Cash)).Succeeded);
        var again = await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Wallet());

        Assert.Equal(PaymentOutcome.AlreadyPaid, again.Outcome);
        Assert.Contains("cash", again.Message);
        Assert.Single(w.Db.PaymentDetails);
    }

    [Fact]
    public async Task OnlyTheOwnerCanPay_AndNotBeforeTheQuotation()
    {
        var w = await CreateAsync();
        var anas = await BookAsync(w);
        var pending = await BookAsync(w, BookingStatuses.Pending, hour: 9);

        Assert.Equal(PaymentOutcome.NotFound, (await w.Payments.ApproveAndPayAsync(w.Ben.UserId, anas.BookingId, Cash)).Outcome);
        Assert.Equal(PaymentOutcome.NotAllowed, (await w.Payments.ApproveAndPayAsync(w.Mary.UserId, anas.BookingId, Cash)).Outcome);
        Assert.Equal(PaymentOutcome.NotPayable, (await w.Payments.ApproveAndPayAsync(w.Ana.UserId, pending.BookingId, Cash)).Outcome);
        Assert.Empty(w.Db.PaymentDetails);
    }

    // ================================================================== provider: Confirm Cash Received

    private static async Task<Booking> CompletedCashJobAsync(World w)
    {
        var b = await BookAsync(w);
        Assert.True((await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Cash)).Succeeded);
        Assert.True((await w.StartingProvider.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start)).Succeeded);
        var done = await w.ProviderBookings.CompleteAsync(w.Mary.UserId, b.BookingId, CompletionTestKit.Message, CompletionTestKit.Photo());
        Assert.True(done.Succeeded);
        Assert.Contains("Confirm Cash Received", done.Message);
        return b;
    }

    [Fact]
    public async Task CompletedCashJob_ShowsConfirmCashReceived_OnTheCard_AndOnTheDashboard()
    {
        var w = await CreateAsync();
        var b = await CompletedCashJobAsync(w);

        var card = (await w.ProviderBookings.GetAsync(w.Mary.UserId, "completed", 1)).Page!.Cards.Single();
        Assert.True(card.CanConfirmCash);
        Assert.Contains("Confirm Cash Received", card.Payment!.Text);

        var dash = (await new ProviderDashboardService(w.Db, new FixedClock()).GetAsync(w.Mary.UserId)).Dashboard!;
        Assert.Contains(dash.Attention, a => a.ActionLabel == "Confirm Cash" && a.Text.Contains("₱650"));

        var admin = (await new BookingMonitorService(w.Db).GetBookingsAsync(w.Admin.UserId, new BookingListQuery()))!;
        Assert.Equal("Cash due", admin.Rows.Single(r => r.BookingId == b.BookingId).Payment.Text);
    }

    [Fact]
    public async Task ConfirmCash_MarksItCollected_AndTheBookingPaid_AndTellsTheCustomer()
    {
        var w = await CreateAsync();
        var b = await CompletedCashJobAsync(w);
        w.Live.Sent.Clear();

        var result = await w.Payments.ConfirmCashReceivedAsync(w.Mary.UserId, b.BookingId);

        Assert.True(result.Succeeded);
        var pay = await PaymentOf(w, b.BookingId);
        Assert.True(IsCollected(pay));
        Assert.Equal(PaymentRecordStatuses.Paid, pay.PaymentStatus);
        Assert.Equal(NowUtc, pay.PaidAt);                                 // collected at = PAID_AT

        var (saved, _) = await ReloadAsync(w, b.BookingId);
        Assert.Equal(BookingStatuses.Completed, saved.Status);
        Assert.Equal(PaymentStatuses.Paid, saved.PaymentStatus);
        Assert.Equal(pay.PaidAt, saved.UpdatedAt);                        // one moment: the customer's bell shows the cash as the latest news
        // database v2.3: no NOTIFICATIONS row; the bell is worked out by usp_GetNotifications (SQL self-test 10/30)

        var (change, users) = Assert.Single(w.Live.Sent);
        Assert.Equal("cash-received", change.What);
        Assert.Contains(w.Ana.UserId, users);

        var providerCard = (await w.ProviderBookings.GetAsync(w.Mary.UserId, "completed", 1)).Page!.Cards.Single();
        Assert.False(providerCard.CanConfirmCash);
        Assert.StartsWith("Cash received", providerCard.Payment!.Text);
        var customerCard = (await w.CustomerBookings.GetAsync(w.Ana.UserId, null, 1)).Page!.Cards.Single();
        Assert.True(customerCard.IsPaid);
        Assert.StartsWith("Paid in cash", customerCard.Payment!.Text);
    }

    [Fact]
    public async Task ConfirmCash_Twice_IsRefused_TheSecondTime()
    {
        var w = await CreateAsync();
        var b = await CompletedCashJobAsync(w);

        Assert.True((await w.Payments.ConfirmCashReceivedAsync(w.Mary.UserId, b.BookingId)).Succeeded);
        var again = await w.Payments.ConfirmCashReceivedAsync(w.Mary.UserId, b.BookingId);

        Assert.Equal(BookingActionOutcome.InvalidTransition, again.Outcome);
        Assert.Contains("already", again.Message);
    }

    [Theory]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task ConfirmCash_BeforeTheJobIsCompleted_IsRefused(string status)
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);
        await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Cash);
        if (status == BookingStatuses.InProgress)
            await w.StartingProvider.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start);

        var result = await w.Payments.ConfirmCashReceivedAsync(w.Mary.UserId, b.BookingId);

        Assert.Equal(BookingActionOutcome.InvalidTransition, result.Outcome);
        Assert.False(IsCollected(await PaymentOf(w, b.BookingId)));
    }

    [Fact]
    public async Task ConfirmCash_ForAnEWalletBooking_IsRefused()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);
        await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Wallet());
        await w.StartingProvider.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start);
        await w.ProviderBookings.CompleteAsync(w.Mary.UserId, b.BookingId, CompletionTestKit.Message, CompletionTestKit.Photo());

        var result = await w.Payments.ConfirmCashReceivedAsync(w.Mary.UserId, b.BookingId);

        Assert.Equal(BookingActionOutcome.InvalidTransition, result.Outcome);
        Assert.False((await w.ProviderBookings.GetAsync(w.Mary.UserId, null, 1)).Page!.Cards.Single().CanConfirmCash);
    }

    [Fact]
    public async Task ConfirmCash_OnlyTheAssignedProvider()
    {
        var w = await CreateAsync();
        var b = await CompletedCashJobAsync(w);

        var other = await w.Payments.ConfirmCashReceivedAsync(w.Juan.UserId, b.BookingId);
        Assert.Equal(ProviderDashboardAccess.Allowed, other.Access);
        Assert.Equal(BookingActionOutcome.NotFound, other.Outcome);          // someone else's booking looks missing

        var customer = await w.Payments.ConfirmCashReceivedAsync(w.Ana.UserId, b.BookingId);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, customer.Access);

        Assert.False(IsCollected(await PaymentOf(w, b.BookingId)));
    }

    [Fact]
    public async Task CustomerDashboard_ShowsScheduled_WithTheCashLine()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w);
        await w.Payments.ApproveAndPayAsync(w.Ana.UserId, b.BookingId, Cash);

        var vm = (await new CustomerDashboardService(w.Db, new FixedClock()).GetAsync(w.Ana.UserId))!;

        Assert.Equal(DashboardState.BookingConfirmed, vm.State);
        Assert.True(vm.Current!.CashToCollect);
        Assert.StartsWith("Cash on completion", vm.Current.Payment!.Text);
    }

    [Fact]
    public void PaymentDisplay_ShowsCashDue_ForCashNotCollectedYet()
    {
        var d = PaymentDisplay.For(PaymentProgress.Effective(PaymentStatuses.Unpaid, PaymentRecordStatuses.PendingCollection));
        Assert.Equal("Cash due", d.Text);
        Assert.Equal("pending", d.Tone);
        Assert.Equal(PaymentStatuses.Paid, PaymentProgress.Effective(PaymentStatuses.Paid, PaymentRecordStatuses.Paid));
        Assert.Equal(PaymentStatuses.Unpaid, PaymentProgress.Effective(PaymentStatuses.Unpaid, null));
    }
}
