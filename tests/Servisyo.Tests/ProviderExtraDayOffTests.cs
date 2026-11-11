using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Days off and bookings (database v2.4):
///   - a day with an active booking (Pending, Accepted, In Progress) cannot be marked unavailable, so no customer is
///     left without their provider - by the provider (Availability) or by an administrator;
///   - an administrator can approve an EXTRA day off (an emergency beyond the 5 a month). It is saved with an
///     ADMIN_ACTIONS row and points to it (APPROVED_BY_ACTION_ID); it does not count toward the provider's 5.
public class ProviderExtraDayOffTests
{
    // 10:00 in Cebu on Wed Oct 14, 2026
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record World(ServisyoDbContext Db, ProviderManagementService Admin, ProviderAvailabilityService Schedule,
        User AdminUser, Provider Vince, Customer Ana);

    private static User NewUser(string first, string role, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status
    };

    private static async Task<World> WorldAsync(string approval = ApprovalStatuses.Approved)
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", Roles.Administrator);
        var vince = new Provider { BusinessName = "Vincent Works", ApprovalStatus = approval, User = NewUser("Vincent", Roles.ServiceProvider) };
        var ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        db.Users.Add(admin);
        db.Providers.Add(vince);
        db.Customers.Add(ana);
        await db.SaveChangesAsync();
        return new(db, new ProviderManagementService(db, null, new FixedClock()), new ProviderAvailabilityService(db, new FixedClock()), admin, vince, ana);
    }

    private static async Task<Booking> BookAsync(World w, DateOnly day, string status, Provider? provider = null)
    {
        var b = new Booking
        {
            CustomerId = w.Ana.CustomerId, ProviderId = (provider ?? w.Vince).ProviderId, BookingDate = day, BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 500m, Status = status, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        };
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        return b;
    }

    private static TimeOnly T(int h) => new(h, 0);

    /// Monday-Friday 8:00-17:00, plus these days off.
    private static AvailabilityForm Week(params DateOnly[] daysOff)
    {
        var form = new AvailabilityForm { MinNoticeDays = 1, MaxJobsPerDay = 4 };
        foreach (var dow in AvailabilityDays.DisplayOrder)
            form.Days.Add(new DayScheduleForm { DayOfWeek = dow, IsAvailable = dow is >= 1 and <= 5, Start = T(8), End = T(17) });
        foreach (var d in daysOff) form.Dates.Add(new UnavailableDateForm { Date = d, Reason = "Leave" });
        return form;
    }

    private static DateOnly Nov(int day) => new(2026, 11, day);

    // ------------------------------------------------------------ the provider's own days off vs bookings

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task AProvider_CannotMarkADayOff_ThatHasAnActiveBooking(string status)
    {
        var w = await WorldAsync();
        var b = await BookAsync(w, Nov(20), status);

        var result = await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(20)));

        Assert.False(result.Saved);
        Assert.Contains($"Nov 20, 2026: you already have booking #{b.BookingId} ({status}) on this day. A day with an active booking cannot be marked unavailable.", result.Errors);
        Assert.Empty(w.Db.ProviderUnavailableDates);
    }

    [Theory]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.Rejected)]
    public async Task AFinishedBooking_DoesNotBlockADayOff(string status)
    {
        var w = await WorldAsync();
        await BookAsync(w, Nov(20), status);

        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(20)))).Saved);
    }

    [Fact]
    public async Task AnotherProvidersBooking_DoesNotBlockMyDayOff()
    {
        var w = await WorldAsync();
        var other = new Provider { BusinessName = "Other", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Liza", Roles.ServiceProvider) };
        w.Db.Providers.Add(other);
        await w.Db.SaveChangesAsync();
        await BookAsync(w, Nov(20), BookingStatuses.Accepted, other);

        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(20)))).Saved);
    }

    [Fact]
    public async Task ADayOffSavedEarlier_IsKept_EvenIfABookingIsOnIt()
    {
        // saved before this rule existed: saving the schedule again must still work (only NEW days are checked)
        var w = await WorldAsync();
        w.Db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = Nov(20), Reason = "Old" });
        await w.Db.SaveChangesAsync();
        await BookAsync(w, Nov(20), BookingStatuses.Accepted);

        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(20)))).Saved);
    }

    // ------------------------------------------------------------ the administrator's extra day off

    [Fact]
    public async Task Approve_SavesTheDayOff_PointingToTheAdminLog()
    {
        var w = await WorldAsync();

        var result = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "  Sick (called the office)  ");

        Assert.Equal(ExtraDayOffOutcome.Success, result.Outcome);
        Assert.Equal("Extra day off approved for Vincent Reyes on Nov 20, 2026. It does not count toward their 5 days that month.", result.Message);

        var log = await w.Db.AdminActions.AsNoTracking().SingleAsync();
        Assert.Equal(ProviderManagementService.ExtraDayOffActionType, log.ActionType);
        Assert.Equal(AdminTargetTypes.ServiceProvider, log.TargetType);
        Assert.Equal(w.Vince.ProviderId, log.TargetProviderId);
        Assert.Equal(w.AdminUser.UserId, log.AdminUserId);
        Assert.Equal("Nov 20, 2026: Sick (called the office)", log.Reason);

        var day = await w.Db.ProviderUnavailableDates.AsNoTracking().SingleAsync();
        Assert.Equal(Nov(20), day.UnavailableDate);
        Assert.Equal("Sick (called the office)", day.Reason);
        Assert.Equal(log.ActionId, day.ApprovedByActionId);
    }

    [Fact]
    public async Task AnApprovedExtraDay_DoesNotCountTowardTheProvidersFive()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(2), Nov(3), Nov(4), Nov(5), Nov(6)))).Saved);     // 5 of their own
        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "Sick")).Outcome);

        var page = (await w.Schedule.GetAsync(w.Vince.UserId)).Page!;
        Assert.Equal(6, page.Form.Dates.Count);
        Assert.True(page.Form.Dates.Single(d => d.Date == Nov(20)).ApprovedByAdmin);

        // saving the page again (all 6 listed) still works; a 6th day of their own does not
        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, page.Form)).Saved);
        Assert.Equal(Nov(20), (await w.Db.ProviderUnavailableDates.AsNoTracking().SingleAsync(d => d.ApprovedByActionId != null)).UnavailableDate);

        var seventh = await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(2), Nov(3), Nov(4), Nov(5), Nov(6), Nov(20), Nov(25)));
        Assert.False(seventh.Saved);
        Assert.Contains("You can only set up to 5 unavailable days per month. November 2026 would have 6.", seventh.Errors);
    }

    [Fact]
    public async Task TheApprovedMark_NeverComesFromTheBrowser()
    {
        var w = await WorldAsync();
        var form = Week(Nov(2), Nov(3), Nov(4), Nov(5), Nov(6), Nov(9));
        form.Dates[5].ApprovedByAdmin = true;                    // pretending the 6th day was approved

        var result = await w.Schedule.SaveAsync(w.Vince.UserId, form);

        Assert.False(result.Saved);
        Assert.Contains("You can only set up to 5 unavailable days per month. November 2026 would have 6.", result.Errors);
    }

    [Fact]
    public async Task AnApprovedDayEarlierThisMonth_IsNotCountedEither()
    {
        var w = await WorldAsync();                                                   // today: Oct 14
        var log = AdminAction.ForProvider(w.AdminUser.UserId, ProviderManagementService.ExtraDayOffActionType, w.Vince.ProviderId, "Oct 2, 2026: Sick");
        w.Db.ProviderUnavailableDates.AddRange(
            new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = new DateOnly(2026, 10, 2), ApprovedBy = log },
            new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = new DateOnly(2026, 10, 5) });
        await w.Db.SaveChangesAsync();

        Assert.Equal(1, (await w.Schedule.GetAsync(w.Vince.UserId)).Page!.PastDaysThisMonth);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task Approve_IsRefused_OnADayWithAnActiveBooking(string status)
    {
        var w = await WorldAsync();
        var b = await BookAsync(w, Nov(20), status);

        var result = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "Sick");

        Assert.Equal(ExtraDayOffOutcome.Invalid, result.Outcome);
        Assert.Contains($"booking #{b.BookingId} ({status}) on Nov 20, 2026", result.Message);
        Assert.Empty(w.Db.ProviderUnavailableDates);
        Assert.Empty(w.Db.AdminActions);                                             // nothing half-saved
    }

    [Fact]
    public async Task Approve_ADayThatIsAlreadyOff_IsRefused()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(20)))).Saved);

        var own = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "Sick");
        Assert.Equal("Vincent Reyes has already marked Nov 20, 2026 as a day off.", own.Message);

        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(23), "Sick")).Outcome);
        var twice = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(23), "Sick");
        Assert.Equal("An extra day off on Nov 23, 2026 was already approved for Vincent Reyes.", twice.Message);
        Assert.Equal(1, await w.Db.AdminActions.CountAsync());
    }

    [Theory]
    [InlineData(null, "Sick", "Please choose the day off.")]
    [InlineData("2026-10-13", "Sick", "Oct 13, 2026 is in the past.")]
    [InlineData("2027-01-04", "Sick", "Jan 4, 2027 is not in 2026.")]
    [InlineData("2026-11-20", null, "Please give the reason")]
    [InlineData("2026-11-20", "   ", "Please give the reason")]
    public async Task Approve_BadInput_IsRefused_AndNothingIsSaved(string? date, string? reason, string expected)
    {
        var w = await WorldAsync();

        var result = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId,
            date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), reason);

        Assert.Equal(ExtraDayOffOutcome.Invalid, result.Outcome);
        Assert.StartsWith(expected, result.Message);
        Assert.Empty(w.Db.ProviderUnavailableDates);
        Assert.Empty(w.Db.AdminActions);
    }

    [Fact]
    public async Task Approve_ATooLongReason_IsRefused()
    {
        var w = await WorldAsync();
        var result = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20),
            new string('x', ProviderManagementService.MaxExtraDayReasonLength + 1));
        Assert.Equal("The reason can be at most 200 characters.", result.Message);
    }

    [Fact]
    public async Task Approve_Dec31_IsTheLastDay()
    {
        var w = await WorldAsync();
        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, new DateOnly(2026, 12, 31), "Family")).Outcome);
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanApprove()
    {
        var w = await WorldAsync();
        var suspendedAdmin = NewUser("Old", Roles.Administrator, AccountStatuses.Suspended);
        w.Db.Users.Add(suspendedAdmin);
        await w.Db.SaveChangesAsync();

        foreach (var who in new[] { w.Vince.UserId, w.Ana.UserId, suspendedAdmin.UserId, 99999 })
            Assert.Equal(ExtraDayOffOutcome.NotAllowed, (await w.Admin.ApproveExtraDayOffAsync(who, w.Vince.ProviderId, Nov(20), "Sick")).Outcome);
        Assert.Empty(w.Db.ProviderUnavailableDates);
    }

    [Fact]
    public async Task Approve_ForAProviderThatIsNotApproved_OrDoesNotExist_IsRefused()
    {
        var w = await WorldAsync(ApprovalStatuses.Pending);

        Assert.Equal(ExtraDayOffOutcome.Invalid, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "Sick")).Outcome);
        Assert.Equal(ExtraDayOffOutcome.NotFound, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, 99999, Nov(20), "Sick")).Outcome);
        Assert.Empty(w.Db.ProviderUnavailableDates);
    }

    [Fact]
    public async Task TheAdminPopUp_ListsTheDaysOff_AndOffersTheForm()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(3)))).Saved);
        await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "Sick");

        var d = (await w.Admin.GetDetailsAsync(w.AdminUser.UserId, w.Vince.ProviderId)).Details!;

        Assert.Equal(new[] { ("Nov 3, 2026", false), ("Nov 20, 2026", true) }, d.DaysOff.Select(x => (x.DateText, x.ApprovedByAdmin)));
        Assert.True(d.CanApproveDayOff);
        Assert.Equal("2026-10-14", d.DayOffMin);
        Assert.Equal("2026-12-31", d.DayOffMax);
    }

    [Fact]
    public void TheModel_LinksAnApprovedDayToItsAdminLog()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ProviderUnavailableDate))!;   // with the column names
        var column = entity.FindProperty(nameof(ProviderUnavailableDate.ApprovedByActionId))!;
        Assert.Equal("APPROVED_BY_ACTION_ID", column.GetColumnName());
        Assert.True(column.IsNullable);
        var fk = Assert.Single(entity.GetForeignKeys(), f => f.PrincipalEntityType.ClrType == typeof(AdminAction));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);                 // the admin history is never deleted with it
    }

    // ------------------------------------------------------------ no same-day requests, together with the other rules

    [Fact]
    public async Task TheSameDayRule_WorksTogetherWithTheOtherDateRules()
    {
        var w = await WorldAsync();                                                   // today: Oct 14, 2026
        var b = await BookAsync(w, Nov(20), BookingStatuses.Accepted);
        var form = Week(new DateOnly(2026, 10, 14),                                   // today
                        Nov(2), Nov(3), Nov(4), Nov(5), Nov(6), Nov(9),             // 6 in November
                        Nov(20),                                                      // booked
                        new DateOnly(2027, 1, 5));                                    // next year

        var result = await w.Schedule.SaveAsync(w.Vince.UserId, form);

        Assert.False(result.Saved);
        Assert.Contains("Oct 14, 2026: Same-day unavailable date selection is not permitted. Unavailable dates must be marked at least 1 day in advance.", result.Errors);
        Assert.Contains($"Nov 20, 2026: you already have booking #{b.BookingId} (Accepted) on this day. A day with an active booking cannot be marked unavailable.", result.Errors);
        Assert.Contains(result.Errors, e => e.StartsWith("Jan 5, 2027 is not in 2026."));
        Assert.Contains("You can only set up to 5 unavailable days per month. November 2026 would have 6.", result.Errors);
        Assert.Equal(4, result.Errors.Count);
        Assert.Empty(w.Db.ProviderUnavailableDates);
    }

    [Fact]
    public async Task AnAdministrator_CanStillApproveToday_AsAnEmergency()
    {
        var w = await WorldAsync();
        var today = new DateOnly(2026, 10, 14);

        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, today, "Sick this morning")).Outcome);

        // the provider's page keeps it, and saving the schedule still works
        var page = (await w.Schedule.GetAsync(w.Vince.UserId)).Page!;
        var row = Assert.Single(page.Form.Dates);
        Assert.True(row.AlreadySaved && row.ApprovedByAdmin);
        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, page.Form)).Saved);
    }
}
