using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Days off and bookings (database v2.4, requests since v2.7):
///   - a day with an active booking (Pending, Accepted, In Progress) cannot be requested, so no customer is left
///     without their provider - by the provider (Request Day Off) or by an administrator;
///   - an administrator can give an EXTRA day off at once (an emergency, even today). It is saved with an
///     ADMIN_ACTIONS row and points to it (APPROVED_BY_ACTION_ID).
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

    private static DateOnly Nov(int day) => new(2026, 11, day);

    private static Task<DayOffResult> AskAsync(World w, DateOnly first, DateOnly? last = null) =>
        w.Schedule.RequestDayOffAsync(w.Vince.UserId, new DayOffRequestForm { Start = first, End = last, Reason = "Leave" });

    // ------------------------------------------------------------ the provider's own days off vs bookings

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task AProvider_CannotRequestADayOff_ThatHasAnActiveBooking(string status)
    {
        var w = await WorldAsync();
        var b = await BookAsync(w, Nov(20), status);

        var result = await AskAsync(w, Nov(19), Nov(21));

        Assert.False(result.Done);
        Assert.Equal(new[] { $"Nov 20, 2026: you already have booking #{b.BookingId} ({status}) on this day. A day with an active booking cannot be marked unavailable." }, result.Errors);
        Assert.Empty(w.Db.ProviderUnavailableDates);
        Assert.Empty(w.Db.DayOffRequests);
    }

    [Theory]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.Rejected)]
    public async Task AFinishedBooking_DoesNotBlockADayOff(string status)
    {
        var w = await WorldAsync();
        await BookAsync(w, Nov(20), status);

        Assert.True((await AskAsync(w, Nov(20))).Done);
    }

    [Fact]
    public async Task AnotherProvidersBooking_DoesNotBlockMyDayOff()
    {
        var w = await WorldAsync();
        var other = new Provider { BusinessName = "Other", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Liza", Roles.ServiceProvider) };
        w.Db.Providers.Add(other);
        await w.Db.SaveChangesAsync();
        await BookAsync(w, Nov(20), BookingStatuses.Accepted, other);

        Assert.True((await AskAsync(w, Nov(20))).Done);
    }

    // ------------------------------------------------------------ the administrator's extra day off

    [Fact]
    public async Task Approve_SavesTheDayOff_PointingToTheAdminLog()
    {
        var w = await WorldAsync();

        var result = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "  Sick (called the office)  ");

        Assert.Equal(ExtraDayOffOutcome.Success, result.Outcome);
        Assert.Equal("Extra day off approved for Vincent Reyes on Nov 20, 2026. Customers cannot book them on that day.", result.Message);

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
    public async Task Approve_ADayThatIsAlreadyOff_OrAskedFor_IsRefused()
    {
        var w = await WorldAsync();
        w.Db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = Nov(20), Reason = "Old" });
        await w.Db.SaveChangesAsync();
        Assert.True((await AskAsync(w, Nov(26))).Done);

        var own = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "Sick");
        Assert.Equal("Vincent Reyes has already marked Nov 20, 2026 as a day off.", own.Message);
        var asked = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(26), "Sick");
        Assert.Equal("Vincent Reyes already asked for Nov 26, 2026 off. Approve their request in the Days Off list instead.", asked.Message);

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
        w.Db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = Nov(3), Reason = "Old" });
        await w.Db.SaveChangesAsync();
        Assert.True((await AskAsync(w, Nov(24), Nov(26))).Done);
        await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(20), "Sick");

        var d = (await w.Admin.GetDetailsAsync(w.AdminUser.UserId, w.Vince.ProviderId)).Details!;

        Assert.Equal(new[] { ("Nov 3, 2026", "Day off"), ("Nov 20, 2026", ProviderDayOffLine.ExtraLabel), ("Nov 24 to Nov 26, 2026", UnavailableDateRules.PendingLabel) },
            d.DaysOff.Select(x => (x.DateText, x.Label)));
        Assert.Equal(1, d.PendingRequests);
        Assert.NotNull(d.DaysOff[2].RequestId);
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
    public async Task AnAdministrator_CanStillGiveToday_AsAnEmergency()
    {
        var w = await WorldAsync();
        var today = new DateOnly(2026, 10, 14);

        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, today, "Sick this morning")).Outcome);

        // the provider's page lists it, marked as given by an administrator (a provider cannot request today)
        var item = Assert.Single((await w.Schedule.GetAsync(w.Vince.UserId)).Page!.DaysOff);
        Assert.Equal("Oct 14, 2026", item.DatesText);
        Assert.Equal(UnavailableDateRules.ApprovedNote, item.Note);
        Assert.False((await AskAsync(w, today)).Done);
    }
}
