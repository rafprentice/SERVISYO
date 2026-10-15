using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class BookingSlotRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);          // Wednesday
    private static readonly DateOnly Tuesday = new(2026, 10, 20);
    private static readonly TimeOnly Now = new(10, 0);

    private static TimeOnly T(int h, int m = 0) => new(h, m);

    private static BookingSlotRules.Window Day(int fromH = 8, int toH = 17, bool lunch = true)
        => new(T(fromH), T(toH), lunch ? new[] { (T(12), T(13)) } : Array.Empty<(TimeOnly, TimeOnly)>());

    private static IReadOnlyList<BookingSlotRules.Window> One(BookingSlotRules.Window w) => new[] { w };
    private static readonly IReadOnlyList<BookingSlotRules.Busy> NoBusy = Array.Empty<BookingSlotRules.Busy>();

    [Fact]
    public void CheckDay_AppliesMinimumNotice()
    {
        // minimum notice 1 day: today is too soon, tomorrow is fine
        Assert.Equal(SlotCheck.NoticeTooShort, BookingSlotRules.CheckDay(Today, Today, 1, false, One(Day()), 0, 5));
        Assert.Equal(SlotCheck.Ok, BookingSlotRules.CheckDay(Today.AddDays(1), Today, 1, false, One(Day()), 0, 5));
        // yesterday is never allowed, even with no notice needed
        Assert.Equal(SlotCheck.NoticeTooShort, BookingSlotRules.CheckDay(Today.AddDays(-1), Today, 0, false, One(Day()), 0, 5));
        Assert.Equal(SlotCheck.Ok, BookingSlotRules.CheckDay(Today, Today, 0, false, One(Day()), 0, 5));
    }

    [Fact]
    public void CheckDay_LimitsHowFarAheadYouCanBook()
    {
        Assert.Equal(SlotCheck.Ok, BookingSlotRules.CheckDay(Today.AddDays(90), Today, 1, false, One(Day()), 0, 5));
        Assert.Equal(SlotCheck.TooFar, BookingSlotRules.CheckDay(Today.AddDays(91), Today, 1, false, One(Day()), 0, 5));
    }

    [Fact]
    public void CheckDay_UnavailableDateNoScheduleAndFullDay()
    {
        Assert.Equal(SlotCheck.DayOff, BookingSlotRules.CheckDay(Tuesday, Today, 1, true, One(Day()), 0, 5));
        Assert.Equal(SlotCheck.NotWorking, BookingSlotRules.CheckDay(Tuesday, Today, 1, false, Array.Empty<BookingSlotRules.Window>(), 0, 5));
        Assert.Equal(SlotCheck.Ok, BookingSlotRules.CheckDay(Tuesday, Today, 1, false, One(Day()), 4, 5));
        Assert.Equal(SlotCheck.DayFull, BookingSlotRules.CheckDay(Tuesday, Today, 1, false, One(Day()), 5, 5));     // the limit counts
    }

    [Theory]
    [InlineData(7, 0, SlotCheck.OutsideHours)]     // starts before the working hours
    [InlineData(8, 0, SlotCheck.Ok)]
    [InlineData(16, 0, SlotCheck.Ok)]              // ends exactly at closing time
    [InlineData(16, 30, SlotCheck.OutsideHours)]   // would end after closing time
    public void CheckTime_TheWholeJobMustFitTheWorkingHours(int h, int m, SlotCheck expected)
        => Assert.Equal(expected, BookingSlotRules.CheckTime(T(h, m), 60, Tuesday, Today, Now, One(Day(lunch: false)), NoBusy));

    [Theory]
    [InlineData(11, 0, SlotCheck.Ok)]              // ends exactly when the break starts
    [InlineData(11, 30, SlotCheck.InBreak)]
    [InlineData(12, 0, SlotCheck.InBreak)]
    [InlineData(12, 30, SlotCheck.InBreak)]
    [InlineData(13, 0, SlotCheck.Ok)]              // starts exactly when the break ends
    public void CheckTime_AJobCannotTouchABreak(int h, int m, SlotCheck expected)
        => Assert.Equal(expected, BookingSlotRules.CheckTime(T(h, m), 60, Tuesday, Today, Now, One(Day()), NoBusy));

    [Theory]
    [InlineData(8, 0, SlotCheck.Ok)]
    [InlineData(8, 30, SlotCheck.Clash)]           // overlaps the first half hour of the 9:00 booking
    [InlineData(9, 0, SlotCheck.Clash)]
    [InlineData(9, 30, SlotCheck.Clash)]
    [InlineData(10, 0, SlotCheck.Ok)]              // starts exactly when the other ends
    public void CheckTime_AJobCannotOverlapAnotherBooking(int h, int m, SlotCheck expected)
    {
        var busy = new[] { new BookingSlotRules.Busy(T(9), 60) };
        Assert.Equal(expected, BookingSlotRules.CheckTime(T(h, m), 60, Tuesday, Today, Now, One(Day()), busy));
    }

    [Fact]
    public void CheckTime_TodayOnlyAllowsTimesInTheFuture()
    {
        Assert.Equal(SlotCheck.TimePassed, BookingSlotRules.CheckTime(T(9), 60, Today, Today, Now, One(Day()), NoBusy));
        Assert.Equal(SlotCheck.TimePassed, BookingSlotRules.CheckTime(T(10), 60, Today, Today, Now, One(Day()), NoBusy));     // right now is too late
        Assert.Equal(SlotCheck.Ok, BookingSlotRules.CheckTime(T(10, 30), 60, Today, Today, Now, One(Day(lunch: false)), NoBusy));
    }

    [Fact]
    public void Slots_EveryHalfHour_SkippingTheBreak()
    {
        var slots = BookingSlotRules.Slots(Tuesday, 60, Today, Now, 1, false, One(Day()), NoBusy, 5);

        var expected = new[]
        {
            T(8), T(8, 30), T(9), T(9, 30), T(10), T(10, 30), T(11),       // the last start before the 12:00 break
            T(13), T(13, 30), T(14), T(14, 30), T(15), T(15, 30), T(16)    // the last start that ends by 17:00
        };
        Assert.Equal(expected, slots.ToArray());
    }

    [Fact]
    public void Slots_LongerJobsHaveFewerStarts()
    {
        var slots = BookingSlotRules.Slots(Tuesday, 150, Today, Now, 1, false, One(Day(lunch: false)), NoBusy, 5);
        Assert.Equal(T(8), slots.First());
        Assert.Equal(T(14, 30), slots.Last());                  // 14:30 + 2h30 = 17:00
    }

    [Fact]
    public void Slots_StartOnTheHalfHourEvenWhenTheScheduleStartsAtAnOddTime()
    {
        var window = new BookingSlotRules.Window(T(8, 10), T(11), Array.Empty<(TimeOnly, TimeOnly)>());
        var slots = BookingSlotRules.Slots(Tuesday, 60, Today, Now, 1, false, One(window), NoBusy, 5);
        Assert.Equal(new[] { T(8, 30), T(9), T(9, 30), T(10) }, slots.ToArray());
    }

    [Fact]
    public void Slots_LeaveOutTakenTimes()
    {
        var busy = new[] { new BookingSlotRules.Busy(T(9), 60) };
        var slots = BookingSlotRules.Slots(Tuesday, 60, Today, Now, 1, false, One(Day()), busy, 5);

        Assert.Contains(T(8), slots);
        Assert.DoesNotContain(T(8, 30), slots);
        Assert.DoesNotContain(T(9), slots);
        Assert.DoesNotContain(T(9, 30), slots);
        Assert.Contains(T(10), slots);
    }

    [Fact]
    public void Slots_AreEmpty_WhenTheDayItselfIsNotBookable()
    {
        Assert.Empty(BookingSlotRules.Slots(Tuesday, 60, Today, Now, 1, true, One(Day()), NoBusy, 5));                       // day off
        Assert.Empty(BookingSlotRules.Slots(Tuesday, 60, Today, Now, 1, false, Array.Empty<BookingSlotRules.Window>(), NoBusy, 5));
        Assert.Empty(BookingSlotRules.Slots(Today, 60, Today, Now, 1, false, One(Day()), NoBusy, 5));                         // too soon
    }

    [Fact]
    public void Slots_Today_OnlyShowTheFuture()
    {
        var slots = BookingSlotRules.Slots(Today, 60, Today, Now, 0, false, One(Day(lunch: false)), NoBusy, 5);
        Assert.Equal(T(10, 30), slots.First());                 // it is 10:00 now
    }

    [Fact]
    public void Slots_NeverCrossMidnight()
    {
        var window = new BookingSlotRules.Window(T(20), T(23, 59), Array.Empty<(TimeOnly, TimeOnly)>());
        var slots = BookingSlotRules.Slots(Tuesday, 60, Today, Now, 1, false, One(window), NoBusy, 5);
        Assert.Equal(T(22, 30), slots.Last());                  // 22:30 + 1h = 23:30
    }

    [Fact]
    public void Explain_HasASentenceForEveryProblem()
    {
        foreach (var check in Enum.GetValues<SlotCheck>().Where(c => c != SlotCheck.Ok))
            Assert.False(string.IsNullOrWhiteSpace(BookingSlotRules.Explain(check, 1)), check.ToString());
        Assert.Equal(string.Empty, BookingSlotRules.Explain(SlotCheck.Ok, 1));
        Assert.Contains("1 day of notice", BookingSlotRules.Explain(SlotCheck.NoticeTooShort, 1));
        Assert.Contains("3 days of notice", BookingSlotRules.Explain(SlotCheck.NoticeTooShort, 3));
    }

    [Theory]
    [InlineData(SlotCheck.NoticeTooShort, true)]
    [InlineData(SlotCheck.DayOff, true)]
    [InlineData(SlotCheck.DayFull, true)]
    [InlineData(SlotCheck.InBreak, false)]
    [InlineData(SlotCheck.Clash, false)]
    [InlineData(SlotCheck.TimePassed, false)]
    public void IsDateProblem_SeparatesDateAndTimeProblems(SlotCheck check, bool dateProblem)
        => Assert.Equal(dateProblem, BookingSlotRules.IsDateProblem(check));
}

