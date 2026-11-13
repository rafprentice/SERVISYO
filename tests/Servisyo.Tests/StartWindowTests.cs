using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Controllers;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Tests;

/// Database v2.12 ("StartWindow"): "Mark In Progress" only inside the start window, in Philippine time:
///   before the scheduled time - no button, a notice; 0-15 minutes after - on time; 16-120 - late, a reason is required;
///   more than 120 - missed: refused, and the no-show job cancels a paid (or cash) booking and refunds a test e-wallet.
/// The SQL side (the CHECK, the triggers, the procedures) is tested by the self-test in DatabaseDesign-v2.sql (38-45).
public class StartWindowTests
{
    /// A clock the test can move. Times are given in Cebu time (UTC+8), as people read them.
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
        public void SetManila(int year, int month, int day, int hour, int minute, int second = 0) =>
            Now = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(8)).ToUniversalTime();
    }

    /// Every booking of these tests: Nov 27, 2026, 9:30 AM (the one in the request's screenshot).
    private static readonly DateOnly Day = new(2026, 11, 27);
    private static readonly TimeOnly At = new(9, 30);

    private sealed class World
    {
        public ServisyoDbContext Db { get; } = new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public TestClock Clock { get; } = new();
        public FakeLiveUpdates Live { get; } = new();
        public Customer Ana { get; set; } = null!;
        public Provider Mary { get; set; } = null!;
        public Provider Juan { get; set; } = null!;
        public ProviderBookingsService Provider => new(Db, Clock, null, Live, new MemoryPhotoStore());
        public CustomerBookingsService Customer => new(Db, Clock, null, Live);
        public NoShowService NoShow => new(Db, Clock, null, Live);

        /// The provider presses the button at this Cebu time (minutes after 9:30 AM on Nov 27; negative = before).
        public void MinutesAfter(double minutes) =>
            Clock.Now = new DateTimeOffset(Day.ToDateTime(At), TimeSpan.FromHours(8)).ToUniversalTime().AddMinutes(minutes);
    }

    private static User NewUser(string first, string role) =>
        new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = AccountStatuses.Active };

    private static async Task<World> CreateAsync()
    {
        var w = new World();
        w.Ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        w.Mary = new Provider { BusinessName = "MaryCas", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Mary", Roles.ServiceProvider) };
        w.Juan = new Provider { BusinessName = "Juan Fix", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Juan", Roles.ServiceProvider) };
        w.Db.AddRange(w.Ana, w.Mary, w.Juan);
        await w.Db.SaveChangesAsync();
        w.MinutesAfter(-60 * 24);
        return w;
    }

    /// An Accepted booking at 9:30 AM on Nov 27: paid by test GCash (default), cash on completion promised, or unpaid.
    private static async Task<Booking> ScheduledAsync(World w, string pay = "gcash", string status = BookingStatuses.Accepted)
    {
        var b = new Booking
        {
            CustomerId = w.Ana.CustomerId, ProviderId = w.Mary.ProviderId, BookingDate = Day, BookingTime = At,
            TotalDurationMinutes = 60, TotalFee = 650m, Status = status,
            PaymentStatus = pay == "gcash" ? PaymentStatuses.Paid : PaymentStatuses.Unpaid,
            CreatedAt = new DateTime(2026, 11, 20, 1, 0, 0), UpdatedAt = new DateTime(2026, 11, 20, 1, 0, 0), RowVersion = new byte[] { 1 }
        };
        if (pay == "gcash")
            b.PaymentDetail = new PaymentDetail { PaymentId = PaymentIds.GCash, PaymentStatus = PaymentRecordStatuses.Paid, TransactionReference = "GCASH-12345678",
                                                  CreatedAt = b.CreatedAt, PaidAt = b.CreatedAt };
        else if (pay == "cash")
            b.PaymentDetail = new PaymentDetail { PaymentId = PaymentIds.Cash, PaymentStatus = PaymentRecordStatuses.PendingCollection,
                                                  TransactionReference = "CASH-2026-0033", CreatedAt = b.CreatedAt };
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();
        return b;
    }

    private static async Task<Booking> Saved(World w, int id) =>
        await w.Db.Bookings.AsNoTracking().Include(b => b.PaymentDetail).SingleAsync(b => b.BookingId == id);

    // ================================================================== the rules

    [Theory]
    [InlineData(-1, StartWindowState.TooEarly)]
    [InlineData(-600, StartWindowState.TooEarly)]
    [InlineData(0, StartWindowState.OnTime)]
    [InlineData(15, StartWindowState.OnTime)]
    [InlineData(16, StartWindowState.Late)]
    [InlineData(120, StartWindowState.Late)]
    [InlineData(121, StartWindowState.Expired)]
    public void TheWindow_HasTheFourStates(int minutesAfter, StartWindowState expected) =>
        Assert.Equal(expected, StartRules.StateAt(minutesAfter));

    [Fact]
    public void Minutes_AreCountedLikeSqlServerDateDiff()
    {
        var scheduled = new DateTime(2026, 11, 27, 9, 30, 0);
        Assert.Equal(0, StartRules.MinutesBetween(scheduled, scheduled.AddSeconds(59)));
        Assert.Equal(15, StartRules.MinutesBetween(scheduled, new DateTime(2026, 11, 27, 9, 45, 59)));      // still on time
        Assert.Equal(16, StartRules.MinutesBetween(scheduled, new DateTime(2026, 11, 27, 9, 46, 0)));       // late from here
        Assert.Equal(-1, StartRules.MinutesBetween(scheduled, new DateTime(2026, 11, 27, 9, 29, 30)));      // too early
        Assert.Equal(121, StartRules.MinutesBetween(scheduled, new DateTime(2026, 11, 27, 11, 31, 0)));     // missed
        Assert.Equal(StartRules.WindowMinutes + 1, StartRules.NoShowAfterMinutes);
    }

    [Fact]
    public void PhilippineTime_IsUtcPlus8()
    {
        Assert.Equal(TimeSpan.FromHours(8), PhilippineTime.Zone.GetUtcOffset(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(8), PhilippineTime.Zone.GetUtcOffset(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));   // no daylight saving
        Assert.Equal(new DateTime(2026, 11, 27, 9, 30, 0), PhilippineTime.FromUtc(new DateTime(2026, 11, 27, 1, 30, 0)));
        Assert.Equal(new DateTime(2026, 11, 28, 0, 15, 0), PhilippineTime.FromUtc(new DateTime(2026, 11, 27, 16, 15, 0)));   // the next day in Cebu
    }

    [Fact]
    public void NextChange_IsTheScheduledTime_Then16_Then121MinutesAfter()
    {
        var s = Day.ToDateTime(At);
        Assert.Equal(s, StartRules.NextChange(Day, At, s.AddHours(-3)));
        Assert.Equal(s.AddMinutes(16), StartRules.NextChange(Day, At, s.AddMinutes(3)));
        Assert.Equal(s.AddMinutes(121), StartRules.NextChange(Day, At, s.AddMinutes(40)));
        Assert.Null(StartRules.NextChange(Day, At, s.AddMinutes(200)));
    }

    [Fact]
    public void TheTooEarlyNotice_IsTheRequestedText()
    {
        var w = StartWindowViewModel.For(Day, At, Day.ToDateTime(At).AddHours(-26));
        Assert.Equal(StartWindowState.TooEarly, w.State);
        Assert.Equal("Scheduled for Nov 27, 2026 · 09:30 AM. Start button unlocks at scheduled time.", w.Notice);
        Assert.Equal(26 * 3600 + 2, w.RefreshInSeconds);                // redraws itself just after 9:30 AM
        Assert.False(w.CanStart);
        Assert.Null(w.LateText);
    }

    [Fact]
    public void TheButtons_FollowTheWindow()
    {
        Assert.Empty(BookingActionInfo.ForStart(StartWindowState.TooEarly));
        Assert.Equal(new[] { "Mark In Progress" }, BookingActionInfo.ForStart(StartWindowState.OnTime).Select(a => a.Label));
        Assert.Equal("sv-btn--primary", BookingActionInfo.Start.Css);                                       // blue
        Assert.Equal(new[] { "Mark In Progress (Late)" }, BookingActionInfo.ForStart(StartWindowState.Late).Select(a => a.Label));
        Assert.Equal("sv-btn--late", BookingActionInfo.StartLate.Css);                                      // amber
        Assert.Equal(BookingAction.Start, BookingActionInfo.StartLate.Action);
        Assert.Empty(BookingActionInfo.ForStart(StartWindowState.Expired));
    }

    [Fact]
    public void ANoShow_IsACancelledBookingWithAPayment()
    {
        Assert.True(StartRules.IsNoShow(BookingStatuses.Cancelled, hasPayment: true));
        Assert.False(StartRules.IsNoShow(BookingStatuses.Cancelled, hasPayment: false));     // the customer cancelled a request
        Assert.False(StartRules.IsNoShow(BookingStatuses.Accepted, hasPayment: true));
        Assert.Equal("Provider No-Show — missed scheduled start window", StartRules.NoShowReason);
    }

    // ================================================================== StartAsync (the server decides)

    [Fact]
    public async Task TooEarly_IsRefused_AndNothingChanges()
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        w.MinutesAfter(-0.5);                                           // 9:29:30 AM

        var result = await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, "early bird");

        Assert.Equal(BookingActionOutcome.TooEarly, result.Outcome);
        Assert.Equal("Cannot start a booking before its scheduled date and time.", result.Message);
        var saved = await Saved(w, b.BookingId);
        Assert.Equal(BookingStatuses.Accepted, saved.Status);
        Assert.Null(saved.StartedAt);
        Assert.Empty(w.Live.Sent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15.9)]                                                   // 9:45:54 AM: still minute 15
    public async Task OnTime_Starts_SavesNow_AndKeepsNoReason(double minutes)
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w, "cash");
        w.MinutesAfter(minutes);

        var result = await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, "  no reason needed  ");

        Assert.True(result.Succeeded);
        Assert.Equal($"Booking #{b.BookingId} is now in progress.", result.Message);
        var saved = await Saved(w, b.BookingId);
        Assert.Equal(BookingStatuses.InProgress, saved.Status);
        Assert.Equal(w.Clock.Now.UtcDateTime, saved.StartedAt);         // ONE reading of the clock, saved in UTC
        Assert.Equal(w.Clock.Now.UtcDateTime, saved.UpdatedAt);
        Assert.Null(saved.LateReason);                                   // on time: the CHECK allows no reason
        Assert.Null(StartRules.MinutesLate(saved.BookingDate, saved.BookingTime, saved.StartedAt));
        var (change, users) = Assert.Single(w.Live.Sent);
        Assert.Equal("start", change.What);
        Assert.Contains(w.Ana.UserId, users);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public async Task Late_WithoutAReason_IsRefused(string? reason)
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        w.MinutesAfter(16);                                             // 9:46 AM

        var result = await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, reason);

        Assert.Equal(BookingActionOutcome.LateReasonRequired, result.Outcome);
        Assert.Equal(StartRules.LateReasonRequired, result.Message);
        Assert.Equal(BookingStatuses.Accepted, (await Saved(w, b.BookingId)).Status);
    }

    [Fact]
    public async Task Late_WithAReasonOver500Characters_IsRefused()
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        w.MinutesAfter(30);

        var result = await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, new string('x', 501));

        Assert.Equal(BookingActionOutcome.LateReasonRequired, result.Outcome);
        Assert.Equal("Please keep the late reason to 500 characters or fewer.", result.Message);
        Assert.True((await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, new string('x', 500))).Succeeded);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(25)]
    [InlineData(120.5)]                                                  // 11:30:30 AM: still minute 120
    public async Task Late_WithAReason_Starts_AndSavesTheReason(double minutes)
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        w.MinutesAfter(minutes);

        var result = await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, "  Heavy traffic on the bridge.\r\nArrived 10:05.  ");

        Assert.True(result.Succeeded);
        var whole = (int)Math.Floor(minutes);
        Assert.Contains($"started {whole} minutes late", result.Message);
        var saved = await Saved(w, b.BookingId);
        Assert.Equal(BookingStatuses.InProgress, saved.Status);
        Assert.Equal("Heavy traffic on the bridge.\nArrived 10:05.", saved.LateReason);    // trimmed, one kind of line break
        Assert.Equal(whole, StartRules.MinutesLate(saved.BookingDate, saved.BookingTime, saved.StartedAt));
    }

    [Theory]
    [InlineData(121)]
    [InlineData(60 * 24)]
    public async Task MoreThanTwoHoursLate_IsRefused_TheWindowHasExpired(double minutes)
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        w.MinutesAfter(minutes);

        var result = await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, "Sorry, I overslept.");

        Assert.Equal(BookingActionOutcome.StartWindowExpired, result.Outcome);
        Assert.Equal("Booking start window has expired.", result.Message);
        Assert.Equal(BookingStatuses.Accepted, (await Saved(w, b.BookingId)).Status);
    }

    [Fact]
    public async Task TheOldButton_ChangeStatusStart_FollowsTheSameRules()
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);

        w.MinutesAfter(-10);
        Assert.Equal(BookingActionOutcome.TooEarly, (await w.Provider.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start)).Outcome);
        w.MinutesAfter(20);
        Assert.Equal(BookingActionOutcome.LateReasonRequired, (await w.Provider.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start)).Outcome);
        w.MinutesAfter(10);
        Assert.True((await w.Provider.ChangeStatusAsync(w.Mary.UserId, b.BookingId, BookingAction.Start)).Succeeded);
    }

    [Fact]
    public async Task OnlyTheAssignedProvider_ACustomerWhoPaid_AndAnAcceptedBooking_CanStart()
    {
        var w = await CreateAsync();
        var paid = await ScheduledAsync(w);
        var unpaid = await ScheduledAsync(w, "none");
        w.MinutesAfter(5);

        Assert.Equal(BookingActionOutcome.NotFound, (await w.Provider.StartAsync(w.Juan.UserId, paid.BookingId, null)).Outcome);
        Assert.Equal(BookingActionOutcome.NotFound, (await w.Provider.StartAsync(w.Ana.UserId, paid.BookingId, null)).Outcome);   // a customer
        var notPaid = await w.Provider.StartAsync(w.Mary.UserId, unpaid.BookingId, null);
        Assert.Equal(BookingActionOutcome.InvalidTransition, notPaid.Outcome);
        Assert.Contains("pay", notPaid.Message);

        Assert.True((await w.Provider.StartAsync(w.Mary.UserId, paid.BookingId, null)).Succeeded);
        var again = await w.Provider.StartAsync(w.Mary.UserId, paid.BookingId, null);              // double click
        Assert.Equal(BookingActionOutcome.InvalidTransition, again.Outcome);
        Assert.Contains("in progress", again.Message);
    }

    // ================================================================== the provider's card

    [Fact]
    public async Task TheCard_ShowsTheNotice_TheBlueButton_TheAmberButton_OrTheMissedNotice()
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        async Task<ProviderBookingCardViewModel> Card() => (await w.Provider.GetAsync(w.Mary.UserId, null, 1)).Page!.Cards.Single();

        w.MinutesAfter(-90);
        var early = await Card();
        Assert.Empty(early.Actions);
        Assert.Equal(StartWindowState.TooEarly, early.StartWindow!.State);
        Assert.Equal("Scheduled for Nov 27, 2026 · 09:30 AM. Start button unlocks at scheduled time.", early.StartWindow.Notice);
        Assert.Equal(90 * 60 + 2, early.StartWindow.RefreshInSeconds);

        w.MinutesAfter(3);
        var onTime = await Card();
        Assert.Equal(new[] { "Mark In Progress" }, onTime.Actions.Select(a => a.Label));
        Assert.Null(onTime.StartWindow!.Notice);
        Assert.Equal(13 * 60 + 2, onTime.StartWindow.RefreshInSeconds);                       // turns late at 9:46 AM

        w.MinutesAfter(42);
        var late = await Card();
        Assert.Equal(new[] { "Mark In Progress (Late)" }, late.Actions.Select(a => a.Label));
        Assert.Equal("42 minutes late", late.StartWindow!.LateText);

        w.MinutesAfter(125);
        var missed = await Card();
        Assert.Empty(missed.Actions);
        Assert.Equal(StartWindowState.Expired, missed.StartWindow!.State);
        Assert.Contains("Provider No-Show", missed.StartWindow.Notice);
        Assert.Null(missed.StartWindow.RefreshInSeconds);
    }

    [Fact]
    public async Task AnUnpaidQuotation_HasNoStartWindow_AndNoButton()
    {
        var w = await CreateAsync();
        await ScheduledAsync(w, "none");
        w.MinutesAfter(5);

        var card = (await w.Provider.GetAsync(w.Mary.UserId, null, 1)).Page!.Cards.Single();

        Assert.True(card.AwaitingPayment);
        Assert.Null(card.StartWindow);
        Assert.Empty(card.Actions);
    }

    [Fact]
    public async Task ALateStart_IsShownToTheProvider_TheCustomer_AndTheAdministrator()
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        w.MinutesAfter(27);
        Assert.True((await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, "Flat tire on the way.")).Succeeded);

        var providerInfo = (await w.Provider.GetAsync(w.Mary.UserId, null, 1)).Page!.Cards.Single().StartInfo!;
        Assert.True(providerInfo.IsLate);
        Assert.Equal(27, providerInfo.MinutesLate);
        Assert.Equal("27 minutes late", providerInfo.LateText);
        Assert.Equal("Flat tire on the way.", providerInfo.LateReason);
        Assert.Equal("Nov 27, 2026 · 9:57 AM", providerInfo.StartedText);       // Cebu time

        var customerCard = (await w.Customer.GetAsync(w.Ana.UserId, null, 1)).Page!.Cards.Single();
        Assert.Equal("Flat tire on the way.", customerCard.StartInfo!.LateReason);
        Assert.False(customerCard.IsNoShow);

        var admin = NewUser("Ada", Roles.Administrator);
        w.Db.Users.Add(admin);
        await w.Db.SaveChangesAsync();
        var details = (await new BookingMonitorService(w.Db).GetDetailsAsync(admin.UserId, b.BookingId)).Details!;
        Assert.Equal(27, details.StartInfo!.MinutesLate);
        Assert.False(details.IsNoShow);
    }

    [Fact]
    public async Task AnOnTimeStart_SaysSo_WithoutAReason()
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        w.MinutesAfter(2);
        await w.Provider.StartAsync(w.Mary.UserId, b.BookingId, null);

        var info = (await w.Customer.GetAsync(w.Ana.UserId, null, 1)).Page!.Cards.Single().StartInfo!;

        Assert.False(info.IsLate);
        Assert.Null(info.LateReason);
        Assert.Equal("Nov 27, 2026 · 9:32 AM", info.StartedText);
    }

    // ================================================================== the no-show job

    [Fact]
    public async Task ANoShow_IsCancelled_AndTheTestPaymentRefunded_AndBothSidesAreTold()
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w);
        w.MinutesAfter(122);

        Assert.Equal(1, await w.NoShow.CancelMissedStartsAsync());

        var saved = await Saved(w, b.BookingId);
        Assert.Equal(BookingStatuses.Cancelled, saved.Status);
        Assert.Equal(PaymentStatuses.Refunded, saved.PaymentStatus);
        Assert.Equal(PaymentRecordStatuses.Refunded, saved.PaymentDetail!.PaymentStatus);
        Assert.Equal(w.Clock.Now.UtcDateTime, saved.UpdatedAt);
        Assert.Null(saved.StartedAt);
        var (change, users) = Assert.Single(w.Live.Sent);
        Assert.Equal("no-show", change.What);
        Assert.Equal(new[] { w.Ana.UserId, w.Mary.UserId }, users);

        var customerCard = (await w.Customer.GetAsync(w.Ana.UserId, null, 1)).Page!.Cards.Single();
        Assert.True(customerCard.IsNoShow);
        Assert.StartsWith("Refunded", customerCard.Payment!.Text);
        var providerCard = (await w.Provider.GetAsync(w.Mary.UserId, null, 1)).Page!.Cards.Single();
        Assert.True(providerCard.IsNoShow);
        Assert.Empty(providerCard.Actions);

        Assert.Equal(0, await w.NoShow.CancelMissedStartsAsync());                       // safe to run again
    }

    [Fact]
    public async Task ACashNoShow_IsCancelled_WithNothingToPayOrCollect()
    {
        var w = await CreateAsync();
        var b = await ScheduledAsync(w, "cash");
        w.MinutesAfter(60 * 5);

        Assert.Equal(1, await w.NoShow.CancelMissedStartsAsync());

        var saved = await Saved(w, b.BookingId);
        Assert.Equal(BookingStatuses.Cancelled, saved.Status);
        Assert.Equal(PaymentStatuses.Unpaid, saved.PaymentStatus);                                    // no money moved
        Assert.Equal(PaymentRecordStatuses.PendingCollection, saved.PaymentDetail!.PaymentStatus);    // kept as it was (rule 2)
        Assert.Equal(PaymentStatuses.Unpaid, PaymentProgress.Effective(saved.Status, saved.PaymentStatus, saved.PaymentDetail.PaymentStatus));

        var customerCard = (await w.Customer.GetAsync(w.Ana.UserId, null, 1)).Page!.Cards.Single();
        Assert.Contains("nothing to pay", customerCard.Payment!.Text);
        var providerCard = (await w.Provider.GetAsync(w.Mary.UserId, null, 1)).Page!.Cards.Single();
        Assert.Contains("nothing to collect", providerCard.Payment!.Text);
        Assert.False(providerCard.CanConfirmCash);
        Assert.True(providerCard.IsNoShow);
    }

    [Fact]
    public async Task TheJob_LeavesAlone_WhatIsNotANoShow()
    {
        var w = await CreateAsync();
        var unpaid = await ScheduledAsync(w, "none");                                    // a quotation nobody approved
        var started = await ScheduledAsync(w, "gcash", BookingStatuses.InProgress);
        var pending = await ScheduledAsync(w, "none", BookingStatuses.Pending);
        var stillInTime = await ScheduledAsync(w);

        w.MinutesAfter(121.9);                                                           // the button expired; the job waits one more minute
        Assert.Equal(0, await w.NoShow.CancelMissedStartsAsync());
        w.MinutesAfter(60 * 24 * 3);
        Assert.Equal(1, await w.NoShow.CancelMissedStartsAsync());                       // only the paid, never started one

        Assert.Equal(BookingStatuses.Accepted, (await Saved(w, unpaid.BookingId)).Status);
        Assert.Equal(BookingStatuses.InProgress, (await Saved(w, started.BookingId)).Status);
        Assert.Equal(BookingStatuses.Pending, (await Saved(w, pending.BookingId)).Status);
        Assert.Equal(BookingStatuses.Cancelled, (await Saved(w, stillInTime.BookingId)).Status);
    }

    [Fact]
    public async Task TheProviderDashboard_SaysNoShow_NotCancelledByTheCustomer()
    {
        Assert.Equal("No-show: not started in time", ProviderBookingDisplay.For(BookingStatuses.Cancelled, isNoShow: true).SideText);
        Assert.Equal("Cancelled by the customer", ProviderBookingDisplay.For(BookingStatuses.Cancelled, isNoShow: false).SideText);

        var w = await CreateAsync();
        await ScheduledAsync(w);
        w.MinutesAfter(200);
        await w.NoShow.CancelMissedStartsAsync();
        var dash = (await new ProviderDashboardService(w.Db, w.Clock).GetAsync(w.Mary.UserId)).Dashboard!;
        Assert.Equal("No-show: not started in time", dash.RecentRequests.Single().Display.SideText);
    }

    // ================================================================== the admin's "Needs Attention"

    [Fact]
    public void LateStartsAndNoShows_HaveTheirOwnAttentionRow()
    {
        var rows = AdminAttention.Build(new AdminAttentionCounts { LateStarts = 2, NoShows = 1, BookingsNeedingAttention = 3 });
        var row = Assert.Single(rows, r => r.Key == "late-start");
        Assert.Equal("1 provider no-show and 2 late starts in the last 7 days", row.Text);
        Assert.Equal("danger", row.Tone);
        Assert.Equal(AdminAttentionAction.BookingList, row.Action);
        Assert.Contains(rows, r => r.Key == "booking");

        var onlyLate = Assert.Single(AdminAttention.Build(new AdminAttentionCounts { LateStarts = 1 }));
        Assert.Equal("1 late start in the last 7 days", onlyLate.Text);
        Assert.Equal("warning", onlyLate.Tone);
        Assert.Empty(AdminAttention.Build(new AdminAttentionCounts()));
    }

    // ================================================================== the endpoint: 400 when the time is wrong

    private sealed class FakeBookings : IProviderBookingsService
    {
        public BookingActionResult Next { get; set; } = new(ProviderDashboardAccess.Allowed, BookingActionOutcome.Success, "ok");
        public (int UserId, int BookingId, string? Reason)? Called { get; private set; }

        public Task<BookingActionResult> StartAsync(int userId, int bookingId, string? lateReason, CancellationToken ct = default)
        {
            Called = (userId, bookingId, lateReason);
            return Task.FromResult(Next);
        }

        public Task<ProviderBookingsResult> GetAsync(int userId, string? tab, int page, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BookingActionResult> ChangeStatusAsync(int userId, int bookingId, BookingAction action, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BookingActionResult> CompleteAsync(int userId, int bookingId, string? completionMessage, IFormFile? proofPhoto, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static ProviderBookingsController ControllerFor(FakeBookings fake, int userId, bool json)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Role, Roles.ServiceProvider)
            }, "test"))
        };
        if (json) http.Request.Headers.Accept = "application/json";
        return new ProviderBookingsController(fake)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoTempData())
        };
    }

    [Theory]
    [InlineData(BookingActionOutcome.TooEarly, "Cannot start a booking before its scheduled date and time.")]
    [InlineData(BookingActionOutcome.StartWindowExpired, "Booking start window has expired.")]
    [InlineData(BookingActionOutcome.LateReasonRequired, "Please enter the reason for the late start before marking the booking In Progress.")]
    public async Task TheStartEndpoint_Answers400_WhenTheTimeOrTheReasonIsWrong(BookingActionOutcome outcome, string message)
    {
        var fake = new FakeBookings { Next = new(ProviderDashboardAccess.Allowed, outcome, message) };
        var controller = ControllerFor(fake, 7, json: true);

        var result = await controller.Start(33, "  why  ", "accepted", 2);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(message, System.Text.Json.JsonSerializer.Serialize(bad.Value));
        Assert.Equal((7, 33, "  why  "), fake.Called!.Value);             // the provider comes from the sign-in, never the form
    }

    [Fact]
    public async Task TheStartEndpoint_WithoutJavaScript_RedirectsWithTheMessage()
    {
        var fake = new FakeBookings { Next = new(ProviderDashboardAccess.Allowed, BookingActionOutcome.TooEarly, StartRules.TooEarlyMessage) };
        var controller = ControllerFor(fake, 7, json: false);

        var result = await controller.Start(33, null, "accepted", 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProviderBookingsController.Index), redirect.ActionName);
        Assert.Equal(StartRules.TooEarlyMessage, controller.TempData["Flash"]);
        Assert.Equal("error", controller.TempData["FlashKind"]);
    }

    [Fact]
    public void TheStartEndpoint_IsAPostWithTheAntiForgeryCheck()
    {
        var method = typeof(ProviderBookingsController).GetMethod(nameof(ProviderBookingsController.Start))!;
        Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false).SingleOrDefault());
        var post = Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>());
        Assert.Equal("{id:int}/start", post.Template);
    }

    // ================================================================== the model and the SQL

    private static IModel Model()
    {
        using var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return db.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void TheModel_HasTheFourColumns_TwoOfThemCalculated_AndTheCheck()
    {
        var bookings = Model().FindEntityType(typeof(Booking))!;
        var started = bookings.FindProperty(nameof(Booking.StartedAt))!;
        var reason = bookings.FindProperty(nameof(Booking.LateReason))!;
        var minutes = bookings.FindProperty(nameof(Booking.MinutesLate))!;
        var isLate = bookings.FindProperty(nameof(Booking.IsLate))!;

        Assert.Equal(("STARTED_AT", true), (started.GetColumnName(), started.IsNullable));
        Assert.Equal(("LATE_REASON", (int?)500, true), (reason.GetColumnName(), reason.GetMaxLength(), reason.IsNullable));
        Assert.Equal(("MINUTES_LATE", true, ServisyoDbContext.BookingMinutesLateSql, (bool?)false),
            (minutes.GetColumnName(), minutes.IsNullable, minutes.GetComputedColumnSql(), minutes.GetIsStored()));
        Assert.Equal(("IS_LATE", false, ServisyoDbContext.BookingIsLateSql, (bool?)false),
            (isLate.GetColumnName(), isLate.IsNullable, isLate.GetComputedColumnSql(), isLate.GetIsStored()));

        var check = Assert.Single(bookings.GetCheckConstraints(), c => c.Name == "CK_BOOKINGS_START");
        Assert.Equal(ServisyoDbContext.BookingStartCheck, check.Sql);
        Assert.Contains("BETWEEN 0 AND 120", check.Sql);
        Assert.Contains("> 15 AND [LATE_REASON] IS NOT NULL AND LEN([LATE_REASON]) > 0", check.Sql);
        Assert.Contains("<= 15 AND [LATE_REASON] IS NULL", check.Sql);
        Assert.Contains("DATEADD(HOUR, 8, [STARTED_AT])", ServisyoDbContext.BookingStartMinutesSql);
    }

    [Fact]
    public void TheMigrationSql_HasTheFiveObjects_AndTheWayBack()
    {
        Assert.Equal(5, StartWindowSql.CreateAll.Length);
        Assert.All(StartWindowSql.CreateAll, s => Assert.StartsWith("EXEC(N'CREATE OR ALTER ", s));
        Assert.Contains("TRIGGER dbo.trg_Bookings_StatusFlow", StartWindowSql.CreateAll[0]);
        Assert.Contains("TRIGGER dbo.trg_Bookings_Audit", StartWindowSql.CreateAll[1]);
        Assert.Contains("PROCEDURE dbo.usp_UpdateBookingStatus", StartWindowSql.CreateAll[2]);
        Assert.Contains("PROCEDURE dbo.usp_GetBookingsNeedingAttention", StartWindowSql.CreateAll[3]);
        Assert.Contains("PROCEDURE dbo.usp_GetNotifications", StartWindowSql.CreateAll[4]);

        // the way back: the same five objects as they were (v2.3, v2.11, v2.3, v2.3, v2.8)
        Assert.Equal(5, StartWindowSql.RestoreOld.Length);
        Assert.Contains(StartWindowSql.RestoreOld[0], DerivedDataSql.CreateAll);
        Assert.Equal(AuditStandardSql.Exec(BookingCompletionSql.BookingsAudit), StartWindowSql.RestoreOld[1]);
        Assert.Contains(StartWindowSql.RestoreOld[2], DerivedDataSql.CreateAll);
        Assert.Contains(StartWindowSql.RestoreOld[3], DerivedDataSql.CreateAll);
        Assert.Equal(NameChangeRequestsSql.GetNotifications28, StartWindowSql.RestoreOld[4]);
        for (var i = 0; i < 4; i++)
            Assert.Equal(Name(StartWindowSql.CreateAll[i]), Name(StartWindowSql.RestoreOld[i]));
        Assert.All(StartWindowSql.RestoreOld, s => Assert.DoesNotContain("LATE_REASON", s));          // they never read the new columns

        static string Name(string sql) => Regex.Match(sql, @"CREATE OR ALTER (?:TRIGGER|PROCEDURE) (dbo\.\w+)").Groups[1].Value;
    }

    [Fact]
    public void TheStatusFlowTrigger_AllowsANoShowOnly_AndTheStartOnlyNow()
    {
        var sql = StartWindowSql.StatusFlow;
        Assert.Contains("(d.STATUS = N'Accepted'    AND i.STATUS IN (N'In Progress', N'Cancelled'))", sql);
        Assert.Contains("THROW 50162", sql);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL p WHERE p.BOOKING_ID = i.BOOKING_ID)", sql);
        Assert.Contains("dbo.fn_ManilaNow()) <= 120", sql);
        Assert.Contains("OR i.PAYMENT_STATUS = N'Paid'", sql);                 // a test e-wallet payment must be refunded with it
        Assert.Contains("THROW 50163", sql);
        Assert.Contains("ABS(DATEDIFF(SECOND, i.STARTED_AT, SYSUTCDATETIME())) > 300", sql);
        Assert.Contains("THROW 50164", sql);
        Assert.Contains("THROW 50161", sql);                                   // the v2.3 payment rule is still there
        Assert.Contains("IF TRY_CAST(SESSION_CONTEXT(N'servisyo_dev_override') AS INT) = 1 RETURN;", sql);
    }

    [Fact]
    public void TheAuditTrigger_KeepsTheV211Parts_AndAddsTheStartAndTheNoShow()
    {
        var sql = StartWindowSql.BookingsAudit;
        Assert.Contains(DatabaseV2Sql.Marker + " (v2.12", sql);
        foreach (var part in new[] { "IF @ins = 1 AND @del = 0", "IF @ins = 0 AND @del = 1" })
            Assert.Equal(Block(BookingCompletionSql.BookingsAudit, part), Block(sql, part));
        Assert.Contains("THEN N'" + BookingCompletionSql.ProofNote + "'", sql);                       // completion proof (v2.11)
        Assert.Contains("THEN CONCAT(N' (late start: ', i.MINUTES_LATE, N' minutes late, reason ''', dbo.fn_AuditValue(i.LATE_REASON), N''')')", sql);
        Assert.Contains("WHEN x.STARTED = 1 THEN N'" + StartWindowSql.OnTimeNote + "'", sql);
        Assert.Contains("WHEN x.NO_SHOW = 1 THEN " + StartWindowSql.NoShowNoteSql, sql);
        Assert.Contains("AND NOT (x.STARTED = 1 AND f.SEQ IN (11, 12))", sql);
        var rows = Regex.Matches(sql, @"^\s+\((\d+), N'[^']+', ", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(Enumerable.Range(1, 12), rows);
        Assert.Equal("Provider No-Show " + (char)8212 + " missed scheduled start window", StartRules.NoShowReason);   // the same words as NCHAR(8212)
    }

    private static string Block(string sql, string start)
    {
        var text = sql.Replace("\r\n", "\n");
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        var to = text.IndexOf(";\n", from, StringComparison.Ordinal);
        return text[from..to];
    }

    [Fact]
    public void TheProcedures_UseTheSameNumbersAndWords_AsTheApp()
    {
        var update = StartWindowSql.UpdateBookingStatus;
        Assert.Contains("@LateReason     NVARCHAR(600) = NULL", update);
        Assert.Contains("THROW 50156, N'" + StartRules.TooEarlyMessage + "'", update);
        Assert.Contains("THROW 50157, N'" + StartRules.ExpiredMessage + "'", update);
        Assert.Contains("THROW 50158, N'" + StartRules.LateReasonRequired + "'", update);
        Assert.Contains("THROW 50159, N'" + StartRules.LateReasonTooLong + "'", update);
        Assert.Contains("@MinutesAfter > 120", update);
        Assert.Contains("@MinutesAfter > 15", update);

        var attention = StartWindowSql.NeedingAttention;
        Assert.Contains("SUM(CASE WHEN k.KIND = 1 THEN 1 ELSE 0 END) OVER () AS LateStartCount", attention);
        Assert.Contains("SUM(CASE WHEN k.KIND = 2 THEN 1 ELSE 0 END) OVER () AS NoShowCount", attention);
        Assert.Contains("DATEADD(DAY, -7, @Today)", attention);

        // the bell: every v2.8 line is still there; a no-show is never "cancelled by the customer"
        var bell = StartWindowSql.GetNotifications212;
        Assert.All(NameChangeRequestsSql.GetNotifications28.Split('\n').Select(l => l.TrimEnd('\r')), l => Assert.Contains(l, bell));
        Assert.Contains("was cancelled automatically (Provider No-Show)", bell);
        Assert.Contains("was cancelled because the provider did not start it within 2 hours of the scheduled time.", bell);
        Assert.Equal(4, Regex.Matches(bell, @"\(v2\.12\)|-- v2\.12:").Count);               // the four places v2.12 changed
    }

    // ================================================================== the design file and the tools

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void TheDesignFile_IsOnV212_WithTheSameSql()
    {
        var design = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "sql", "DatabaseDesign-v2.sql")).Replace("\r\n", "\n");
        Assert.Contains("DATABASE DESIGN v2.12", design);
        Assert.Contains("WHAT CHANGED IN v2.12 (EF migration \"StartWindow\")", design);
        Assert.Contains("MigrationId LIKE N'%[_]StartWindow'", design);
        Assert.Contains("CONSTRAINT CK_BOOKINGS_START CHECK (" + ServisyoDbContext.BookingStartCheck + ")", design);
        Assert.Contains("IS_LATE                      AS (" + ServisyoDbContext.BookingIsLateSql + ")", design);
        Assert.Contains("MINUTES_LATE                 AS (" + ServisyoDbContext.BookingMinutesLateSql + ")", design);
        Assert.Contains("LATE_REASON                  NVARCHAR(500)   NULL", design);
        Assert.Contains("STARTED_AT                   DATETIME2       NULL", design);
        foreach (var sql in new[] { StartWindowSql.StatusFlow, StartWindowSql.BookingsAudit, StartWindowSql.UpdateBookingStatus, StartWindowSql.NeedingAttention })
            Assert.Contains(sql.Replace("\r\n", "\n").Trim(), design);
        foreach (var old in StartWindowSql.RestoreOld)                                   // the old versions were replaced
        {
            var body = old["EXEC(N'".Length..old.LastIndexOf("')", StringComparison.Ordinal)].Replace("''", "'");
            Assert.DoesNotContain(body.Replace("\r\n", "\n").Trim(), design);
        }
        Assert.Contains("CREATE PROCEDURE #svMoveBooking", design);
        foreach (var n in Enumerable.Range(38, 8))
            Assert.Contains($"SET @t = N'{n} ", design);
        Assert.Contains("ProviderName NVARCHAR(255), TotalCount INT, LateStartCount INT, NoShowCount INT);", design);
    }

    [Fact]
    public void TheTools_KnowTheNewMigration()
    {
        var root = RepoRoot();
        var add = File.ReadAllText(Path.Combine(root, "tools", "add-start-window.ps1"));
        Assert.Contains("StartWindowSql.CreateAll", add);
        Assert.Contains("StartWindowSql.RestoreOld", add);
        foreach (var op in new[] { "AddColumn BOOKINGS IS_LATE", "AddColumn BOOKINGS LATE_REASON", "AddColumn BOOKINGS MINUTES_LATE",
                                   "AddColumn BOOKINGS STARTED_AT", "AddCheckConstraint BOOKINGS CK_BOOKINGS_START" })
            Assert.Contains(op, add);
        Assert.Contains("MigrationId LIKE N'%[_]BookingCompletionProof'", add);          // v2.11 must be there first
        Assert.Contains("ROLLBACK TRANSACTION", add);                                    // the live test keeps nothing
        var deploy = File.ReadAllText(Path.Combine(root, "tools", "make-monster-deploy.ps1"));
        Assert.Contains("$Latest = \"StartWindow\"", deploy);
        var check = File.ReadAllText(Path.Combine(root, "docs", "sql", "check-monster-before-deploy.sql"));
        Assert.Contains("MigrationId LIKE N'%[_]StartWindow'", check);
    }

    [Fact]
    public void TheNoShowJob_IsRegistered()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Servisyo.Web", "Program.cs"));
        Assert.Contains("AddScoped<INoShowService, NoShowService>()", program);
        Assert.Contains("AddHostedService<NoShowCancellationService>()", program);
        Assert.Equal(TimeSpan.FromMinutes(5), NoShowCancellationService.Interval);
    }

    [Fact]
    public void ThePage_HasTheLateStartWindow_AndTheScripts()
    {
        var view = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Servisyo.Web", "Views", "ProviderBookings", "Index.cshtml"));
        Assert.Contains("data-late-dialog", view);
        Assert.Contains("name=\"lateReason\"", view);
        Assert.Contains("maxlength=\"@StartRules.MaxLateReasonLength\"", view);
        Assert.Contains("data-start-refresh-in", view);
        Assert.Contains("~/js/booking-start.js", view);
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Servisyo.Web", "wwwroot", "js", "booking-start.js"));
        Assert.Contains(StartRules.LateReasonRequired, js);
        Assert.Contains(StartRules.LateReasonTooLong, js);
        Assert.Contains("\"Accept\": \"application/json\"", js);
        Assert.Contains("window.svLiveRefresh", File.ReadAllText(Path.Combine(RepoRoot(), "src", "Servisyo.Web", "wwwroot", "js", "live-bookings.js")));
    }
}
