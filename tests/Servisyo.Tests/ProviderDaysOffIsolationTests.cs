using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Every provider has their OWN days off: the list, the 5-a-month count, the same-day rule, the booked-day rule and
/// the administrator's extra days all look only at the signed-in provider (PROVIDER_UNAVAILABLE_DATES.PROVIDER_ID,
/// worked out from the sign-in cookie - never sent by the browser).
/// The ONE thing shared by all providers (database v2.5): only one provider can be off on any date, first come first served.
public class ProviderDaysOffIsolationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);     // Oct 14, 10:00 in Cebu
    private static readonly DateOnly Today = new(2026, 10, 14);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record World(ServisyoDbContext Db, ProviderAvailabilityService Schedule, ProviderManagementService Admin,
        User AdminUser, Provider Vince, Provider Liza);

    private static User NewUser(string first, string role) => new()
    {
        FirstName = first, LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = AccountStatuses.Active
    };

    private static async Task<World> WorldAsync()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", Roles.Administrator);
        var vince = new Provider { BusinessName = "Vincent Works", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Vincent", Roles.ServiceProvider) };
        var liza = new Provider { BusinessName = "Liza Cleans", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Liza", Roles.ServiceProvider) };
        db.Users.Add(admin);
        db.Providers.AddRange(vince, liza);
        await db.SaveChangesAsync();
        return new(db, new ProviderAvailabilityService(db, new FixedClock()), new ProviderManagementService(db, null, new FixedClock()), admin, vince, liza);
    }

    private static AvailabilityForm Week(params DateOnly[] daysOff)
    {
        var form = new AvailabilityForm { MinNoticeDays = 1, MaxJobsPerDay = 4 };
        foreach (var dow in AvailabilityDays.DisplayOrder)
            form.Days.Add(new DayScheduleForm { DayOfWeek = dow, IsAvailable = dow is >= 1 and <= 5, Start = new TimeOnly(8, 0), End = new TimeOnly(17, 0) });
        foreach (var d in daysOff) form.Dates.Add(new UnavailableDateForm { Date = d, Reason = "Leave" });
        return form;
    }

    private static DateOnly Nov(int day) => new(2026, 11, day);

    private static async Task<IReadOnlyList<DateOnly?>> ListOf(World w, Provider p) =>
        (await w.Schedule.GetAsync(p.UserId)).Page!.Form.Dates.Select(d => d.Date).ToList();

    [Fact]
    public async Task EachProvider_SeesOnlyTheirOwnDaysOff()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(13)))).Saved);
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(20)))).Saved);

        Assert.Equal(new DateOnly?[] { Nov(13) }, await ListOf(w, w.Vince));
        Assert.Equal(new DateOnly?[] { Nov(20) }, await ListOf(w, w.Liza));
    }

    // ------------------------------------------------------------ only one provider off per day

    private const string Taken = "Another provider already has this day off. Only one provider can be off per day, so please choose another date.";

    [Fact]
    public async Task TwoProviders_CannotTakeTheSameDayOff_FirstComeFirstServed()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(14)))).Saved);              // Liza (like Angel) was first

        var result = await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(14)));

        Assert.False(result.Saved);
        Assert.Equal(new[] { $"Nov 14, 2026: {Taken}" }, result.Errors);
        Assert.Equal(1, await w.Db.ProviderUnavailableDates.CountAsync(d => d.UnavailableDate == Nov(14)));
        Assert.Empty(await ListOf(w, w.Vince));

        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(15)))).Saved);            // another day is fine
    }

    [Fact]
    public async Task ADayFreedByOneProvider_CanBeTakenByAnother()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(14)))).Saved);
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week())).Saved);                     // Liza removes it

        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(14)))).Saved);
    }

    [Fact]
    public async Task MyOwnSavedDay_StaysMine_WhenISaveAgain()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(14)))).Saved);

        var page = (await w.Schedule.GetAsync(w.Liza.UserId)).Page!;
        page.Form.Dates[0].Reason = "Family trip";
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, page.Form)).Saved);                  // not "taken" by herself
        Assert.Equal("Family trip", (await w.Db.ProviderUnavailableDates.SingleAsync()).Reason);
    }

    [Fact]
    public async Task ThePage_KnowsWhichDaysAreTakenByOthers_ButNotByWhom()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(14), Nov(20)))).Saved);
        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(16)))).Saved);

        var vince = (await w.Schedule.GetAsync(w.Vince.UserId)).Page!;
        Assert.Equal(new[] { Nov(14), Nov(20) }, vince.TakenDates);                                  // not his own Nov 16
        Assert.Equal(new[] { Nov(16) }, (await w.Schedule.GetAsync(w.Liza.UserId)).Page!.TakenDates);
    }

    [Fact]
    public async Task AnAdministrator_CannotApproveADayAnotherProviderHasOff()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(14)))).Saved);

        var result = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(14), "Sick");

        Assert.Equal(ExtraDayOffOutcome.Invalid, result.Outcome);
        Assert.Equal("Liza Reyes already has Nov 14, 2026 off. Only one provider can be off per day, so Vincent Reyes cannot be given that day.", result.Message);
        Assert.Equal(1, await w.Db.ProviderUnavailableDates.CountAsync());
        Assert.Empty(w.Db.AdminActions);
    }

    [Fact]
    public async Task ADayAnAdministratorApproved_IsTakenForEveryoneElse()
    {
        var w = await WorldAsync();
        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(14), "Sick")).Outcome);

        var result = await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(14)));

        Assert.False(result.Saved);
        Assert.Contains($"Nov 14, 2026: {Taken}", result.Errors);
    }

    [Fact]
    public void TheDatabase_AllowsOnlyOneProviderPerDate()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.FindEntityType(typeof(ProviderUnavailableDate))!;
        var byDate = Assert.Single(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ProviderUnavailableDate.UnavailableDate) }));
        Assert.True(byDate.IsUnique);
        Assert.Equal("IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE", byDate.GetDatabaseName());
    }

    [Fact]
    public async Task AnotherProvidersDays_DoNotCountTowardMyFive()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(2), Nov(3), Nov(4), Nov(5), Nov(6)))).Saved);

        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(9), Nov(10), Nov(11), Nov(12), Nov(13)))).Saved);
    }

    [Fact]
    public async Task AnotherProvidersPastDaysThisMonth_DoNotCountForMe()
    {
        var w = await WorldAsync();
        w.Db.ProviderUnavailableDates.AddRange(
            new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = new DateOnly(2026, 10, 1) },
            new ProviderUnavailableDate { ProviderId = w.Vince.ProviderId, UnavailableDate = new DateOnly(2026, 10, 2) });
        await w.Db.SaveChangesAsync();

        Assert.Equal(2, (await w.Schedule.GetAsync(w.Vince.UserId)).Page!.PastDaysThisMonth);
        Assert.Equal(0, (await w.Schedule.GetAsync(w.Liza.UserId)).Page!.PastDaysThisMonth);
    }

    [Fact]
    public async Task SavingMyList_NeverRemovesAnotherProvidersDays()
    {
        var w = await WorldAsync();
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Nov(20)))).Saved);

        Assert.True((await w.Schedule.SaveAsync(w.Vince.UserId, Week())).Saved);           // Vince's list is empty

        Assert.Equal(new DateOnly?[] { Nov(20) }, await ListOf(w, w.Liza));
    }

    [Fact]
    public async Task AnotherProvidersDayOffForToday_DoesNotLetMeTakeToday()
    {
        // the same-day rule lets a day saved EARLIER stay - but only the provider's own one
        var w = await WorldAsync();
        w.Db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = w.Liza.ProviderId, UnavailableDate = Today, Reason = "Planned" });
        await w.Db.SaveChangesAsync();

        var result = await w.Schedule.SaveAsync(w.Vince.UserId, Week(Today));

        Assert.False(result.Saved);
        Assert.Contains("Oct 14, 2026: Same-day unavailable date selection is not permitted. Unavailable dates must be marked at least 1 day in advance.", result.Errors);
        Assert.Empty(await ListOf(w, w.Vince));
        Assert.True((await w.Schedule.SaveAsync(w.Liza.UserId, Week(Today))).Saved);       // Liza's own saved day stays
    }

    [Fact]
    public async Task AnExtraDayApprovedForOneProvider_IsOnlyTheirs()
    {
        var w = await WorldAsync();
        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Liza.ProviderId, Nov(20), "Sick")).Outcome);

        Assert.Empty(await ListOf(w, w.Vince));
        Assert.True((await w.Schedule.GetAsync(w.Liza.UserId)).Page!.Form.Dates.Single().ApprovedByAdmin);

        // Liza's approved day is taken for Vince (one provider per day), not an exemption for him
        var six = await w.Schedule.SaveAsync(w.Vince.UserId, Week(Nov(2), Nov(3), Nov(4), Nov(5), Nov(6), Nov(20)));
        Assert.False(six.Saved);
        Assert.Equal(new[] { $"Nov 20, 2026: {Taken}" }, six.Errors);
    }

    [Fact]
    public void TheFormsHaveNoProviderOrUserId_ThatIsAlwaysTheSignedInAccount()
    {
        foreach (var type in new[] { typeof(AvailabilityForm), typeof(DayScheduleForm), typeof(BreakForm), typeof(UnavailableDateForm) })
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                p => p.Name.Contains("ProviderId") || p.Name.Contains("UserId"));
    }

    // ------------------------------------------------------------ the browser never keeps a signed-in page

    [Fact]
    public void ASignedInPage_IsSentWithNoStore()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Cookies"))
        };
        PrivatePages.MarkNoStore(context);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void APublicPage_OrOneThatSetsItsOwnCaching_IsLeftAlone()
    {
        var visitor = new DefaultHttpContext();                                                  // not signed in
        PrivatePages.MarkNoStore(visitor);
        Assert.False(visitor.Response.Headers.ContainsKey("Cache-Control"));

        var asset = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Cookies"))
        };
        asset.Response.Headers.CacheControl = "max-age=31536000, immutable";                    // e.g. site.css
        PrivatePages.MarkNoStore(asset);
        Assert.Equal("max-age=31536000, immutable", asset.Response.Headers.CacheControl.ToString());
    }
}
