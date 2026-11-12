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

/// Every provider has their OWN days off: the list, the requests, the same-day rule, the booked-day rule and the
/// administrator's extra days all look only at the signed-in provider (PROVIDER_UNAVAILABLE_DATES.PROVIDER_ID,
/// worked out from the sign-in cookie - never sent by the browser).
/// The ONE thing shared by all providers (database v2.5): only one provider can be off on any date, first come first
/// served - and since v2.7 a Pending request already holds its dates.
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

    private static DateOnly Nov(int day) => new(2026, 11, day);

    private static Task<DayOffResult> AskAsync(World w, Provider p, DateOnly first, DateOnly? last = null) =>
        w.Schedule.RequestDayOffAsync(p.UserId, new DayOffRequestForm { Start = first, End = last, Reason = "Leave" });

    /// Approves the provider's waiting request, so they can send the next one (one request at a time).
    private static async Task ApproveAsync(World w, Provider p)
    {
        var id = await w.Db.DayOffRequests.Where(r => r.ProviderId == p.ProviderId && r.Status == DayOffStatuses.Pending).Select(r => r.RequestId).SingleAsync();
        Assert.Equal(AdminDayOffOutcome.Success, (await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, p.ProviderId, id, approve: true)).Outcome);
    }

    private static async Task<IReadOnlyList<string>> ListOf(World w, Provider p) =>
        (await w.Schedule.GetAsync(p.UserId)).Page!.DaysOff.Select(d => d.DatesText).ToList();

    [Fact]
    public async Task EachProvider_SeesOnlyTheirOwnDaysOff()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, w.Vince, Nov(13))).Done);
        Assert.True((await AskAsync(w, w.Liza, Nov(20), Nov(21))).Done);

        Assert.Equal(new[] { "Nov 13, 2026" }, await ListOf(w, w.Vince));
        Assert.Equal(new[] { "Nov 20 to Nov 21, 2026" }, await ListOf(w, w.Liza));
    }

    // ------------------------------------------------------------ only one provider off per day

    private const string Taken = "Another provider already has this day off. Only one provider can be off per day, so please choose another date.";

    [Fact]
    public async Task TwoProviders_CannotAskForTheSameDay_FirstComeFirstServed()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, w.Liza, Nov(14))).Done);                    // Liza (like Angel) was first - still Pending

        var result = await AskAsync(w, w.Vince, Nov(13), Nov(15));

        Assert.False(result.Done);
        Assert.Equal(new[] { $"Nov 14, 2026: {Taken}" }, result.Errors);
        Assert.Equal(1, await w.Db.ProviderUnavailableDates.CountAsync(d => d.UnavailableDate == Nov(14)));
        Assert.Empty(await ListOf(w, w.Vince));
        Assert.Empty(w.Db.DayOffRequests.Where(r => r.ProviderId == w.Vince.ProviderId));

        Assert.True((await AskAsync(w, w.Vince, Nov(15))).Done);                   // another day is fine
    }

    [Fact]
    public async Task ADayFreedByOneProvider_CanBeTakenByAnother()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, w.Liza, Nov(14))).Done);
        var id = (await w.Db.DayOffRequests.SingleAsync()).RequestId;
        Assert.True((await w.Schedule.RemoveRequestAsync(w.Liza.UserId, id)).Done);    // Liza cancels it

        Assert.True((await AskAsync(w, w.Vince, Nov(14))).Done);
    }

    [Fact]
    public async Task ARejectedRequest_GivesItsDaysBack()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, w.Liza, Nov(14), Nov(16))).Done);
        var id = (await w.Db.DayOffRequests.SingleAsync()).RequestId;
        Assert.Equal(AdminDayOffOutcome.Success, (await w.Admin.DecideDayOffRequestAsync(w.AdminUser.UserId, w.Liza.ProviderId, id, approve: false)).Outcome);

        Assert.True((await AskAsync(w, w.Vince, Nov(15))).Done);
    }

    [Fact]
    public async Task ThePage_KnowsWhichDaysAreTakenByOthers_ButNotByWhom()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, w.Liza, Nov(14))).Done);
        await ApproveAsync(w, w.Liza);
        Assert.True((await AskAsync(w, w.Liza, Nov(20))).Done);                     // pending: it holds the date too
        Assert.True((await AskAsync(w, w.Vince, Nov(16))).Done);

        var vince = (await w.Schedule.GetAsync(w.Vince.UserId)).Page!;
        Assert.Equal(new[] { Nov(14), Nov(20) }, vince.TakenDates);                 // not his own Nov 16
        Assert.Equal(new[] { Nov(16) }, (await w.Schedule.GetAsync(w.Liza.UserId)).Page!.TakenDates);
    }

    [Fact]
    public async Task AnAdministrator_CannotGiveADayAnotherProviderHasOff()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, w.Liza, Nov(14))).Done);

        var result = await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(14), "Sick");

        Assert.Equal(ExtraDayOffOutcome.Invalid, result.Outcome);
        Assert.Equal("Liza Reyes already has Nov 14, 2026 off. Only one provider can be off per day, so Vincent Reyes cannot be given that day.", result.Message);
        Assert.Equal(1, await w.Db.ProviderUnavailableDates.CountAsync());
        Assert.Empty(w.Db.AdminActions);
    }

    [Fact]
    public async Task ADayAnAdministratorGave_IsTakenForEveryoneElse()
    {
        var w = await WorldAsync();
        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Vince.ProviderId, Nov(14), "Sick")).Outcome);

        var result = await AskAsync(w, w.Liza, Nov(14));

        Assert.False(result.Done);
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
    public async Task AnotherProvidersRequest_DoesNotCountAsMine()
    {
        // "one request at a time" is per provider: Liza's waiting request does not stop Vince from asking
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, w.Liza, Nov(9), Nov(13))).Done);

        Assert.True((await AskAsync(w, w.Vince, Nov(2), Nov(6))).Done);
        Assert.Equal(UnavailableDateRules.PendingLabel, (await w.Schedule.GetAsync(w.Liza.UserId)).Page!.DaysOff.Single().Badge);
        Assert.Equal(UnavailableDateRules.PendingLabel, (await w.Schedule.GetAsync(w.Vince.UserId)).Page!.DaysOff.Single().Badge);
    }

    [Fact]
    public async Task AProviderCannotCancelOrRemove_AnotherProvidersDaysOff()
    {
        var w = await WorldAsync();
        Assert.True((await AskAsync(w, w.Liza, Nov(20))).Done);
        var request = await w.Db.DayOffRequests.SingleAsync();
        w.Db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = w.Liza.ProviderId, UnavailableDate = Nov(25), Reason = "Old" });
        await w.Db.SaveChangesAsync();
        var single = await w.Db.ProviderUnavailableDates.SingleAsync(d => d.UnavailableDate == Nov(25));

        Assert.False((await w.Schedule.RemoveRequestAsync(w.Vince.UserId, request.RequestId)).Done);   // Vince guesses Liza's ids
        Assert.False((await w.Schedule.RemoveDayAsync(w.Vince.UserId, single.UnavailableId)).Done);

        Assert.Equal(new[] { "Nov 20, 2026", "Nov 25, 2026" }, await ListOf(w, w.Liza));
    }

    [Fact]
    public async Task AnotherProvidersDayOffForToday_DoesNotLetMeTakeToday()
    {
        var w = await WorldAsync();
        w.Db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = w.Liza.ProviderId, UnavailableDate = Today, Reason = "Planned" });
        await w.Db.SaveChangesAsync();

        var result = await AskAsync(w, w.Vince, Today);

        Assert.False(result.Done);
        Assert.Contains("Oct 14, 2026: Same-day unavailable date selection is not permitted. Unavailable dates must be marked at least 1 day in advance.", result.Errors);
        Assert.Empty(await ListOf(w, w.Vince));
        Assert.Equal(new[] { "Oct 14, 2026" }, await ListOf(w, w.Liza));                  // Liza's own saved day stays
    }

    [Fact]
    public async Task AnExtraDayGivenToOneProvider_IsOnlyTheirs()
    {
        var w = await WorldAsync();
        Assert.Equal(ExtraDayOffOutcome.Success, (await w.Admin.ApproveExtraDayOffAsync(w.AdminUser.UserId, w.Liza.ProviderId, Nov(20), "Sick")).Outcome);

        Assert.Empty(await ListOf(w, w.Vince));
        Assert.Equal(UnavailableDateRules.ApprovedNote, (await w.Schedule.GetAsync(w.Liza.UserId)).Page!.DaysOff.Single().Note);

        // Liza's day is taken for Vince (one provider per day), not an exemption for him
        var result = await AskAsync(w, w.Vince, Nov(18), Nov(21));
        Assert.False(result.Done);
        Assert.Equal(new[] { $"Nov 20, 2026: {Taken}" }, result.Errors);
    }

    [Fact]
    public void TheFormsHaveNoProviderOrUserId_ThatIsAlwaysTheSignedInAccount()
    {
        foreach (var type in new[] { typeof(AvailabilityForm), typeof(DayScheduleForm), typeof(BreakForm), typeof(DayOffRequestForm), typeof(DayOffDecisionForm) })
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
