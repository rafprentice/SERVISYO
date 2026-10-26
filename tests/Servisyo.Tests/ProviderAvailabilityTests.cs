using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ProviderAvailabilityServiceTests
{
    // 10:00 in Cebu on Wed Oct 14, 2026
    private static readonly DateTimeOffset DefaultNow = new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 14);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static (ProviderAvailabilityService svc, ServisyoDbContext db) Create(DateTimeOffset? now = null)
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new ProviderAvailabilityService(db, new FixedClock(now ?? DefaultNow)), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string approval = ApprovalStatuses.Approved,
        string account = AccountStatuses.Active, string first = "Marco", string last = "Reyes", string business = "Marco's Plumbing Works")
    {
        var p = new Provider { BusinessName = business, ApprovalStatus = approval, User = NewUser(first, last, Roles.ServiceProvider, account) };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static TimeOnly T(int h, int m = 0) => new(h, m);

    /// A valid week: Monday-Friday 8:00-17:00 open, Saturday and Sunday closed, notice 1 day, 4 jobs.
    private static AvailabilityForm ValidForm()
    {
        var form = new AvailabilityForm { MinNoticeDays = 1, MaxJobsPerDay = 4 };
        foreach (var dow in AvailabilityDays.DisplayOrder)
            form.Days.Add(new DayScheduleForm { DayOfWeek = dow, IsAvailable = dow is >= 1 and <= 5, Start = T(8), End = T(17) });
        return form;
    }

    private static DayScheduleForm Day(AvailabilityForm f, int dow) => f.Days.Single(d => d.DayOfWeek == dow);

    // ----------------------------------------------------------------- access

    [Fact]
    public async Task OnlyAnActiveApprovedProvider_CanUseThePage()
    {
        var (svc, db) = Create();
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator);
        db.Customers.Add(customer);
        db.Users.Add(admin);
        var pending = await AddProviderAsync(db, ApprovalStatuses.Pending);
        var rejected = await AddProviderAsync(db, ApprovalStatuses.Rejected);
        var suspended = await AddProviderAsync(db, account: AccountStatuses.Suspended);
        var deactivated = await AddProviderAsync(db, account: AccountStatuses.Deactivated);
        await db.SaveChangesAsync();

        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetAsync(customer.UserId)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetAsync(admin.UserId)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetAsync(99999)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetAsync(suspended.UserId)).Access);
        Assert.Equal(ProviderDashboardAccess.NotAllowed, (await svc.GetAsync(deactivated.UserId)).Access);
        Assert.Equal(ProviderDashboardAccess.Pending, (await svc.GetAsync(pending.UserId)).Access);
        Assert.Equal(ProviderDashboardAccess.Rejected, (await svc.GetAsync(rejected.UserId)).Access);

        // saving is refused for the same people, and nothing is written
        foreach (var userId in new[] { customer.UserId, admin.UserId, suspended.UserId, deactivated.UserId, pending.UserId, rejected.UserId, 99999 })
        {
            var result = await svc.SaveAsync(userId, ValidForm());
            Assert.False(result.Saved);
            Assert.NotEqual(ProviderDashboardAccess.Allowed, result.Access);
        }
        Assert.Empty(db.ProviderAvailabilities);
    }

    // ------------------------------------------------------------------- read

    [Fact]
    public async Task NewProvider_SeesASuggestedMondayToFridayWeek()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);

        var page = (await svc.GetAsync(p.UserId)).Page!;

        Assert.True(page.IsSuggested);
        Assert.Equal(Today, page.Today);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 0 }, page.Form.Days.Select(d => d.DayOfWeek));
        Assert.Equal(new[] { true, true, true, true, true, false, false }, page.Form.Days.Select(d => d.IsAvailable));
        Assert.All(page.Form.Days, d => { Assert.Equal(T(8), d.Start); Assert.Equal(T(17), d.End); });
        Assert.Equal(p.MinNoticeDays, page.Form.MinNoticeDays);
        Assert.Equal(p.MaxJobsPerDay, page.Form.MaxJobsPerDay);
        Assert.Empty(db.ProviderAvailabilities);          // suggesting stores nothing
    }

    [Fact]
    public async Task Page_ShowsTheProvidersNameAndBusinessInTheSidebar()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);

        var shell = (await svc.GetAsync(p.UserId)).Page!.Shell;

        Assert.Equal("Provider", shell.Role);
        Assert.Equal("Marco Reyes", shell.FullName);
        Assert.Equal("MR", shell.Initials);
        Assert.Equal("Marco's Plumbing Works", shell.Subtitle);
    }

    [Fact]
    public async Task SavedSchedule_IsShownBack_WithBreaksInOrder_AndOnlyUpcomingDates()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = ValidForm();
        Day(form, 1).Breaks.Add(new BreakForm { Start = T(15), End = T(15, 30) });
        Day(form, 1).Breaks.Add(new BreakForm { Start = T(12), End = T(13) });
        form.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 25), Reason = "Personal Leave" });
        form.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 20), Reason = null });
        Assert.True((await svc.SaveAsync(p.UserId, form)).Saved);
        // an old date that is already in the past
        db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = p.ProviderId, UnavailableDate = new DateOnly(2026, 10, 1), Reason = "Old" });
        await db.SaveChangesAsync();

        var page = (await svc.GetAsync(p.UserId)).Page!;

        Assert.False(page.IsSuggested);
        var monday = Day(page.Form, 1);
        Assert.Equal(new[] { T(12), T(15) }, monday.Breaks.Select(b => b.Start!.Value));
        Assert.Equal(new[] { new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25) }, page.Form.Dates.Select(d => d.Date!.Value));
        Assert.Equal("Personal Leave", page.Form.Dates[1].Reason);
        Assert.False(Day(page.Form, 6).IsAvailable);
        Assert.Equal(4, page.Form.MaxJobsPerDay);
    }

    [Fact]
    public async Task NoticeChoices_AlwaysIncludeTheCurrentValue()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        p.MinNoticeDays = 4;                                   // not one of the usual choices
        await db.SaveChangesAsync();

        var labels = (await svc.GetAsync(p.UserId)).Page!.NoticeOptions;

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 7, 14 }, labels.Select(o => o.Value));
        Assert.Equal("Same day", labels.First().Label);
        Assert.Equal("1 day", labels.Single(o => o.Value == 1).Label);
        Assert.Equal("4 days", labels.Single(o => o.Value == 4).Label);
    }

    [Fact]
    public async Task ARejectedForm_IsShownBackWithWhatWasTyped()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var posted = ValidForm();
        Day(posted, 1).End = T(7);                              // wrong on purpose
        posted.Days.Reverse();                                  // browser sent the days in another order

        var page = (await svc.GetAsync(p.UserId, posted)).Page!;

        Assert.False(page.IsSuggested);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 0 }, page.Form.Days.Select(d => d.DayOfWeek));
        Assert.Equal(T(7), Day(page.Form, 1).End);
    }

    [Fact]
    public async Task AnIncompletePost_FallsBackToTheSavedSchedule()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var posted = new AvailabilityForm();                   // no days at all

        var page = (await svc.GetAsync(p.UserId, posted)).Page!;

        Assert.Equal(7, page.Form.Days.Count);
    }

    // ------------------------------------------------------------------- save

    [Fact]
    public async Task Save_WritesOneRowPerDay_WithBreaksDatesAndSettings()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = ValidForm();
        form.MinNoticeDays = 2;
        form.MaxJobsPerDay = 6;
        Day(form, 1).Breaks.Add(new BreakForm { Start = T(12), End = T(13) });
        form.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 25), Reason = "  Personal Leave  " });

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        Assert.Empty(result.Errors);

        var rows = await db.ProviderAvailabilities.Include(a => a.Breaks).OrderBy(a => a.DayOfWeek).ToListAsync();
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6 }, rows.Select(r => r.DayOfWeek));
        Assert.Equal(new[] { false, true, true, true, true, true, false }, rows.Select(r => r.IsAvailable));
        Assert.All(rows, r => { Assert.Equal(T(8), r.StartTime); Assert.Equal(T(17), r.EndTime); Assert.Equal(p.ProviderId, r.ProviderId); });

        var monday = rows.Single(r => r.DayOfWeek == 1);
        var brk = Assert.Single(monday.Breaks);
        Assert.Equal(T(12), brk.BreakStart);
        Assert.Equal(T(13), brk.BreakEnd);

        var date = await db.ProviderUnavailableDates.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 25), date.UnavailableDate);
        Assert.Equal("Personal Leave", date.Reason);

        var saved = await db.Providers.SingleAsync();
        Assert.Equal(2, saved.MinNoticeDays);
        Assert.Equal(6, saved.MaxJobsPerDay);
    }

    [Fact]
    public async Task SavingAgain_UpdatesInPlace_AndReplacesTheBreaks()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var first = ValidForm();
        Day(first, 1).Breaks.Add(new BreakForm { Start = T(12), End = T(13) });
        Day(first, 2).Breaks.Add(new BreakForm { Start = T(12), End = T(13) });
        Assert.True((await svc.SaveAsync(p.UserId, first)).Saved);
        var mondayId = (await db.ProviderAvailabilities.SingleAsync(a => a.DayOfWeek == 1)).AvailabilityId;

        var second = ValidForm();
        Day(second, 1).Start = T(9);
        Day(second, 1).Breaks.Add(new BreakForm { Start = T(11), End = T(11, 45) });
        Day(second, 6).IsAvailable = true;                      // Saturday opens
        Assert.True((await svc.SaveAsync(p.UserId, second)).Saved);

        Assert.Equal(7, await db.ProviderAvailabilities.CountAsync());           // still one row per day
        var monday = await db.ProviderAvailabilities.Include(a => a.Breaks).SingleAsync(a => a.DayOfWeek == 1);
        Assert.Equal(mondayId, monday.AvailabilityId);
        Assert.Equal(T(9), monday.StartTime);
        Assert.Equal(T(11), Assert.Single(monday.Breaks).BreakStart);
        Assert.Empty((await db.ProviderAvailabilities.Include(a => a.Breaks).SingleAsync(a => a.DayOfWeek == 2)).Breaks);
        Assert.Equal(1, await db.ProviderBreaks.CountAsync());                    // old breaks are gone, not piled up
        Assert.True((await db.ProviderAvailabilities.SingleAsync(a => a.DayOfWeek == 6)).IsAvailable);
    }

    [Fact]
    public async Task AClosedDay_KeepsValidHours_UsesDefaultsOtherwise_AndDropsItsBreaks()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = ValidForm();
        Day(form, 3).IsAvailable = false;                       // Wednesday closed, keeps its own 9-15
        Day(form, 3).Start = T(9);
        Day(form, 3).End = T(15);
        Day(form, 3).Breaks.Add(new BreakForm { Start = T(12), End = T(13) });
        Day(form, 6).Start = null;                              // Saturday closed, no hours typed
        Day(form, 6).End = null;
        Day(form, 0).Start = T(18);                             // Sunday closed, nonsense hours (end before start)
        Day(form, 0).End = T(6);

        Assert.True((await svc.SaveAsync(p.UserId, form)).Saved);

        var wed = await db.ProviderAvailabilities.Include(a => a.Breaks).SingleAsync(a => a.DayOfWeek == 3);
        Assert.False(wed.IsAvailable);
        Assert.Equal((T(9), T(15)), (wed.StartTime, wed.EndTime));
        Assert.Empty(wed.Breaks);
        var sat = await db.ProviderAvailabilities.SingleAsync(a => a.DayOfWeek == 6);
        Assert.Equal((T(8), T(17)), (sat.StartTime, sat.EndTime));
        var sun = await db.ProviderAvailabilities.SingleAsync(a => a.DayOfWeek == 0);
        Assert.Equal((T(8), T(17)), (sun.StartTime, sun.EndTime));
    }

    [Fact]
    public async Task EmptyBreakAndDateRows_AreIgnored_AndBackToBackBreaksAreAllowed()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = ValidForm();
        var monday = Day(form, 1);
        monday.Breaks.Add(new BreakForm());                                              // nothing typed
        monday.Breaks.Add(new BreakForm { Start = T(12), End = T(12, 30) });
        monday.Breaks.Add(new BreakForm { Start = T(12, 30), End = T(13) });             // starts exactly when the other ends
        form.Dates.Add(new UnavailableDateForm());                                       // nothing typed

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.True(result.Saved);
        Assert.Equal(2, await db.ProviderBreaks.CountAsync());
        Assert.Empty(db.ProviderUnavailableDates);
    }

    // -------------------------------------------------------------- validation

    private static void Mutate(AvailabilityForm f, string key)
    {
        switch (key)
        {
            case "endBeforeStart": Day(f, 1).End = T(7); break;
            case "endEqualsStart": Day(f, 1).End = T(8); break;
            case "noStart": Day(f, 1).Start = null; break;
            case "noEnd": Day(f, 1).End = null; break;
            case "breakOutside": Day(f, 1).Breaks.Add(new BreakForm { Start = T(16, 30), End = T(17, 30) }); break;
            case "breakBeforeOpening": Day(f, 1).Breaks.Add(new BreakForm { Start = T(7), End = T(9) }); break;
            case "breakBackwards": Day(f, 1).Breaks.Add(new BreakForm { Start = T(13), End = T(12) }); break;
            case "breakOneSided": Day(f, 1).Breaks.Add(new BreakForm { Start = T(12), End = null }); break;
            case "breaksOverlap":
                Day(f, 1).Breaks.Add(new BreakForm { Start = T(12), End = T(13) });
                Day(f, 1).Breaks.Add(new BreakForm { Start = T(12, 30), End = T(14) });
                break;
            case "sixBreaks":
                for (var i = 0; i < 6; i++) Day(f, 1).Breaks.Add(new BreakForm { Start = T(9 + i), End = T(9 + i, 30) });
                break;
            case "missingDay": f.Days.RemoveAll(d => d.DayOfWeek == 0); break;
            case "duplicateDay": f.Days[6].DayOfWeek = 1; break;
            case "dayOutOfRange": f.Days[6].DayOfWeek = 7; break;
            case "pastDate": f.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 13) }); break;
            case "duplicateDate":
                f.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 25) });
                f.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 25), Reason = "again" });
                break;
            case "reasonWithoutDate": f.Dates.Add(new UnavailableDateForm { Reason = "Leave" }); break;
            case "longReason": f.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 25), Reason = new string('x', 256) }); break;
            case "noticeNegative": f.MinNoticeDays = -1; break;
            case "noticeTooBig": f.MinNoticeDays = 31; break;
            case "jobsZero": f.MaxJobsPerDay = 0; break;
            case "jobsTooBig": f.MaxJobsPerDay = 51; break;
            default: throw new ArgumentException(key);
        }
    }

    [Theory]
    [InlineData("endBeforeStart", "Monday: the end time must be after the start time.")]
    [InlineData("endEqualsStart", "Monday: the end time must be after the start time.")]
    [InlineData("noStart", "Monday: please enter a start time and an end time.")]
    [InlineData("noEnd", "Monday: please enter a start time and an end time.")]
    [InlineData("breakOutside", "Monday: breaks must be inside your working hours.")]
    [InlineData("breakBeforeOpening", "Monday: breaks must be inside your working hours.")]
    [InlineData("breakBackwards", "Monday: a break must end after it starts.")]
    [InlineData("breakOneSided", "Monday: every break needs a start time and an end time.")]
    [InlineData("breaksOverlap", "Monday: breaks cannot overlap.")]
    [InlineData("sixBreaks", "Monday: you can add at most 5 breaks.")]
    [InlineData("missingDay", "The weekly schedule was incomplete.")]
    [InlineData("duplicateDay", "The weekly schedule was incomplete.")]
    [InlineData("dayOutOfRange", "The weekly schedule was incomplete.")]
    [InlineData("pastDate", "Oct 13, 2026 is in the past.")]
    [InlineData("duplicateDate", "Oct 25, 2026 is listed more than once.")]
    [InlineData("reasonWithoutDate", "Every unavailable date needs a date.")]
    [InlineData("longReason", "the reason can be at most 255 characters.")]
    [InlineData("noticeNegative", "Minimum booking notice must be between 0 and 30 days.")]
    [InlineData("noticeTooBig", "Minimum booking notice must be between 0 and 30 days.")]
    [InlineData("jobsZero", "Maximum jobs per day must be between 1 and 50.")]
    [InlineData("jobsTooBig", "Maximum jobs per day must be between 1 and 50.")]
    public async Task BadInput_IsRejectedWithAClearMessage_AndNothingIsSaved(string key, string expected)
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = ValidForm();
        Mutate(form, key);

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.False(result.Saved);
        Assert.Contains(result.Errors, e => e.Contains(expected));
        Assert.Empty(db.ProviderAvailabilities);
        Assert.Empty(db.ProviderBreaks);
        Assert.Empty(db.ProviderUnavailableDates);
        var provider = await db.Providers.SingleAsync();
        Assert.Equal(1, provider.MinNoticeDays);               // the provider's own defaults are untouched
        Assert.Equal(5, provider.MaxJobsPerDay);
    }

    [Fact]
    public async Task AllProblemsAreReportedTogether()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var form = ValidForm();
        Day(form, 1).End = T(7);
        Day(form, 2).Start = null;
        form.MaxJobsPerDay = 0;

        var result = await svc.SaveAsync(p.UserId, form);

        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public async Task ARejectedSave_LeavesAnEarlierSavedScheduleUntouched()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var good = ValidForm();
        good.MaxJobsPerDay = 3;
        Assert.True((await svc.SaveAsync(p.UserId, good)).Saved);

        var bad = ValidForm();
        Day(bad, 1).Start = T(9);                               // would change Monday ...
        bad.MaxJobsPerDay = 0;                                   // ... but this makes the whole save fail
        Assert.False((await svc.SaveAsync(p.UserId, bad)).Saved);

        Assert.Equal(T(8), (await db.ProviderAvailabilities.SingleAsync(a => a.DayOfWeek == 1)).StartTime);
        Assert.Equal(3, (await db.Providers.SingleAsync()).MaxJobsPerDay);
    }

    // ----------------------------------------------------------------- dates

    [Fact]
    public async Task TodayIsAllowed_ButYesterdayIsNot_ByCebuTime()
    {
        // 20:00 UTC on Oct 13 is already 04:00 on Oct 14 in Cebu
        var (svc, db) = Create(new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero));
        var p = await AddProviderAsync(db);

        var today = ValidForm();
        today.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 14), Reason = "Today" });
        Assert.True((await svc.SaveAsync(p.UserId, today)).Saved);

        var yesterday = ValidForm();
        yesterday.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 13) });
        var result = await svc.SaveAsync(p.UserId, yesterday);
        Assert.False(result.Saved);
        Assert.Contains(result.Errors, e => e.Contains("is in the past"));
    }

    [Fact]
    public async Task Dates_AreAddedUpdatedAndRemoved_AndOldOnesAreLeftAlone()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        db.ProviderUnavailableDates.AddRange(
            new ProviderUnavailableDate { ProviderId = p.ProviderId, UnavailableDate = new DateOnly(2026, 10, 1), Reason = "Old" },
            new ProviderUnavailableDate { ProviderId = p.ProviderId, UnavailableDate = new DateOnly(2026, 10, 20), Reason = "Leave" },
            new ProviderUnavailableDate { ProviderId = p.ProviderId, UnavailableDate = new DateOnly(2026, 10, 25), Reason = "Trip" });
        await db.SaveChangesAsync();

        var form = ValidForm();
        form.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 20), Reason = "Family event" });   // reason changed
        form.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 11, 2), Reason = "Holiday" });        // new
        // 25 Oct is not listed any more: removed

        Assert.True((await svc.SaveAsync(p.UserId, form)).Saved);

        var all = await db.ProviderUnavailableDates.OrderBy(d => d.UnavailableDate).ToListAsync();
        Assert.Equal(new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20), new DateOnly(2026, 11, 2) }, all.Select(d => d.UnavailableDate));
        Assert.Equal(new[] { "Old", "Family event", "Holiday" }, all.Select(d => d.Reason));
    }

    [Fact]
    public async Task RemovingEveryDate_ClearsTheList()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = p.ProviderId, UnavailableDate = new DateOnly(2026, 10, 25), Reason = "Trip" });
        await db.SaveChangesAsync();

        Assert.True((await svc.SaveAsync(p.UserId, ValidForm())).Saved);

        Assert.Empty(db.ProviderUnavailableDates);
    }

    // -------------------------------------------------------------- ownership

    [Fact]
    public async Task SavingNeverTouchesAnotherProvidersSchedule()
    {
        var (svc, db) = Create();
        var marco = await AddProviderAsync(db);
        var liza = await AddProviderAsync(db, first: "Liza", last: "Santos", business: "Liza Clean Services");

        var lizaForm = ValidForm();
        lizaForm.MaxJobsPerDay = 9;
        Day(lizaForm, 1).Breaks.Add(new BreakForm { Start = T(12), End = T(13) });
        lizaForm.Dates.Add(new UnavailableDateForm { Date = new DateOnly(2026, 10, 30), Reason = "Leave" });
        Assert.True((await svc.SaveAsync(liza.UserId, lizaForm)).Saved);

        var marcoForm = ValidForm();
        marcoForm.MaxJobsPerDay = 2;
        Day(marcoForm, 1).Start = T(10);
        Assert.True((await svc.SaveAsync(marco.UserId, marcoForm)).Saved);

        var lizaMonday = await db.ProviderAvailabilities.Include(a => a.Breaks).SingleAsync(a => a.ProviderId == liza.ProviderId && a.DayOfWeek == 1);
        Assert.Equal(T(8), lizaMonday.StartTime);
        Assert.Single(lizaMonday.Breaks);
        Assert.Equal(9, (await db.Providers.SingleAsync(p => p.ProviderId == liza.ProviderId)).MaxJobsPerDay);
        Assert.Equal(1, await db.ProviderUnavailableDates.CountAsync(d => d.ProviderId == liza.ProviderId));
        Assert.Equal(0, await db.ProviderUnavailableDates.CountAsync(d => d.ProviderId == marco.ProviderId));
        Assert.Equal(7, await db.ProviderAvailabilities.CountAsync(a => a.ProviderId == marco.ProviderId));
    }
}
