using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.7: providers REQUEST days off and an administrator approves or rejects them.
///   - 1 to 7 days in a row ("Maximum Day Off is 1 week or 7 days"), from tomorrow to December 31, no 5-a-month limit;
///   - a Pending request holds its dates (one provider per date) but does not stop bookings; Approved does;
///   - Rejected gives the dates back; every answer is an ADMIN_ACTIONS row the request points to.
public class DayOffRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);     // Wed Oct 14, 10:00 in Cebu
    private static readonly DateOnly Today = new(2026, 10, 14);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// Records what would have been pushed to the open pages (SignalR).
    private sealed class FakeDayOffLive : IDayOffLiveUpdates
    {
        public List<(int ProviderId, int ProviderUserId, string What)> Sent { get; } = new();
        public Task DayOffChangedAsync(int providerId, int providerUserId, string what)
        {
            Sent.Add((providerId, providerUserId, what));
            return Task.CompletedTask;
        }
    }

    private sealed record World(ServisyoDbContext Db, ProviderAvailabilityService Schedule, ProviderManagementService Admin,
        User AdminUser, Provider Vince, Provider Liza, Customer Ana, FakeDayOffLive Live);

    private static User NewUser(string first, string role, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status
    };

    private static async Task<World> WorldAsync()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", Roles.Administrator);
        var vince = new Provider { BusinessName = "Vincent Works", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Vincent", Roles.ServiceProvider) };
        var liza = new Provider { BusinessName = "Liza Cleans", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Liza", Roles.ServiceProvider) };
        var ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        db.Users.Add(admin);
        db.Providers.AddRange(vince, liza);
        db.Customers.Add(ana);
        await db.SaveChangesAsync();
        var live = new FakeDayOffLive();
        return new(db, new ProviderAvailabilityService(db, new FixedClock(), null, live), new ProviderManagementService(db, null, new FixedClock(), live),
            admin, vince, liza, ana, live);
    }

    private static DateOnly Oct(int day) => new(2026, 10, day);
    private static DateOnly Nov(int day) => new(2026, 11, day);

    private static Task<DayOffResult> AskAsync(World w, DateOnly? first, DateOnly? last = null, string? reason = "Family trip", Provider? who = null) =>
        w.Schedule.RequestDayOffAsync((who ?? w.Vince).UserId, new DayOffRequestForm { Start = first, End = last, Reason = reason });

    private static async Task<int> OnlyRequestIdAsync(World w) => (await w.Db.DayOffRequests.SingleAsync()).RequestId;

    private static async Task<Booking> BookAsync(World w, DateOnly day, string status)
    {
        var b = new Booking
        {
            CustomerId = w.Ana.CustomerId, ProviderId = w.Vince.ProviderId, BookingDate = day, BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 500m, Status = status, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        };
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        return b;
    }

    // ------------------------------------------------------------ the provider asks

    [Fact]
    public async Task ARequest_IsSavedAsPending_WithOneDayPerDate()
    {
        var w = await WorldAsync();

        var result = await AskAsync(w, Nov(20), Nov(22), "  Family trip  ");

        Assert.True(result.Done);
        Assert.Equal("Your day off request for Nov 20 to Nov 22, 2026 (3 days) was sent. Customers can still book you on those days until an administrator approves it.", result.Message);
        var request = await w.Db.DayOffRequests.Include(r => r.Days).SingleAsync();
        Assert.Equal(DayOffStatuses.Pending, request.Status);
        Assert.Equal("Family trip", request.Reason);
        Assert.Equal(w.Vince.ProviderId, request.ProviderId);
        Assert.Null(request.DecidedByActionId);
        Assert.Equal(Now.UtcDateTime, request.RequestedAt);
        Assert.Equal(new[] { Nov(20), Nov(21), Nov(22) }, request.Days.Select(d => d.UnavailableDate).OrderBy(d => d));
        Assert.All(request.Days, d => { Assert.Equal(w.Vince.ProviderId, d.ProviderId); Assert.Null(d.Reason); Assert.Null(d.ApprovedByActionId); });
        Assert.Empty(w.Db.AdminActions);
    }

    [Fact]
    public async Task OneDay_TheLastDayCanBeLeftEmpty()
    {
        var w = await WorldAsync();
        var result = await AskAsync(w, Nov(20), null, null);

        Assert.True(result.Done);
        Assert.Equal(Nov(20), (await w.Db.ProviderUnavailableDates.SingleAsync()).UnavailableDate);
        Assert.Null((await w.Db.DayOffRequests.SingleAsync()).Reason);
    }

    [Fact]
    public async Task SevenDays_IsTheMost_EightAreRefused()
    {
        var w = await WorldAsync();

        var eight = await AskAsync(w, Nov(20), Nov(27));
        Assert.False(eight.Done);
        Assert.Equal(new[] { "Maximum Day Off is 1 week or 7 days" }, eight.Errors);
        Assert.Empty(w.Db.DayOffRequests);

        Assert.True((await AskAsync(w, Nov(20), Nov(26))).Done);
        Assert.Equal(7, await w.Db.ProviderUnavailableDates.CountAsync());
    }

    [Fact]
    public async Task TheFiveDaysAMonthLimit_IsGone_TheAdministratorDecides()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(2), Nov(8))).Done);                         // 7 days in November
        var first = await OnlyRequestIdAsync(w);
        Assert.Equal(AdminDayOffOutcome.Success, (await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, first, true)).Outcome);

        Assert.True((await AskAsync(w, Nov(16), Nov(22))).Done);                       // 7 more in the same month
        Assert.Equal(14, await w.Db.ProviderUnavailableDates.CountAsync());
    }

    [Theory]
    [InlineData("2026-10-14", null, "Oct 14, 2026: Same-day unavailable date selection is not permitted. Unavailable dates must be marked at least 1 day in advance.")]
    [InlineData("2026-10-13", null, "Oct 13, 2026 is in the past. Choose tomorrow or a later date.")]
    [InlineData("2026-11-20", "2026-11-19", "The last day off cannot be before the first day.")]
    [InlineData("2026-12-30", "2027-01-02", "Jan 2, 2027 is not in 2026. You can only request days off until December 31, 2026.")]
    [InlineData("2027-01-04", null, "Jan 4, 2027 is not in 2026. You can only request days off until December 31, 2026.")]
    [InlineData("2026-11-01", "2026-12-31", "Maximum Day Off is 1 week or 7 days")]
    public async Task BadDates_AreRefused_AndNothingIsSaved(string first, string? last, string expected)
    {
        var w = await WorldAsync();

        var result = await AskAsync(w, DateOnly.Parse(first, System.Globalization.CultureInfo.InvariantCulture),
            last is null ? null : DateOnly.Parse(last, System.Globalization.CultureInfo.InvariantCulture));

        Assert.False(result.Done);
        Assert.Equal(expected, Assert.Single(result.Errors));
        Assert.Empty(w.Db.DayOffRequests);
        Assert.Empty(w.Db.ProviderUnavailableDates);
    }

    [Fact]
    public async Task NoFirstDay_OrATooLongReason_IsRefused()
    {
        var w = await WorldAsync();
        Assert.Equal("Please choose the first day off.", (await AskAsync(w, null)).Message);
        Assert.Equal("The reason can be at most 255 characters.", Assert.Single((await AskAsync(w, Nov(20), null, new string('x', 256))).Errors));
        Assert.Empty(w.Db.DayOffRequests);
    }

    [Fact]
    public async Task Tomorrow_IsTheFirstDay_AndDec31_TheLast()
    {
        var w = await WorldAsync();
        var page = (await w.Schedule.GetAsync(w.Vince.UserId)).Page!;
        Assert.Equal(Oct(15), page.FirstDate);
        Assert.Equal(new DateOnly(2026, 12, 31), page.LastDate);

        Assert.True((await AskAsync(w, Oct(15))).Done);
        await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, await OnlyRequestIdAsync(w), true);
        Assert.True((await AskAsync(w, new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 31))).Done);
    }

    [Fact]
    public async Task TheSameDayRule_UsesCebuTime()
    {
        // 20:00 UTC on Oct 13 is already 04:00 on Oct 14 in Cebu: Oct 14 is today there
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var p = new Provider { BusinessName = "Vincent Works", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Vincent", Roles.ServiceProvider) };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        var svc = new ProviderAvailabilityService(db, new EarlyClock());

        Assert.False((await svc.RequestDayOffAsync(p.UserId, new DayOffRequestForm { Start = Oct(14) })).Done);
        Assert.True((await svc.RequestDayOffAsync(p.UserId, new DayOffRequestForm { Start = Oct(15) })).Done);
    }

    private sealed class EarlyClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task OnlyOneRequest_CanWaitAtATime()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20), Nov(22))).Done);

        var second = await AskAsync(w, Nov(25));

        Assert.False(second.Done);
        Assert.Equal("Your request for Nov 20 to Nov 22, 2026 is still waiting for an administrator. You can send a new request when it is answered, or cancel it first.", second.Message);
        Assert.Equal(1, await w.Db.DayOffRequests.CountAsync());
        Assert.True((await w.Schedule.GetAsync(w.Vince.UserId)).Page!.HasPendingRequest);
    }

    [Fact]
    public async Task ADayThatIsAlreadyMine_CannotBeAskedForAgain()
    {
        var w = await WorldAsync();
        w.Db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = Nov(21), Reason = "Old" });
        await w.Db.SaveChangesAsync();

        var result = await AskAsync(w, Nov(20), Nov(22));

        Assert.Equal(new[] { "Nov 21, 2026: you already have this day off (or asked for it)." }, result.Errors);
    }

    [Fact]
    public async Task EveryProblemDay_IsListed()
    {
        var w = await WorldAsync();
        var b = await BookAsync(w, Nov(21), BookingStatuses.Accepted);
        Assert.True((await AskAsync(w, Nov(23), null, "x", w.Liza)).Done);

        var result = await AskAsync(w, Nov(20), Nov(24));

        Assert.False(result.Done);
        Assert.Equal("Your day off request for Nov 20 to Nov 24, 2026 was not sent.", result.Message);
        Assert.Equal(new[]
        {
            $"Nov 21, 2026: you already have booking #{b.BookingId} (Accepted) on this day. A day with an active booking cannot be marked unavailable.",
            "Nov 23, 2026: Another provider already has this day off. Only one provider can be off per day, so please choose another date."
        }, result.Errors);
    }

    [Fact]
    public async Task OnlyAnApprovedActiveProvider_CanAsk()
    {
        var w = await WorldAsync();
        var pending = new Provider { BusinessName = "New", ApprovalStatus = ApprovalStatuses.Pending, User = NewUser("Pen", Roles.ServiceProvider) };
        var suspended = new Provider { BusinessName = "Sus", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Sus", Roles.ServiceProvider, AccountStatuses.Suspended) };
        w.Db.Providers.AddRange(pending, suspended);
        await w.Db.SaveChangesAsync();
        var form = new DayOffRequestForm { Start = Nov(20) };

        Assert.Equal(ProviderDashboardAccess.Pending, (await w.Schedule.RequestDayOffAsync(pending.UserId, form)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await w.Schedule.RequestDayOffAsync(suspended.UserId, form)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await w.Schedule.RequestDayOffAsync(w.Ana.UserId, form)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await w.Schedule.RemoveRequestAsync(w.AdminUser.UserId, 1)).Access);
        Assert.Empty(w.Db.DayOffRequests);
    }

    // ------------------------------------------------------------ the provider's list

    [Fact]
    public async Task TheList_ShowsEachRequestAsOneLine_WithItsBadge()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(2), Nov(4), "Trip")).Done);
        await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, await OnlyRequestIdAsync(w), true);
        Assert.True((await AskAsync(w, Nov(20), Nov(22), null)).Done);
        await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Oct(14), "Sick");

        var list = (await w.Schedule.GetAsync(w.Vince.UserId)).Page!.DaysOff;

        Assert.Equal(new[] { "Oct 14, 2026", "Nov 2 to Nov 4, 2026", "Nov 20 to Nov 22, 2026" }, list.Select(d => d.DatesText));
        Assert.Equal(new[] { "1 day", "3 days", "3 days" }, list.Select(d => d.DaysText));
        Assert.Equal(new string?[] { null, "Approved", "Pending Admin Approval" }, list.Select(d => d.Badge));
        Assert.Equal(new[] { "Remove", "Remove", "Cancel request" }, list.Select(d => d.RemoveLabel));
        Assert.Equal(UnavailableDateRules.ApprovedNote, list[0].Note);
        Assert.Equal("Trip", list[1].Reason);
        Assert.True(list[2].IsPending);
        Assert.NotNull(list[2].RequestId);
        Assert.NotNull(list[0].UnavailableId);
    }

    [Fact]
    public async Task CancellingAPendingRequest_DeletesItAndItsDays()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20), Nov(22))).Done);

        var result = await w.Schedule.RemoveRequestAsync(w.Vince.UserId, await OnlyRequestIdAsync(w));

        Assert.True(result.Done);
        Assert.Equal("Your day off request for Nov 20 to Nov 22, 2026 was cancelled.", result.Message);
        Assert.Empty(w.Db.DayOffRequests);
        Assert.Empty(w.Db.ProviderUnavailableDates);
        Assert.True((await AskAsync(w, Nov(25))).Done);                                 // a new one can be sent now
    }

    [Fact]
    public async Task RemovingAnApprovedRequest_FreesItsDays()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20), Nov(22))).Done);
        var id = await OnlyRequestIdAsync(w);
        await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, id, true);

        var result = await w.Schedule.RemoveRequestAsync(w.Vince.UserId, id);

        Assert.True(result.Done);
        Assert.Equal("Your days off for Nov 20 to Nov 22, 2026 were removed. Customers can book you on those days again.", result.Message);
        Assert.Empty(w.Db.ProviderUnavailableDates);
        Assert.Empty(w.Db.DayOffRequests);
        Assert.Single(w.Db.AdminActions);                                               // the admin history stays
    }

    [Fact]
    public async Task RemovingAnApprovedRequest_KeepsTheDaysThatAlreadyPassed()
    {
        var w = await WorldAsync();
        var request = new ProviderDayOffRequest { ProviderId = w.Vince.ProviderId, Status = DayOffStatuses.Approved,
            DecidedBy = AdminAction.ForProvider(w.AdminUser.UserId, ProviderManagementService.DayOffApprovedActionType, w.Vince.ProviderId, "Oct 13 to Oct 15, 2026") };
        foreach (var d in new[] { Oct(13), Oct(14), Oct(15) })
            request.Days.Add(new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = d });
        w.Db.DayOffRequests.Add(request);
        await w.Db.SaveChangesAsync();

        Assert.True((await w.Schedule.RemoveRequestAsync(w.Vince.UserId, request.RequestId)).Done);

        Assert.Equal(Oct(13), (await w.Db.ProviderUnavailableDates.SingleAsync()).UnavailableDate);   // yesterday stays as history
        Assert.Single(w.Db.DayOffRequests);
    }

    [Fact]
    public async Task ASingleDay_CanBeRemoved_ButNotOneThatPassed()
    {
        var w = await WorldAsync();
        w.Db.ProviderUnavailableDates.AddRange(
            new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = Nov(20) },
            new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = Oct(1) });
        await w.Db.SaveChangesAsync();
        var upcoming = await w.Db.ProviderUnavailableDates.SingleAsync(d => d.UnavailableDate == Nov(20));
        var old = await w.Db.ProviderUnavailableDates.SingleAsync(d => d.UnavailableDate == Oct(1));

        Assert.Equal("Nov 20, 2026 was removed. Customers can book you on that day again.", (await w.Schedule.RemoveDayAsync(w.Vince.UserId, upcoming.UnavailableId)).Message);
        Assert.False((await w.Schedule.RemoveDayAsync(w.Vince.UserId, old.UnavailableId)).Done);
        Assert.Equal(Oct(1), (await w.Db.ProviderUnavailableDates.SingleAsync()).UnavailableDate);
    }

    [Fact]
    public async Task ADayOfARequest_IsNotRemovedOnItsOwn()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20), Nov(21))).Done);
        var day = await w.Db.ProviderUnavailableDates.FirstAsync();

        Assert.False((await w.Schedule.RemoveDayAsync(w.Vince.UserId, day.UnavailableId)).Done);
        Assert.Equal(2, await w.Db.ProviderUnavailableDates.CountAsync());
    }

    // ------------------------------------------------------------ the administrator answers

    [Fact]
    public async Task Approve_MarksTheRequest_AndPointsToTheAdminLog()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20), Nov(22))).Done);
        var id = await OnlyRequestIdAsync(w);

        var result = await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, id, approve: true);

        Assert.Equal(AdminDayOffOutcome.Success, result.Outcome);
        Assert.Equal("Day off approved for Vincent Reyes: Nov 20 to Nov 22, 2026 (3 days). Customers cannot book them on those days.", result.Message);
        var log = await w.Db.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(ProviderManagementService.DayOffApprovedActionType, log.ActionType);
        Assert.Equal(AdminTargetTypes.ServiceProvider, log.TargetType);
        Assert.Equal(w.Vince.ProviderId, log.TargetProviderId);
        Assert.Equal(w.AdminUser.UserId, log.AdminUserId);
        Assert.Equal("Nov 20 to Nov 22, 2026", log.Reason);
        var request = await w.Db.DayOffRequests.AsNoTracking().Include(r => r.Days).SingleAsync();
        Assert.Equal(DayOffStatuses.Approved, request.Status);
        Assert.Equal(log.ActionId, request.DecidedByActionId);
        Assert.Equal(3, request.Days.Count);
        Assert.Equal(new[] { "Approved" }, (await w.Schedule.GetAsync(w.Vince.UserId)).Page!.DaysOff.Select(d => d.Badge));
    }

    [Fact]
    public async Task Reject_GivesTheDaysBack_AndKeepsTheRequestAsTheRecord()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20), Nov(22))).Done);
        var id = await OnlyRequestIdAsync(w);

        var result = await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, id, approve: false);

        Assert.Equal(AdminDayOffOutcome.Success, result.Outcome);
        Assert.Equal("Day off request of Vincent Reyes for Nov 20 to Nov 22, 2026 was rejected. They stay available on those days.", result.Message);
        Assert.Empty(w.Db.ProviderUnavailableDates);
        var request = await w.Db.DayOffRequests.AsNoTracking().SingleAsync();
        Assert.Equal(DayOffStatuses.Rejected, request.Status);
        var log = await w.Db.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(ProviderManagementService.DayOffRejectedActionType, log.ActionType);
        Assert.Equal(log.ActionId, request.DecidedByActionId);
        Assert.Empty((await w.Schedule.GetAsync(w.Vince.UserId)).Page!.DaysOff);
        Assert.True((await AskAsync(w, Nov(21))).Done);                                  // a rejected request does not wait any more
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task Approve_IsRefused_WhileABookingWaitsOnOneOfTheDays(string status)
    {
        // a Pending request does not stop bookings, so a customer may have booked one of its days meanwhile
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20), Nov(22))).Done);
        var b = await BookAsync(w, Nov(21), status);

        var result = await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, await OnlyRequestIdAsync(w), approve: true);

        Assert.Equal(AdminDayOffOutcome.Invalid, result.Outcome);
        Assert.Equal($"Vincent Reyes has booking #{b.BookingId} ({status}) on Nov 21, 2026. That booking has to be settled first (or reject the request), so the customer is not left without a provider.", result.Message);
        Assert.Equal(DayOffStatuses.Pending, (await w.Db.DayOffRequests.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(w.Db.AdminActions);

        // rejecting it is still possible
        Assert.Equal(AdminDayOffOutcome.Success, (await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, await OnlyRequestIdAsync(w), approve: false)).Outcome);
    }

    [Fact]
    public async Task ARequestIsAnsweredOnlyOnce()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20))).Done);
        var id = await OnlyRequestIdAsync(w);
        await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, id, approve: true);

        var again = await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, id, approve: false);

        Assert.Equal(AdminDayOffOutcome.Invalid, again.Outcome);
        Assert.Equal("This day off request of Vincent Reyes was already approved.", again.Message);
        Assert.Single(w.Db.AdminActions);
        Assert.Single(w.Db.ProviderUnavailableDates);
    }

    [Fact]
    public async Task TheRequest_MustBelongToTheProviderInTheAddress()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20))).Done);
        var id = await OnlyRequestIdAsync(w);

        Assert.Equal(AdminDayOffOutcome.NotFound, (await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Liza.ProviderId, id, true)).Outcome);
        Assert.Equal(AdminDayOffOutcome.NotFound, (await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, 99999, true)).Outcome);
        Assert.Equal(DayOffStatuses.Pending, (await w.Db.DayOffRequests.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanAnswer()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20))).Done);
        var id = await OnlyRequestIdAsync(w);
        var suspendedAdmin = NewUser("Old", Roles.Administrator, AccountStatuses.Suspended);
        w.Db.Users.Add(suspendedAdmin);
        await w.Db.SaveChangesAsync();

        foreach (var who in new[] { w.Vince.UserId, w.Ana.UserId, suspendedAdmin.UserId, 99999 })
            Assert.Equal(AdminDayOffOutcome.NotAllowed, (await w.Admin.DecideDayOffRequestAsync(who, w.Vince.ProviderId, id, true)).Outcome);
        Assert.Equal(DayOffStatuses.Pending, (await w.Db.DayOffRequests.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ACancelledRequest_CannotBeAnswered()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20))).Done);
        var id = await OnlyRequestIdAsync(w);
        Assert.True((await w.Schedule.RemoveRequestAsync(w.Vince.UserId, id)).Done);

        Assert.Equal(AdminDayOffOutcome.NotFound, (await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, id, true)).Outcome);
        Assert.Empty(w.Db.AdminActions);
    }

    // ------------------------------------------------------------ live: the administrators see it at once

    [Fact]
    public async Task EveryChange_IsPushedToTheAdministratorsAndTheProvider_AndARefusalIsNot()
    {
        var w = await WorldAsync();
        var vince = (w.Vince.ProviderId, w.Vince.UserId);

        Assert.True((await AskAsync(w, Nov(20), Nov(22))).Done);
        Assert.False((await AskAsync(w, Nov(25))).Done);                                // refused (one waiting): nothing pushed
        var id = await OnlyRequestIdAsync(w);
        await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, id, approve: true);
        Assert.True((await w.Schedule.RemoveRequestAsync(w.Vince.UserId, id)).Done);
        Assert.True((await AskAsync(w, Nov(24))).Done);
        await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, await OnlyRequestIdAsync(w), approve: false);
        Assert.True((await AskAsync(w, Nov(26))).Done);
        Assert.True((await w.Schedule.RemoveRequestAsync(w.Vince.UserId, await w.Db.DayOffRequests.Where(r => r.Status == DayOffStatuses.Pending).Select(r => r.RequestId).SingleAsync())).Done);
        await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Oct(14), "Sick");

        Assert.Equal(new[] { "requested", "approved", "removed", "requested", "rejected", "requested", "cancelled", "extra-day" }, w.Live.Sent.Select(s => s.What));
        Assert.All(w.Live.Sent, s => Assert.Equal(vince, (s.ProviderId, s.ProviderUserId)));
    }

    [Fact]
    public async Task TheProvidersPage_ListsTheWaitingRequests_AndMarksTheirCards()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, Nov(20), Nov(22))).Done);
        Assert.True((await AskAsync(w, Nov(25), null, null, w.Liza)).Done);
        await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Liza.ProviderId,
            await w.Db.DayOffRequests.Where(r => r.ProviderId == w.Liza.ProviderId).Select(r => r.RequestId).SingleAsync(), approve: true);

        var page = (await w.Admin.GetProvidersAsync(w.AdminUser.UserId, new ProviderListQuery()))!;

        var waiting = Assert.Single(page.WaitingDayOffs);                                // Liza's is answered already
        Assert.Equal(new WaitingDayOffLine(w.Vince.ProviderId, "Vincent Reyes", "Nov 20 to Nov 22, 2026", "3 days"), waiting);
        Assert.Contains(page.Cards.Single(c => c.ProviderId == w.Vince.ProviderId).Badges, b => b.Text == ProviderManagementService.DayOffRequestBadge);
        Assert.DoesNotContain(page.Cards.Single(c => c.ProviderId == w.Liza.ProviderId).Badges, b => b.Text == ProviderManagementService.DayOffRequestBadge);
    }

    // ------------------------------------------------------------ which days stop bookings

    [Fact]
    public async Task OnlyApprovedRequests_AndDaysWithoutARequest_BlockBookings()
    {
        var w = await WorldAsync();
        w.Db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = Nov(2) });   // an older day off
        await w.Db.SaveChangesAsync();
        await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(3), "Sick");                               // an extra day
        Assert.True((await AskAsync(w, Nov(10), Nov(11))).Done);
        await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Vince.ProviderId, await OnlyRequestIdAsync(w), true);            // approved
        Assert.True((await AskAsync(w, Nov(20), Nov(21))).Done);                                                                        // pending

        var blocking = await w.Db.ProviderUnavailableDates.BlockingBookings().OrderBy(d => d.UnavailableDate).Select(d => d.UnavailableDate).ToListAsync();

        Assert.Equal(new[] { Nov(2), Nov(3), Nov(10), Nov(11) }, blocking);
        Assert.Equal(6, await w.Db.ProviderUnavailableDates.CountAsync());                 // the pending days still hold their dates
    }

    // ------------------------------------------------------------ the database model

    private static IModel Model()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return db.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void TheRequestsTable_HasItsStatusRules_AndKeepsTheAdminHistory()
    {
        var entity = Model().FindEntityType(typeof(ProviderDayOffRequest))!;
        Assert.Equal("PROVIDER_DAY_OFF_REQUESTS", entity.GetTableName());
        Assert.Equal(new[] { "REQUEST_ID", "PROVIDER_ID", "REASON", "STATUS", "REQUESTED_AT", "DECIDED_BY_ACTION_ID" }.OrderBy(x => x),
            entity.GetProperties().Select(p => p.GetColumnName()).OrderBy(x => x));
        Assert.Equal(20, entity.FindProperty(nameof(ProviderDayOffRequest.Status))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(ProviderDayOffRequest.Status))!.IsNullable);

        var checks = entity.GetCheckConstraints().ToDictionary(c => c.Name!, c => c.Sql);
        Assert.Equal("[STATUS] IN ('Pending','Approved','Rejected')", checks["CK_PROVIDER_DAY_OFF_REQUESTS_STATUS"]);
        Assert.Equal(ServisyoDbContext.DayOffDecisionCheck, checks["CK_PROVIDER_DAY_OFF_REQUESTS_DECISION"]);

        var decision = Assert.Single(entity.GetForeignKeys(), f => f.PrincipalEntityType.ClrType == typeof(AdminAction));
        Assert.Equal(DeleteBehavior.Restrict, decision.DeleteBehavior);
        Assert.True(Assert.Single(entity.GetIndexes(), i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ProviderDayOffRequest.DecidedByActionId)).IsUnique);
    }

    [Fact]
    public void ADay_PointsToItsRequest_WithoutCascade_AndNeverToBothSources()
    {
        var entity = Model().FindEntityType(typeof(ProviderUnavailableDate))!;
        var column = entity.FindProperty(nameof(ProviderUnavailableDate.RequestId))!;
        Assert.Equal("REQUEST_ID", column.GetColumnName());
        Assert.True(column.IsNullable);
        var fk = Assert.Single(entity.GetForeignKeys(), f => f.PrincipalEntityType.ClrType == typeof(ProviderDayOffRequest));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        Assert.Equal("[REQUEST_ID] IS NULL OR [APPROVED_BY_ACTION_ID] IS NULL",
            Assert.Single(entity.GetCheckConstraints(), c => c.Name == "CK_PROVIDER_UNAVAILABLE_DATES_SOURCE").Sql);
        // still only one provider per date (v2.5)
        Assert.True(Assert.Single(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ProviderUnavailableDate.UnavailableDate) })).IsUnique);
    }

    // ------------------------------------------------------------ the SQL the migration adds

    [Fact]
    public void TheDatabaseObjects_ChangeOnlyTheDayOffCheck()
    {
        Assert.Equal(DayOffRequestsSql.CreateAll.Length, DayOffRequestsSql.RestoreOld.Length);

        // fn_IsProviderAvailable and usp_CreateBooking: one changed line each, now ignoring a Pending request
        foreach (var (before, after) in new[] { (DayOffRequestsSql.IsProviderAvailableV20, DayOffRequestsSql.IsProviderAvailable27),
                                                (DayOffRequestsSql.CreateBookingV23, DayOffRequestsSql.CreateBooking27) })
        {
            var a = before.Split('\n');
            var b = after.Split('\n');
            Assert.Equal(a.Length + 1, b.Length);
            Assert.Single(a.Except(b));
            Assert.Contains("r.STATUS = N''Approved''", after);
            Assert.Contains("u.REQUEST_ID IS NULL", after);
            Assert.StartsWith("EXEC(N'CREATE OR ALTER ", after);
        }

        // usp_GetNotifications: the v2.3 text is all still there, plus the two new parts
        var notes = DayOffRequestsSql.GetNotifications27;
        Assert.All(DayOffRequestsSql.GetNotificationsV23.Split('\n').Select(l => l.TrimEnd('\r')), l => Assert.Contains(l, notes));
        Assert.Contains("N''day-off''", notes);
        Assert.Contains("N''day-off-new''", notes);
        Assert.Contains("a.ACTION_TYPE IN (N''Day Off Approved'', N''Day Off Rejected'', N''Extra Day Off Approved'')", notes);
        Assert.Contains("WHERE @Role = N''Administrator'' AND r.STATUS = N''Pending''", notes);

        // the down steps are exactly the older versions the migrations made
        Assert.Contains(DayOffRequestsSql.RestoreOld[0], DatabaseV2Sql.CreateAll);
        Assert.Contains(DayOffRequestsSql.RestoreOld[1], DerivedDataSql.CreateAll);
        Assert.Contains(DayOffRequestsSql.RestoreOld[2], DerivedDataSql.CreateAll);
    }

    [Fact]
    public void TheActionTypes_AreTheOnesTheBellReads()
    {
        Assert.Contains($"N''{ProviderManagementService.DayOffApprovedActionType}''", DayOffRequestsSql.GetNotifications27);
        Assert.Contains($"N''{ProviderManagementService.DayOffRejectedActionType}''", DayOffRequestsSql.GetNotifications27);
        Assert.Contains($"N''{ProviderManagementService.ExtraDayOffActionType}''", DayOffRequestsSql.GetNotifications27);
        Assert.Contains($"N''{DayOffStatuses.Approved}''", DayOffRequestsSql.CreateBooking27);
    }

    [Fact]
    public void TheDesignFile_HasTheV27Objects_WithTheSameSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "sql", "DatabaseDesign-v2.sql"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var design = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "sql", "DatabaseDesign-v2.sql")).Replace("\r\n", "\n");

        Assert.Contains("DATABASE DESIGN v2.10", design);                        // v2.8 to v2.10 came after
        Assert.Contains("WHAT CHANGED IN v2.7 (EF migration \"DayOffRequests\")", design);
        Assert.Contains("CREATE TABLE PROVIDER_DAY_OFF_REQUESTS (", design);
        Assert.Contains("CONSTRAINT CK_PROVIDER_DAY_OFF_REQUESTS_STATUS CHECK (" + ServisyoDbContext.DayOffStatusCheck + ")", design);
        Assert.Contains("CONSTRAINT CK_PROVIDER_DAY_OFF_REQUESTS_DECISION CHECK (" + ServisyoDbContext.DayOffDecisionCheck + ")", design);
        Assert.Contains("CONSTRAINT CK_PROVIDER_UNAVAILABLE_DATES_SOURCE CHECK (" + ServisyoDbContext.DayOffSourceCheck + ")", design);
        // the same objects as the migration (the migration only wraps them in EXEC); v2.8 changed usp_GetNotifications
        // again, so the design file has ITS version (NameChangeRequestTests checks that one)
        foreach (var sql in DayOffRequestsSql.CreateAll.Where(s => !s.Contains("PROCEDURE dbo.usp_GetNotifications")))
        {
            var body = sql["EXEC(N'".Length..sql.LastIndexOf("')", StringComparison.Ordinal)].Replace("''", "'");
            Assert.Contains(body.Replace("\r\n", "\n").Trim(), design);
        }
    }
}
