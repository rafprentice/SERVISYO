using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Controllers;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;

namespace Servisyo.Tests;

/// Exact-minute start times: the provider's exact shift start, the "soonest" start today (the next whole minute, when
/// the provider needs no notice), "Enter exact time" on the booking form, and the live countdown on the provider's card.
public class ExactStartTimeTests
{
    private static TimeOnly T(int h, int m = 0, int s = 0) => new(h, m, s);
    private static readonly DateOnly Today = new(2026, 11, 14);              // a Saturday
    private static readonly IReadOnlyList<BookingSlotRules.Busy> NoBusy = Array.Empty<BookingSlotRules.Busy>();

    private static IReadOnlyList<BookingSlotRules.Window> Shift(TimeOnly start, TimeOnly end, bool lunch = false) => new[]
    {
        new BookingSlotRules.Window(start, end, lunch ? new[] { (T(12), T(13)) } : Array.Empty<(TimeOnly, TimeOnly)>())
    };

    // ================================================================== the rules

    [Fact]
    public void NextMinute_IsTheNextWholeMinute()
    {
        Assert.Equal(T(4, 5), BookingSlotRules.NextMinute(T(4, 4, 30)));
        Assert.Equal(T(4, 5), BookingSlotRules.NextMinute(T(4, 4)));
        Assert.Equal(T(0, 0).AddMinutes(1), BookingSlotRules.NextMinute(T(0, 0, 59)));
        Assert.Null(BookingSlotRules.NextMinute(T(23, 59, 10)));                 // no minute left today
    }

    [Fact]
    public void Today_WithNoNotice_TheSoonestStartComesFirst_ThenTheHalfHours()
    {
        // it is 4:04:30 AM; the provider works from 4:04 AM (that minute has just passed)
        var slots = BookingSlotRules.Slots(Today, 30, Today, T(4, 4, 30), 0, false, Shift(T(4, 4), T(17)), NoBusy, 5);
        Assert.Equal(new[] { T(4, 5), T(4, 30), T(5), T(5, 30) }, slots.Take(4).ToArray());
        Assert.Equal(T(4, 5), BookingSlotRules.SoonestSlot(Today, Today, T(4, 4, 30), 0));
    }

    [Fact]
    public void TheSoonestStart_IsOnlyForToday_WithNoNoticeDays()
    {
        Assert.Null(BookingSlotRules.SoonestSlot(Today.AddDays(1), Today, T(4, 4, 30), 0));
        Assert.Null(BookingSlotRules.SoonestSlot(Today, Today, T(4, 4, 30), 1));
        // another day: the provider's exact start (4:04) first, not rounded
        var tomorrow = BookingSlotRules.Slots(Today.AddDays(1), 30, Today, T(4, 4, 30), 0, false, Shift(T(4, 4), T(6)), NoBusy, 5);
        Assert.Equal(new[] { T(4, 4), T(4, 30), T(5), T(5, 30) }, tomorrow.ToArray());
    }

    [Fact]
    public void TheSoonestStart_FollowsEveryOtherRule()
    {
        // 11:59:30 with a 12:00-13:00 break: 12:00 runs into the break, so the soonest is not offered
        var lunch = BookingSlotRules.Slots(Today, 60, Today, T(11, 59, 30), 0, false, Shift(T(8), T(17), lunch: true), NoBusy, 5);
        Assert.Equal(T(13), lunch.First());
        // another booking from 10:00 to 11:00: 10:00 is taken
        var busy = new[] { new BookingSlotRules.Busy(T(10), 60) };
        var taken = BookingSlotRules.Slots(Today, 30, Today, T(9, 59, 10), 0, false, Shift(T(8), T(17)), busy, 5);
        Assert.Equal(T(11), taken.First());
        // 4:31 PM: a 60-minute job would end after 5 PM
        Assert.Empty(BookingSlotRules.Slots(Today, 60, Today, T(16, 30, 5), 0, false, Shift(T(8), T(17)), NoBusy, 5));
    }

    [Theory]
    [InlineData(4, 4, 59, 4, 5, SlotCheck.Ok)]               // the next minute: allowed (it starts in a second)
    [InlineData(4, 5, 0, 4, 5, SlotCheck.TimePassed)]        // that minute has come: too late
    [InlineData(4, 4, 30, 4, 4, SlotCheck.TimePassed)]
    [InlineData(4, 4, 30, 9, 17, SlotCheck.Ok)]              // any whole minute later the same day
    public void CheckTime_TodayNeedsAtLeastTheNextWholeMinute(int nh, int nm, int ns, int h, int m, SlotCheck expected) =>
        Assert.Equal(expected, BookingSlotRules.CheckTime(T(h, m), 30, Today, Today, T(nh, nm, ns), Shift(T(4), T(17)), NoBusy));