public class BookProviderServiceTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly DateOnly Tuesday = new(2026, 10, 20);
    private static readonly DateOnly Wednesday = new(2026, 10, 21);
    private static readonly DateTime NowUtc = new(2026, 10, 14, 2, 0, 0);

    private static (BookProviderService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new BookProviderService(db, new FixedClock()), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new() { FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status };

    /// A bookable provider who works Tuesdays 8:00-17:00 with a 12:00-13:00 lunch break.
    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string approval = ApprovalStatuses.Approved,
        string account = AccountStatuses.Active, int minNotice = 1, int maxJobs = 5, string first = "Marco", string last = "Reyes",
        bool tuesdaySchedule = true)
    {
        var p = new Provider
        {
            BusinessName = $"{first}'s Works", ApprovalStatus = approval, MinNoticeDays = minNotice, MaxJobsPerDay = maxJobs,
            User = NewUser(first, last, Roles.ServiceProvider, account)
        };
        if (tuesdaySchedule)
        {
            p.Availabilities.Add(new ProviderAvailability
            {
                DayOfWeek = 2, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), IsAvailable = true,
                Breaks = { new ProviderBreak { BreakStart = new TimeOnly(12, 0), BreakEnd = new TimeOnly(13, 0) } }
            });
        }
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<Service> AddServiceAsync(ServisyoDbContext db, Provider p, string name = "Pipe Repair",
        decimal price = 600m, int minutes = 60, bool active = true, string category = "Plumbing", bool categoryActive = true)
    {
        var cat = await db.ServiceCategories.FirstOrDefaultAsync(c => c.CategoryName == category)
                  ?? new ServiceCategory { CategoryName = category, IsActive = categoryActive };
        var s = new Service { ProviderId = p.ProviderId, Category = cat, ServiceName = name, Price = price, DurationMinutes = minutes, IsActive = active };
        db.Services.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    private static async Task<Customer> AddCustomerAsync(ServisyoDbContext db, string first = "Ana", string last = "Reyes",
        string status = AccountStatuses.Active, string? address = "45 Mabini St, Quezon City")
    {
        var c = new Customer { User = NewUser(first, last, Roles.Customer, status), Address = address };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static async Task<Booking> AddBookingAsync(ServisyoDbContext db, Customer c, Provider p, string status, TimeOnly time, int minutes = 60, DateOnly? date = null)
    {
        var b = new Booking
        {
            CustomerId = c.CustomerId, ProviderId = p.ProviderId, BookingDate = date ?? Tuesday, BookingTime = time,
            TotalDurationMinutes = minutes, TotalFee = 600m, Status = status, RowVersion = new byte[] { 1 }
        };
        db.Bookings.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    private static BookProviderForm Form(Service s, TimeOnly? time = null, DateOnly? date = null, params Service[] more)
        => new()
        {
            ServiceIds = new[] { s }.Concat(more).Select(x => x.ServiceId).ToList(),
            Date = date ?? Tuesday, Time = time ?? new TimeOnly(9, 0),
            Location = "45 Mabini St, Quezon City", Description = "Kitchen sink pipe is leaking."
        };

    private static async Task<(Customer c, Provider p, Service s)> StandardAsync(ServisyoDbContext db)
    {
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, p);
        return (c, p, s);
    }

    // ------------------------------------------------------------------ who may book, and whom

    [Fact]
    public async Task OnlyAnActiveCustomer_CanUseBooking()
    {
        var (svc, db) = Create();
        var p = await AddProviderAsync(db);
        var s = await AddServiceAsync(db, p);
        var suspended = await AddCustomerAsync(db, status: AccountStatuses.Suspended);

        Assert.Equal(BookPageOutcome.NotAllowed, (await svc.GetPageAsync(p.UserId, p.ProviderId)).Outcome);              // a provider
        Assert.Equal(BookPageOutcome.NotAllowed, (await svc.GetPageAsync(suspended.UserId, p.ProviderId)).Outcome);
        Assert.Equal(BookOutcome.NotAllowed, (await svc.CreateAsync(suspended.UserId, p.ProviderId, Form(s))).Outcome);
        Assert.Equal(BookOutcome.NotAllowed, (await svc.CreateAsync(p.UserId, p.ProviderId, Form(s))).Outcome);
        Assert.False((await svc.GetSlotsAsync(suspended.UserId, p.ProviderId, Tuesday, new[] { s.ServiceId })).Allowed);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task ProvidersThatCannotBeBooked_AreRefused()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);

        var pending = await AddProviderAsync(db, approval: ApprovalStatuses.Pending, first: "Pen");
        var rejected = await AddProviderAsync(db, approval: ApprovalStatuses.Rejected, first: "Rej");
        var suspended = await AddProviderAsync(db, account: AccountStatuses.Suspended, first: "Sus");
        var noServices = await AddProviderAsync(db, first: "Non");
        var inactiveService = await AddProviderAsync(db, first: "Ina");
        var inactiveCategory = await AddProviderAsync(db, first: "Cat");
        await AddServiceAsync(db, pending);
        await AddServiceAsync(db, rejected);
        await AddServiceAsync(db, suspended);
        await AddServiceAsync(db, inactiveService, active: false);
        await AddServiceAsync(db, inactiveCategory, category: "Retired", categoryActive: false);

        foreach (var id in new[] { pending.ProviderId, rejected.ProviderId, suspended.ProviderId, noServices.ProviderId,
                                   inactiveService.ProviderId, inactiveCategory.ProviderId, 9999 })
        {
            Assert.Equal(BookPageOutcome.ProviderUnavailable, (await svc.GetPageAsync(c.UserId, id)).Outcome);
            Assert.False((await svc.GetSlotsAsync(c.UserId, id, Tuesday, new[] { 1 })).ProviderAvailable);
        }

        var form = new BookProviderForm { ServiceIds = new List<int> { 1 }, Date = Tuesday, Time = new TimeOnly(9, 0), Location = "x", Description = "y" };
        Assert.Equal(BookOutcome.ProviderUnavailable, (await svc.CreateAsync(c.UserId, pending.ProviderId, form)).Outcome);
        Assert.Empty(db.Bookings);
    }

    // ------------------------------------------------------------------ the form

    [Fact]
    public async Task Page_ListsTheServices_AndOffersTheSavedAddress()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db, minNotice: 2);
        await AddServiceAsync(db, p, "Pipe Repair", 600m, 60);
        await AddServiceAsync(db, p, "Aircon Cleaning", 900m, 90, category: "Cleaning");
        await AddServiceAsync(db, p, "Hidden", 100m, active: false);

        var page = (await svc.GetPageAsync(c.UserId, p.ProviderId)).Page!;

        Assert.Equal(new[] { "Aircon Cleaning", "Pipe Repair" }, page.Services.Select(s => s.Name).ToArray());    // by category, hidden one left out
        Assert.Equal("1 hr 30 min", page.Services[0].DurationText);
        Assert.Equal("₱900", page.Services[0].PriceText);
        Assert.Equal("Marco Reyes", page.ProviderName);
        Assert.Equal(new DateOnly(2026, 10, 16), page.EarliestDate);              // today + 2 days of notice
        Assert.Equal(new DateOnly(2027, 1, 12), page.LatestDate);                 // today + 90 days
        Assert.Equal("45 Mabini St, Quezon City", page.Form.Location);
    }

    // ------------------------------------------------------------------ free times

    [Fact]
    public async Task Slots_ForOneService_AreEveryHalfHourAroundTheBreak()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);

        var result = await svc.GetSlotsAsync(c.UserId, p.ProviderId, Tuesday, new[] { s.ServiceId });

        Assert.True(result.Allowed);
        Assert.Null(result.Message);
        Assert.Equal(14, result.Slots.Count);
        Assert.Equal(new SlotViewModel("08:00", "8:00 AM"), result.Slots.First());
        Assert.Equal(new SlotViewModel("16:00", "4:00 PM"), result.Slots.Last());
        Assert.DoesNotContain(result.Slots, x => x.Value == "12:00");
        Assert.DoesNotContain(result.Slots, x => x.Value == "11:30");
    }

    [Fact]
    public async Task Slots_UseTheTotalDurationOfAllChosenServices()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        var a = await AddServiceAsync(db, p, "A", 500m, 60);
        var b = await AddServiceAsync(db, p, "B", 700m, 90);                      // 60 + 90 = 150 minutes

        var result = await svc.GetSlotsAsync(c.UserId, p.ProviderId, Tuesday, new[] { a.ServiceId, b.ServiceId });

        Assert.Equal("14:30", result.Slots.Last().Value);                          // 14:30 + 2h30 = 17:00
        Assert.Contains(result.Slots, x => x.Value == "09:30");                                              // ends exactly when lunch starts
        Assert.DoesNotContain(result.Slots, x => x.Value is "10:00" or "10:30" or "11:00");                 // those would run into the lunch break
    }

    [Fact]
    public async Task Slots_LeaveOutTimesThatAreTaken_ButNotFinishedBookings()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var other = await AddCustomerAsync(db, "Ben", "Cruz");
        await AddBookingAsync(db, other, p, BookingStatuses.Accepted, new TimeOnly(9, 0));
        await AddBookingAsync(db, other, p, BookingStatuses.Cancelled, new TimeOnly(14, 0));
        await AddBookingAsync(db, other, p, BookingStatuses.Rejected, new TimeOnly(15, 0));

        var times = (await svc.GetSlotsAsync(c.UserId, p.ProviderId, Tuesday, new[] { s.ServiceId })).Slots.Select(x => x.Value).ToList();

        Assert.DoesNotContain("09:00", times);
        Assert.DoesNotContain("09:30", times);
        Assert.DoesNotContain("08:30", times);
        Assert.Contains("14:00", times);              // a cancelled booking frees the time
        Assert.Contains("15:00", times);              // so does a rejected one
    }

    [Fact]
    public async Task Slots_ExplainWhyADateHasNoTimes()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = p.ProviderId, UnavailableDate = new DateOnly(2026, 10, 27) });
        await db.SaveChangesAsync();

        var ids = new[] { s.ServiceId };
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.DayOff, 1), (await svc.GetSlotsAsync(c.UserId, p.ProviderId, new DateOnly(2026, 10, 27), ids)).Message);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.NotWorking, 1), (await svc.GetSlotsAsync(c.UserId, p.ProviderId, Wednesday, ids)).Message);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.NoticeTooShort, 1), (await svc.GetSlotsAsync(c.UserId, p.ProviderId, Today, ids)).Message);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.TooFar, 1), (await svc.GetSlotsAsync(c.UserId, p.ProviderId, Today.AddDays(120), ids)).Message);
        Assert.All(new[] { Wednesday, Today }, d => Assert.Empty(svc.GetSlotsAsync(c.UserId, p.ProviderId, d, ids).Result.Slots));
    }

    [Fact]
    public async Task Slots_NeedARealServiceOfThisProvider()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var otherProvider = await AddProviderAsync(db, first: "Liza", last: "Santos");
        var foreign = await AddServiceAsync(db, otherProvider, "Foreign", 100m);

        var none = await svc.GetSlotsAsync(c.UserId, p.ProviderId, Tuesday, Array.Empty<int>());
        var wrong = await svc.GetSlotsAsync(c.UserId, p.ProviderId, Tuesday, new[] { foreign.ServiceId });
        var mixed = await svc.GetSlotsAsync(c.UserId, p.ProviderId, Tuesday, new[] { s.ServiceId, foreign.ServiceId });

        foreach (var r in new[] { none, wrong, mixed })
        {
            Assert.Equal("Choose at least one service first.", r.Message);
            Assert.Empty(r.Slots);
        }
    }

    // ------------------------------------------------------------------ creating a booking

    [Fact]
    public async Task Create_SavesAPendingUnpaidBooking_ForTheSignedInCustomer()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s));

        Assert.True(result.Succeeded);
        Assert.Equal("Marco Reyes", result.ProviderName);

        var b = await db.Bookings.Include(x => x.Services).SingleAsync();
        Assert.Equal(result.BookingId, b.BookingId);
        Assert.Equal(c.CustomerId, b.CustomerId);
        Assert.Equal(p.ProviderId, b.ProviderId);
        Assert.Equal(BookingStatuses.Pending, b.Status);
        Assert.Equal(PaymentStatuses.Unpaid, b.PaymentStatus);
        Assert.Equal(Tuesday, b.BookingDate);
        Assert.Equal(new TimeOnly(9, 0), b.BookingTime);
        Assert.Equal(60, b.TotalDurationMinutes);
        Assert.Equal(600m, b.TotalFee);
        Assert.Equal("45 Mabini St, Quezon City", b.ServiceLocation);
        Assert.Equal("Kitchen sink pipe is leaking.", b.ServiceDescription);
        Assert.Equal(NowUtc, b.CreatedAt);

        var line = Assert.Single(b.Services);
        Assert.Equal(s.ServiceId, line.ServiceId);
        Assert.Equal(600m, line.PriceSnapshot);
        Assert.Equal(60, line.DurationMinutesSnapshot);
    }

    [Fact]
    public async Task Create_TellsTheProvider()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s));

        var note = await db.Notifications.SingleAsync();
        Assert.Equal(p.UserId, note.UserId);
        Assert.Equal($"You have a new booking request (Booking #{result.BookingId}).", note.Message);
        Assert.False(note.IsRead);
    }

    [Fact]
    public async Task Create_WithSeveralServices_AddsUpFeeAndDuration_FromTheDatabase()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db);
        var a = await AddServiceAsync(db, p, "A", 500m, 60);
        var b = await AddServiceAsync(db, p, "B", 700.50m, 90, category: "Cleaning");

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(a, new TimeOnly(13, 0), Tuesday, b));

        Assert.True(result.Succeeded);
        var booking = await db.Bookings.Include(x => x.Services).SingleAsync();
        Assert.Equal(150, booking.TotalDurationMinutes);
        Assert.Equal(1200.50m, booking.TotalFee);
        Assert.Equal(2, booking.Services.Count);
    }

    [Fact]
    public async Task Create_TrimsTheTextBoxes_AndIgnoresRepeatedServiceIds()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var form = Form(s);
        form.ServiceIds = new List<int> { s.ServiceId, s.ServiceId };
        form.Location = "   100 Ortigas Ave   ";
        form.Description = "  Fix it  ";

        Assert.True((await svc.CreateAsync(c.UserId, p.ProviderId, form)).Succeeded);

        var b = await db.Bookings.Include(x => x.Services).SingleAsync();
        Assert.Equal("100 Ortigas Ave", b.ServiceLocation);
        Assert.Equal("Fix it", b.ServiceDescription);
        Assert.Single(b.Services);
        Assert.Equal(600m, b.TotalFee);
    }

    [Fact]
    public async Task Create_TheCustomerIsTheSignedInUser_NeverSomeoneElse()
    {
        var (svc, db) = Create();
        var (ana, p, s) = await StandardAsync(db);
        var ben = await AddCustomerAsync(db, "Ben", "Cruz");

        await svc.CreateAsync(ben.UserId, p.ProviderId, Form(s));

        Assert.Equal(ben.CustomerId, (await db.Bookings.SingleAsync()).CustomerId);
    }

    // ------------------------------------------------------------------ what the form must contain

    [Fact]
    public async Task Create_ReportsEveryMissingField_AndSavesNothing()
    {
        var (svc, db) = Create();
        var (c, p, _) = await StandardAsync(db);

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, new BookProviderForm { ServiceIds = new List<int>(), Location = "  ", Description = "" });

        Assert.Equal(BookOutcome.Invalid, result.Outcome);
        Assert.Equal("Choose at least one service.", result.FieldErrors["ServiceIds"]);
        Assert.Equal("Choose a date.", result.FieldErrors["Date"]);
        Assert.Equal("Choose a time.", result.FieldErrors["Time"]);
        Assert.Equal("Enter the address where the service will be done.", result.FieldErrors["Location"]);
        Assert.Equal("Describe what you need done.", result.FieldErrors["Description"]);
        Assert.Empty(db.Bookings);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Create_RefusesTooLongText()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var form = Form(s);
        form.Location = new string('a', 256);
        form.Description = new string('b', 256);

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, form);

        Assert.True(result.FieldErrors.ContainsKey("Location"));
        Assert.True(result.FieldErrors.ContainsKey("Description"));
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task Create_RefusesServicesThatAreNotOfferedByThisProvider()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var otherProvider = await AddProviderAsync(db, first: "Liza", last: "Santos");
        var foreign = await AddServiceAsync(db, otherProvider, "Foreign", 1m);
        var inactive = await AddServiceAsync(db, p, "Old", 100m, active: false);
        var retired = await AddServiceAsync(db, p, "Retired", 100m, category: "Retired", categoryActive: false);

        foreach (var bad in new[] { foreign.ServiceId, inactive.ServiceId, retired.ServiceId, 9999 })
        {
            var form = Form(s);
            form.ServiceIds = new List<int> { s.ServiceId, bad };
            var result = await svc.CreateAsync(c.UserId, p.ProviderId, form);

            Assert.Equal(BookOutcome.Invalid, result.Outcome);
            Assert.True(result.FieldErrors.ContainsKey("ServiceIds"), $"service {bad}");
        }
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task Create_RefusesMoreThanTenServices()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var form = Form(s);
        form.ServiceIds = Enumerable.Range(1, 11).ToList();

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, form);

        Assert.Contains("at most 10", result.FieldErrors["ServiceIds"]);
        Assert.Empty(db.Bookings);
    }

    // ------------------------------------------------------------------ the provider's schedule rules

    [Fact]
    public async Task Create_RefusesADateWithoutEnoughNotice()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);        // minimum notice is 1 day

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, date: Today));

        Assert.Equal(BookOutcome.Invalid, result.Outcome);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.NoticeTooShort, 1), result.FieldErrors["Date"]);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task Create_RefusesADayTheProviderDoesNotWork()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, date: Wednesday));

        Assert.Equal(BookingSlotRules.Explain(SlotCheck.NotWorking, 1), result.FieldErrors["Date"]);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task Create_RefusesAnUnavailableDate_AndAScheduleThatIsSwitchedOff()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = p.ProviderId, UnavailableDate = Tuesday });
        await db.SaveChangesAsync();

        var dayOff = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s));
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.DayOff, 1), dayOff.FieldErrors["Date"]);

        db.ProviderUnavailableDates.RemoveRange(db.ProviderUnavailableDates);
        (await db.ProviderAvailabilities.SingleAsync()).IsAvailable = false;
        await db.SaveChangesAsync();

        var off = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s));
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.NotWorking, 1), off.FieldErrors["Date"]);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task Create_RefusesTimesOutsideTheWorkingHours_OrInsideABreak()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);

        var early = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(7, 0)));
        var late = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(16, 30)));
        var lunch = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(12, 0)));
        var intoLunch = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(11, 30)));

        Assert.Equal(BookingSlotRules.Explain(SlotCheck.OutsideHours, 1), early.FieldErrors["Time"]);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.OutsideHours, 1), late.FieldErrors["Time"]);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.InBreak, 1), lunch.FieldErrors["Time"]);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.InBreak, 1), intoLunch.FieldErrors["Time"]);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task Create_RefusesATimeThatIsNotOneOfTheOfferedStarts()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(9, 10)));

        Assert.Equal("Please choose one of the available times.", result.FieldErrors["Time"]);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task Create_TodayNeedsAFutureTime_WhenNoNoticeIsRequired()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db, minNotice: 0, tuesdaySchedule: false);
        p.Availabilities.Add(new ProviderAvailability { DayOfWeek = 3, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0) });   // Wednesday = today
        await db.SaveChangesAsync();
        var s = await AddServiceAsync(db, p);

        var past = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(9, 0), Today));
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.TimePassed, 0), past.FieldErrors["Time"]);

        var future = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(11, 0), Today));
        Assert.True(future.Succeeded);
    }

    // ------------------------------------------------------------------ conflicts and the daily limit

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task Create_RefusesATimeThatOverlapsAnActiveBooking(string status)
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var other = await AddCustomerAsync(db, "Ben", "Cruz");
        await AddBookingAsync(db, other, p, status, new TimeOnly(9, 0));

        var same = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(9, 0)));
        var overlap = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(9, 30)));
        var touching = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(10, 0)));      // starts when the other ends

        Assert.Equal(BookingSlotRules.Explain(SlotCheck.Clash, 1), same.FieldErrors["Time"]);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.Clash, 1), overlap.FieldErrors["Time"]);
        Assert.True(touching.Succeeded);
        Assert.Equal(2, db.Bookings.Count());           // the other customer's booking + the touching one
    }

    [Theory]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.Rejected)]
    [InlineData(BookingStatuses.Completed)]
    public async Task Create_AFinishedOrCancelledBookingDoesNotBlockTheTime(string status)
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var other = await AddCustomerAsync(db, "Ben", "Cruz");
        await AddBookingAsync(db, other, p, status, new TimeOnly(9, 0));

        Assert.True((await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(9, 0)))).Succeeded);
    }

    [Fact]
    public async Task Create_TheSecondCustomerCannotTakeTheSameTime()
    {
        var (svc, db) = Create();
        var (ana, p, s) = await StandardAsync(db);
        var ben = await AddCustomerAsync(db, "Ben", "Cruz");

        Assert.True((await svc.CreateAsync(ana.UserId, p.ProviderId, Form(s))).Succeeded);
        var second = await svc.CreateAsync(ben.UserId, p.ProviderId, Form(s));

        Assert.Equal(BookOutcome.Invalid, second.Outcome);
        Assert.Equal(BookingSlotRules.Explain(SlotCheck.Clash, 1), second.FieldErrors["Time"]);
        Assert.Single(db.Bookings);
        Assert.Single(db.Notifications);                 // the provider is only told about the booking that was made
    }

    [Fact]
    public async Task Create_RefusesADayThatHasReachedTheMaximumJobs()
    {
        var (svc, db) = Create();
        var c = await AddCustomerAsync(db);
        var p = await AddProviderAsync(db, maxJobs: 2);
        var s = await AddServiceAsync(db, p);
        var other = await AddCustomerAsync(db, "Ben", "Cruz");
        await AddBookingAsync(db, other, p, BookingStatuses.Pending, new TimeOnly(8, 0));
        await AddBookingAsync(db, other, p, BookingStatuses.Accepted, new TimeOnly(10, 0));
        await AddBookingAsync(db, other, p, BookingStatuses.Cancelled, new TimeOnly(14, 0));      // does not count

        var result = await svc.CreateAsync(c.UserId, p.ProviderId, Form(s, new TimeOnly(15, 0)));

        Assert.Equal(BookingSlotRules.Explain(SlotCheck.DayFull, 1), result.FieldErrors["Date"]);
        Assert.Equal(3, db.Bookings.Count());
    }

    [Fact]
    public async Task Create_ABookingOnAnotherProvidersDay_DoesNotBlock()
    {
        var (svc, db) = Create();
        var (c, p, s) = await StandardAsync(db);
        var otherProvider = await AddProviderAsync(db, first: "Liza", last: "Santos");
        var other = await AddCustomerAsync(db, "Ben", "Cruz");
        await AddBookingAsync(db, other, otherProvider, BookingStatuses.Accepted, new TimeOnly(9, 0));

        Assert.True((await svc.CreateAsync(c.UserId, p.ProviderId, Form(s))).Succeeded);
    }
}