    [Fact]
    public void CheckTime_RefusesSeconds()
    {
        Assert.Equal(SlotCheck.NotWholeMinute, BookingSlotRules.CheckTime(T(9, 10, 30), 30, Today.AddDays(1), Today, T(4), Shift(T(8), T(17)), NoBusy));
        Assert.False(BookingSlotRules.IsDateProblem(SlotCheck.NotWholeMinute));
        Assert.Contains("whole minutes", BookingSlotRules.Explain(SlotCheck.NotWholeMinute, 0));
    }

    // ================================================================== the booking form (BookProviderService)

    /// Sat 14 Nov 2026, 4:04:30 AM in Manila.
    private sealed class EarlyMorning : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 14, 4, 4, 30, TimeSpan.FromHours(8)).ToUniversalTime();
    }

    private static async Task<(BookProviderService svc, ServisyoDbContext db, Customer c, Provider p, Service s)> WorldAsync(int minNotice = 0)
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        User U(string first, string role) => new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = AccountStatuses.Active };
        var c = new Customer { User = U("Ana", Roles.Customer), Address = "45 Mabini St, Quezon City" };
        var p = new Provider { BusinessName = "Early Bird", ApprovalStatus = ApprovalStatuses.Approved, MinNoticeDays = minNotice, MaxJobsPerDay = 5, User = U("Mary", Roles.ServiceProvider) };
        p.Availabilities.Add(new ProviderAvailability
        {
            DayOfWeek = (byte)Today.DayOfWeek, StartTime = T(4, 4), EndTime = T(17), IsAvailable = true,
            Breaks = { new ProviderBreak { BreakStart = T(12), BreakEnd = T(13) } }
        });
        db.AddRange(c, p);
        await db.SaveChangesAsync();
        var s = new Service { ProviderId = p.ProviderId, Category = new ServiceCategory { CategoryName = "Cleaning", IsActive = true }, ServiceName = "Aircon Cleaning", Price = 650m, DurationMinutes = 30, IsActive = true };
        db.Services.Add(s);
        await db.SaveChangesAsync();
        return (new BookProviderService(db, new EarlyMorning()), db, c, p, s);
    }

    private static BookProviderForm Form(Service s, TimeOnly time) => new()
    {
        ServiceIds = new List<int> { s.ServiceId }, Date = Today, Time = time,
        Location = "45 Mabini St, Quezon City", Description = "Live demo booking."
    };

    [Fact]
    public async Task TheSlots_ForToday_StartWithTheSoonestMinute_AndSayTheHours()
    {
        var (svc, _, c, p, s) = await WorldAsync();

        var result = await svc.GetSlotsAsync(c.UserId, p.ProviderId, Today, new[] { s.ServiceId });

        Assert.Equal(new SlotViewModel("04:05", "4:05 AM", Soon: true), result.Slots[0]);
        Assert.Equal(new SlotViewModel("04:30", "4:30 AM"), result.Slots[1]);
        Assert.Single(result.Slots, x => x.Soon);
        Assert.Equal("4:04 AM – 5:00 PM, break 12:00 PM – 1:00 PM", result.Hours);
        Assert.Equal("04:05", result.EarliestTime);
        Assert.Null((await svc.GetSlotsAsync(c.UserId, p.ProviderId, Today.AddDays(7), new[] { s.ServiceId })).EarliestTime);
    }

    [Fact]
    public async Task ABookingOneMinuteAhead_IsSaved()
    {
        var (svc, db, c, p, s) = await WorldAsync();

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, T(4, 5)));

        Assert.True(result.Succeeded);
        var b = db.Bookings.Single();
        Assert.Equal((Today, T(4, 5), BookingStatuses.Pending), (b.BookingDate, b.BookingTime, b.Status));
    }

    [Fact]
    public async Task TheCurrentMinute_IsTooLate()
    {
        var (svc, db, c, p, s) = await WorldAsync();

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, T(4, 4)));

        Assert.Equal(BookingSlotRules.Explain(SlotCheck.TimePassed, 0), result.FieldErrors["Time"]);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task WithNoticeDays_TodayCannotBeBooked_EvenOneMinuteAhead()
    {
        var (svc, db, c, p, s) = await WorldAsync(minNotice: 1);

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, T(4, 5)));

        Assert.Equal(BookingSlotRules.Explain(SlotCheck.NoticeTooShort, 1), result.FieldErrors["Date"]);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task EnterExactTime_IsCheckedAgainstEveryRule()
    {
        var (svc, db, c, p, s) = await WorldAsync();
        db.Bookings.Add(new Booking
        {
            CustomerId = c.CustomerId, ProviderId = p.ProviderId, BookingDate = Today, BookingTime = T(9), TotalDurationMinutes = 60,
            TotalFee = 650m, Status = BookingStatuses.Accepted, RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();
        async Task<BookTimeCheckResult> Check(TimeOnly t) => await svc.CheckTimeAsync(c.UserId, p.ProviderId, Today, t, new[] { s.ServiceId });

        Assert.True((await Check(T(7, 17))).Ok);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.TimePassed, 0), (await Check(T(4, 4))).Message);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.OutsideHours, 0), (await Check(T(16, 31))).Message);   // would end after 5 PM
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.InBreak, 0), (await Check(T(11, 45))).Message);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.Clash, 0), (await Check(T(8, 45))).Message);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.NotWholeMinute, 0), (await Check(T(7, 17, 5))).Message);

        Assert.False((await svc.CheckTimeAsync(p.UserId, p.ProviderId, Today, T(7, 17), new[] { s.ServiceId })).Allowed);   // not a customer
        Assert.True((await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, T(7, 17)))).Succeeded);                           // and it saves
    }

    [Fact]
    public void TheCheckTimeEndpoint_IsAGetForCustomers()
    {
        var method = typeof(BookProviderController).GetMethod(nameof(BookProviderController.CheckTime))!;
        var get = Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>());
        Assert.Equal("check-time", get.Template);
    }

    // ================================================================== the provider's countdown

    [Theory]
    [InlineData(54, "Starts in 00:54")]
    [InlineData(600, "Starts in 10:00")]
    [InlineData(3725, "Starts in 1:02:05")]
    [InlineData(90061, "Starts in 1d 01:01:01")]
    [InlineData(0, "Starting now")]
    public void TheCountdown_ReadsLikeAClock(int seconds, string text) =>
        Assert.Equal(text, StartWindowViewModel.Countdown(seconds));

    [Fact]
    public void TheCountdown_RunsOnTheServersClock()
    {
        var w = StartWindowViewModel.For(Today, T(4, 5), new DateTime(2026, 11, 14, 4, 4, 6, 250));

        Assert.Equal(StartWindowState.TooEarly, w.State);
        Assert.Equal("2026-11-14T04:05:00+08:00", w.ScheduledIso);
        Assert.Equal("2026-11-14T04:04:06.250+08:00", w.ServerNowIso);
        Assert.Equal(54, w.SecondsUntilStart);                     // 53.75 seconds, rounded up
        Assert.Equal("Starts in 00:54", w.CountdownText);
        Assert.Equal(0, StartWindowViewModel.For(Today, T(4, 5), new DateTime(2026, 11, 14, 4, 6, 0)).SecondsUntilStart);
    }

    // ================================================================== the pages

    private static string Repo(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void TheBookingForm_HasEnterExactTime()
    {
        var view = Repo("src", "Servisyo.Web", "Views", "BookProvider", "Index.cshtml");
        Assert.Contains(">Enter exact time</button>", view);
        Assert.Contains("type=\"time\" step=\"60\" disabled data-bp-exact-input", view);       // only sent once it is opened
        Assert.Contains("data-check-url=", view);
        Assert.Contains("data-today=", view);
        var js = Repo("src", "Servisyo.Web", "wwwroot", "js", "book-provider.js");
        Assert.Contains("Soonest", js);
        Assert.Contains("checkUrl", js);
        Assert.Contains("60000", js);                                                          // today's list is asked for again every minute
    }

    [Fact]
    public void TheProviderCard_SwapsTheCountdownForTheButton()
    {
        var view = Repo("src", "Servisyo.Web", "Views", "ProviderBookings", "Index.cshtml");
        Assert.Contains("data-countdown data-start-at=\"@w.ScheduledIso\" data-server-now=\"@w.ServerNowIso\"", view);
        Assert.Contains("data-start-unlock hidden", view);
        Assert.Contains("<partial name=\"_StartNowForm\"", view);
        var js = Repo("src", "Servisyo.Web", "wwwroot", "js", "booking-start.js");
        Assert.Contains("function countdownText(seconds)", js);
        Assert.Contains("\"Starting now\"", js);
        Assert.Contains("[data-start-unlock]", js);
    }
}
