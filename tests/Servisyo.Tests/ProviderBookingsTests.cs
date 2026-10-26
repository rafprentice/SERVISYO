using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ProviderBookingActionInfoTests
{
    [Fact]
    public void Pending_OffersAcceptAndReject()
    {
        var actions = BookingActionInfo.ForStatus(BookingStatuses.Pending);

        Assert.Equal(new[] { "Accept", "Reject" }, actions.Select(a => a.Label));
        Assert.Equal(new[] { BookingStatuses.Accepted, BookingStatuses.Rejected }, actions.Select(a => a.ToStatus));
        Assert.True(actions[1].Confirm);          // rejecting asks "are you sure?"; accepting does not
        Assert.False(actions[0].Confirm);
    }

    [Fact]
    public void Accepted_OffersMarkInProgress_AndInProgress_OffersMarkCompleted()
    {
        var accepted = Assert.Single(BookingActionInfo.ForStatus(BookingStatuses.Accepted));
        var inProgress = Assert.Single(BookingActionInfo.ForStatus(BookingStatuses.InProgress));

        Assert.Equal("Mark In Progress", accepted.Label);
        Assert.Equal(BookingStatuses.InProgress, accepted.ToStatus);
        Assert.Equal("Mark Completed", inProgress.Label);
        Assert.Equal(BookingStatuses.Completed, inProgress.ToStatus);
    }

    [Theory]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Rejected)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData("Something else")]
    public void FinishedBookings_OfferNoActions(string status)
        => Assert.Empty(BookingActionInfo.ForStatus(status));

    [Fact]
    public void EveryActionStartsFromTheStatusItsButtonIsShownFor()
    {
        foreach (var action in Enum.GetValues<BookingAction>())
        {
            var info = BookingActionInfo.For(action);
            Assert.Contains(info, BookingActionInfo.ForStatus(info.FromStatus));
        }
    }

    [Fact]
    public void AnUnknownAction_IsNotMadeUp()
        => Assert.Throws<ArgumentOutOfRangeException>(() => BookingActionInfo.For((BookingAction)99));
}

public class ProviderBookingTabTests
{
    [Fact]
    public void TheTabsAreInTheDocumentedOrder()
        => Assert.Equal(
            new[] { "All", "Pending", "Accepted", "In Progress", "Completed", "Rejected", "Cancelled" },
            ProviderBookingTab.All.Select(t => t.Label));

    [Theory]
    [InlineData(null, "all")]
    [InlineData("", "all")]
    [InlineData("bogus", "all")]
    [InlineData("pending", "pending")]
    [InlineData("PENDING", "pending")]
    [InlineData(" in-progress ", "in-progress")]
    [InlineData("cancelled", "cancelled")]
    public void Find_FallsBackToAll(string? key, string expected)
        => Assert.Equal(expected, ProviderBookingTab.Find(key).Key);

    [Theory]
    [InlineData(BookingStatuses.Pending, "pending")]
    [InlineData(BookingStatuses.Accepted, "accepted")]
    [InlineData(BookingStatuses.InProgress, "progress")]
    [InlineData(BookingStatuses.Completed, "completed")]
    [InlineData(BookingStatuses.Rejected, "danger")]
    [InlineData(BookingStatuses.Cancelled, "muted")]
    public void Badge_UsesTheStatusColour(string status, string tone)
        => Assert.Equal(tone, ProviderBookingBadge.Tone(status));
}

public class BookingFormatTests
{
    [Fact]
    public void DateTimeText_ReadsLikeTheMockup()
        => Assert.Equal("Sep 20, 2026 · 9:00 AM", BookingFormat.DateTimeText(new DateOnly(2026, 9, 20), new TimeOnly(9, 0)));

    [Fact]
    public void DateTimeText_UsesAfternoonHours()
        => Assert.Equal("Oct 3, 2026 · 2:30 PM", BookingFormat.DateTimeText(new DateOnly(2026, 10, 3), new TimeOnly(14, 30)));

    [Fact]
    public void SubmittedText_IsShownInCebuTime()
        // 6 pm UTC on 15 Sep is 2 am on 16 Sep in the Philippines
        => Assert.Equal("Sep 16, 2026", BookingFormat.SubmittedText(new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void FromLine_ShowsCustomerAndCategory()
    {
        Assert.Equal("from Ana Reyes · Plumbing", new ProviderBookingCardViewModel { CustomerName = "Ana Reyes", CategoryText = "Plumbing" }.FromLine);
        Assert.Equal("from Ana Reyes", new ProviderBookingCardViewModel { CustomerName = "Ana Reyes" }.FromLine);
    }
}

public class ProviderBookingsServiceTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime NowUtc = new(2026, 10, 14, 2, 0, 0);
    private static readonly DateOnly Day = new(2026, 10, 20);

    private static (ProviderBookingsService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new ProviderBookingsService(db, new FixedClock()), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string approval = ApprovalStatuses.Approved,
        string account = AccountStatuses.Active, string business = "Marco's Plumbing Works")
    {
        var p = new Provider { BusinessName = business, ApprovalStatus = approval, User = NewUser("Marco", "Reyes", Roles.ServiceProvider, account) };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<Customer> AddCustomerAsync(ServisyoDbContext db, string first = "Ana", string last = "Reyes")
    {
        var c = new Customer { User = NewUser(first, last, Roles.Customer) };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static async Task<Booking> AddBookingAsync(ServisyoDbContext db, Customer c, Provider p, string status,
        DateOnly? date = null, TimeOnly? time = null, int minutes = 60, DateTime? createdUtc = null,
        string? location = "Lahug, Cebu City", string? notes = "Kitchen sink is leaking.",
        (string Service, string Category)[]? services = null)
    {
        var b = new Booking
        {
            CustomerId = c.CustomerId, ProviderId = p.ProviderId,
            BookingDate = date ?? Day, BookingTime = time ?? new TimeOnly(9, 0), TotalDurationMinutes = minutes,
            TotalFee = 600m, Status = status, ServiceLocation = location, ServiceDescription = notes,
            // a booking past "Accepted" has been paid (demo "Approve & Pay"), so the provider may start it
            PaymentStatus = status is BookingStatuses.Accepted or BookingStatuses.InProgress or BookingStatuses.Completed
                ? PaymentStatuses.Paid : PaymentStatuses.Unpaid,
            CreatedAt = createdUtc ?? NowUtc.AddDays(-1), UpdatedAt = NowUtc.AddDays(-1),
            RowVersion = new byte[] { 1 }
        };

        foreach (var (serviceName, categoryName) in services ?? new[] { ("Pipe Repair", "Plumbing") })
        {
            var category = await db.ServiceCategories.FirstOrDefaultAsync(x => x.CategoryName == categoryName)
                           ?? new ServiceCategory { CategoryName = categoryName };
            var service = new Service { ProviderId = p.ProviderId, Category = category, ServiceName = serviceName, Price = 600m, DurationMinutes = 60 };
            b.Services.Add(new BookingService { Service = service, PriceSnapshot = 600m, DurationMinutesSnapshot = 60 });
        }

        db.Bookings.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    private static async Task<string> StatusOf(ServisyoDbContext db, int bookingId)
        => (await db.Bookings.AsNoTracking().FirstAsync(b => b.BookingId == bookingId)).Status;

    // ------------------------------------------------------------------ access

    [Fact]
    public async Task OnlyAnActiveApprovedProvider_CanOpenThePage()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var approved = await AddProviderAsync(db);
        var pending = await AddProviderAsync(db, ApprovalStatuses.Pending);
        var rejected = await AddProviderAsync(db, ApprovalStatuses.Rejected);
        var suspended = await AddProviderAsync(db, account: AccountStatuses.Suspended);
        var deactivated = await AddProviderAsync(db, account: AccountStatuses.Deactivated);

        var expected = new (int UserId, ProviderDashboardAccess Access)[]
        {
            (approved.UserId, ProviderDashboardAccess.Allowed),
            (pending.UserId, ProviderDashboardAccess.Pending),
            (rejected.UserId, ProviderDashboardAccess.Rejected),
            (suspended.UserId, ProviderDashboardAccess.NotAllowed),
            (deactivated.UserId, ProviderDashboardAccess.NotAllowed),
            (customer.UserId, ProviderDashboardAccess.NotAllowed),
            (999_999, ProviderDashboardAccess.NotAllowed)
        };

        foreach (var (userId, access) in expected)
        {
            var result = await svc.GetAsync(userId, null, 1);
            Assert.Equal(access, result.Access);
            Assert.Equal(access == ProviderDashboardAccess.Allowed, result.Page is not null);
        }
    }

    [Fact]
    public async Task OnlyAnActiveApprovedProvider_CanChangeABookingStatus()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var pending = await AddProviderAsync(db, ApprovalStatuses.Pending);
        var rejected = await AddProviderAsync(db, ApprovalStatuses.Rejected);
        var suspended = await AddProviderAsync(db, account: AccountStatuses.Suspended);
        var deactivated = await AddProviderAsync(db, account: AccountStatuses.Deactivated);

        var expected = new (Provider Provider, ProviderDashboardAccess Access)[]
        {
            (pending, ProviderDashboardAccess.Pending),
            (rejected, ProviderDashboardAccess.Rejected),
            (suspended, ProviderDashboardAccess.NotAllowed),
            (deactivated, ProviderDashboardAccess.NotAllowed)
        };

        foreach (var (provider, access) in expected)
        {
            var booking = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending);

            var result = await svc.ChangeStatusAsync(provider.UserId, booking.BookingId, BookingAction.Accept);

            Assert.False(result.Succeeded);
            Assert.Equal(access, result.Access);
            Assert.Equal(BookingStatuses.Pending, await StatusOf(db, booking.BookingId));
        }

        Assert.Empty(await db.Notifications.ToListAsync());
    }

    // -------------------------------------------------------------------- list

    [Fact]
    public async Task TabCounts_CountOnlyThisProvidersBookings()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var mine = await AddProviderAsync(db);
        var other = await AddProviderAsync(db, business: "Someone else");

        var plan = new[]
        {
            BookingStatuses.Pending, BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress,
            BookingStatuses.Completed, BookingStatuses.Completed, BookingStatuses.Completed,
            BookingStatuses.Rejected, BookingStatuses.Cancelled
        };
        foreach (var status in plan) await AddBookingAsync(db, customer, mine, status);
        await AddBookingAsync(db, customer, other, BookingStatuses.Pending);
        await AddBookingAsync(db, customer, other, BookingStatuses.Completed);

        var page = (await svc.GetAsync(mine.UserId, null, 1)).Page!;
        var counts = page.Tabs.ToDictionary(t => t.Key, t => t.Count);

        Assert.Equal(new[] { "all", "pending", "accepted", "in-progress", "completed", "rejected", "cancelled" }, page.Tabs.Select(t => t.Key));
        Assert.Equal(9, counts["all"]);
        Assert.Equal(2, counts["pending"]);
        Assert.Equal(1, counts["accepted"]);
        Assert.Equal(1, counts["in-progress"]);
        Assert.Equal(3, counts["completed"]);
        Assert.Equal(1, counts["rejected"]);
        Assert.Equal(1, counts["cancelled"]);
        Assert.Equal(9, page.Cards.Count);
    }

    [Fact]
    public async Task ACard_ShowsWhatTheMockupShows()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db, "Ana", "Reyes");
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, customer, provider, BookingStatuses.Accepted,
            date: new DateOnly(2026, 9, 20), time: new TimeOnly(9, 0),
            createdUtc: new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc),
            location: "  Lahug, Cebu City  ", notes: "  Kitchen sink is leaking.  ",
            services: new[] { ("Pipe Repair", "Plumbing"), ("Waterproofing", "Roofing") });

        var card = Assert.Single((await svc.GetAsync(provider.UserId, null, 1)).Page!.Cards);

        Assert.Equal(booking.BookingId, card.BookingId);
        Assert.Equal("Pipe Repair & Waterproofing", card.Title);
        Assert.Equal("Ana Reyes", card.CustomerName);
        Assert.Equal("Plumbing, Roofing", card.CategoryText);
        Assert.Equal("from Ana Reyes · Plumbing, Roofing", card.FromLine);
        Assert.Equal("Accepted", card.Status);
        Assert.Equal("accepted", card.Tone);
        Assert.Equal("Sep 20, 2026 · 9:00 AM", card.WhenText);
        Assert.Equal("Lahug, Cebu City", card.Location);
        Assert.Equal("Sep 16, 2026", card.SubmittedText);
        Assert.Equal("Kitchen sink is leaking.", card.Notes);
        Assert.Equal(new[] { "Mark In Progress" }, card.Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task ABookingWithoutLocationOrNotes_StillShows()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, location: "   ", notes: null);

        var card = Assert.Single((await svc.GetAsync(provider.UserId, null, 1)).Page!.Cards);

        Assert.Null(card.Location);
        Assert.Null(card.Notes);
    }

    [Fact]
    public async Task Cards_AreNewestRequestFirst()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var oldest = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, createdUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var third = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, createdUtc: new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));
        var tieFirst = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, createdUtc: new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc));
        var tieSecond = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, createdUtc: new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc));

        var ids = (await svc.GetAsync(provider.UserId, null, 1)).Page!.Cards.Select(c => c.BookingId).ToList();

        Assert.Equal(new[] { tieSecond.BookingId, tieFirst.BookingId, third.BookingId, oldest.BookingId }, ids);
    }

    [Fact]
    public async Task TheChosenTab_ListsOnlyThatStatus()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var pending = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Accepted);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Completed);

        var page = (await svc.GetAsync(provider.UserId, "pending", 1)).Page!;

        Assert.Equal("pending", page.ActiveTab);
        Assert.Equal(1, page.TotalMatching);
        Assert.Equal(pending.BookingId, Assert.Single(page.Cards).BookingId);
        Assert.Equal(3, page.Tabs.First(t => t.Key == "all").Count);       // the counts never change with the tab
    }

    [Fact]
    public async Task AnUnknownTab_ShowsEverything()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Pending);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Completed);

        var page = (await svc.GetAsync(provider.UserId, "'; DROP TABLE BOOKINGS;--", 1)).Page!;

        Assert.Equal("all", page.ActiveTab);
        Assert.Equal(2, page.Cards.Count);
    }

    [Fact]
    public async Task AnEmptyTab_HasNoCards()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Pending);

        var page = (await svc.GetAsync(provider.UserId, "cancelled", 1)).Page!;

        Assert.Empty(page.Cards);
        Assert.Equal(0, page.TotalMatching);
        Assert.Equal(1, page.PageCount);
    }

    [Fact]
    public async Task ABrandNewProvider_SeesAllZeroes()
    {
        var (svc, db) = Create();
        var provider = await AddProviderAsync(db);

        var page = (await svc.GetAsync(provider.UserId, null, 1)).Page!;

        Assert.Empty(page.Cards);
        Assert.All(page.Tabs, t => Assert.Equal(0, t.Count));
    }

    [Fact]
    public async Task LongLists_AreSplitIntoPages_AndOutOfRangePagesAreClamped()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        for (var i = 0; i < 25; i++)
            await AddBookingAsync(db, customer, provider, BookingStatuses.Completed, createdUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i));

        var first = (await svc.GetAsync(provider.UserId, null, 1)).Page!;
        var second = (await svc.GetAsync(provider.UserId, null, 2)).Page!;
        var tooFar = (await svc.GetAsync(provider.UserId, null, 99)).Page!;
        var tooLow = (await svc.GetAsync(provider.UserId, null, -3)).Page!;

        Assert.Equal(2, first.PageCount);
        Assert.Equal(ProviderBookingsPageViewModel.PageSize, first.Cards.Count);
        Assert.Equal(5, second.Cards.Count);
        Assert.Equal(2, tooFar.Page);
        Assert.Equal(5, tooFar.Cards.Count);
        Assert.Equal(1, tooLow.Page);
        Assert.Empty(first.Cards.Select(c => c.BookingId).Intersect(second.Cards.Select(c => c.BookingId)));
    }

    [Fact]
    public async Task ActionButtons_FollowTheStatus()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        foreach (var status in new[] { BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress,
                                       BookingStatuses.Completed, BookingStatuses.Rejected, BookingStatuses.Cancelled })
            await AddBookingAsync(db, customer, provider, status);

        var cards = (await svc.GetAsync(provider.UserId, null, 1)).Page!.Cards.ToDictionary(c => c.Status);

        Assert.Equal(new[] { "Accept", "Reject" }, cards[BookingStatuses.Pending].Actions.Select(a => a.Label));
        Assert.Equal(new[] { "Mark In Progress" }, cards[BookingStatuses.Accepted].Actions.Select(a => a.Label));
        Assert.Equal(new[] { "Mark Completed" }, cards[BookingStatuses.InProgress].Actions.Select(a => a.Label));
        Assert.Empty(cards[BookingStatuses.Completed].Actions);
        Assert.Empty(cards[BookingStatuses.Rejected].Actions);
        Assert.Empty(cards[BookingStatuses.Cancelled].Actions);
    }

    // ----------------------------------------------------------- status changes

    [Theory]
    [InlineData(BookingStatuses.Pending, BookingAction.Accept, BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.Pending, BookingAction.Reject, BookingStatuses.Rejected)]
    [InlineData(BookingStatuses.Accepted, BookingAction.Start, BookingStatuses.InProgress)]
    [InlineData(BookingStatuses.InProgress, BookingAction.Complete, BookingStatuses.Completed)]
    public async Task AllowedChanges_UpdateTheBooking_AndTellTheCustomer(string from, BookingAction action, string to)
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, customer, provider, from);

        var result = await svc.ChangeStatusAsync(provider.UserId, booking.BookingId, action);

        Assert.True(result.Succeeded);
        Assert.Equal(BookingActionOutcome.Success, result.Outcome);
        Assert.Contains($"#{booking.BookingId}", result.Message);

        var saved = await db.Bookings.AsNoTracking().FirstAsync(b => b.BookingId == booking.BookingId);
        Assert.Equal(to, saved.Status);
        Assert.Equal(NowUtc, saved.UpdatedAt);

        // same wording as usp_UpdateBookingStatus in the PDF, sent to the customer's user account
        var note = Assert.Single(await db.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(customer.UserId, note.UserId);
        Assert.Equal($"Your booking #{booking.BookingId} is now {to}.", note.Message);
        Assert.False(note.IsRead);
    }

    [Fact]
    public async Task EveryOtherStatusAndActionPair_IsRefused_AndChangesNothing()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);

        var allowed = new HashSet<(string, BookingAction)>
        {
            (BookingStatuses.Pending, BookingAction.Accept),
            (BookingStatuses.Pending, BookingAction.Reject),
            (BookingStatuses.Accepted, BookingAction.Start),
            (BookingStatuses.InProgress, BookingAction.Complete)
        };
        var statuses = new[]
        {
            BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress,
            BookingStatuses.Completed, BookingStatuses.Rejected, BookingStatuses.Cancelled
        };

        var tried = 0;
        foreach (var status in statuses)
        {
            foreach (var action in Enum.GetValues<BookingAction>())
            {
                if (allowed.Contains((status, action))) continue;

                var booking = await AddBookingAsync(db, customer, provider, status);
                var result = await svc.ChangeStatusAsync(provider.UserId, booking.BookingId, action);

                Assert.Equal(BookingActionOutcome.InvalidTransition, result.Outcome);
                Assert.False(result.Succeeded);
                Assert.Equal(status, await StatusOf(db, booking.BookingId));
                tried++;
            }
        }

        Assert.Equal(6 * 4 - 4, tried);
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task ARefusal_ExplainsTheCurrentStatus()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, customer, provider, BookingStatuses.Cancelled);

        var result = await svc.ChangeStatusAsync(provider.UserId, booking.BookingId, BookingAction.Accept);

        Assert.Equal($"Booking #{booking.BookingId} is cancelled, so it can't be accepted.", result.Message);
    }

    [Fact]
    public async Task PressingAcceptTwice_OnlyWorksOnce()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending);

        var first = await svc.ChangeStatusAsync(provider.UserId, booking.BookingId, BookingAction.Accept);
        var second = await svc.ChangeStatusAsync(provider.UserId, booking.BookingId, BookingAction.Accept);

        Assert.True(first.Succeeded);
        Assert.Equal(BookingActionOutcome.InvalidTransition, second.Outcome);
        Assert.Single(await db.Notifications.ToListAsync());               // the customer is told once
    }

    [Fact]
    public async Task ACancelledBooking_CannotBeAcceptedAnyMore()
    {
        // the customer cancelled while the provider still had the page open
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending);
        var tracked = await db.Bookings.FirstAsync(b => b.BookingId == booking.BookingId);
        tracked.Status = BookingStatuses.Cancelled;
        await db.SaveChangesAsync();

        var result = await svc.ChangeStatusAsync(provider.UserId, booking.BookingId, BookingAction.Accept);

        Assert.Equal(BookingActionOutcome.InvalidTransition, result.Outcome);
        Assert.Equal(BookingStatuses.Cancelled, await StatusOf(db, booking.BookingId));
    }

    [Fact]
    public async Task AProvider_CannotTouchAnotherProvidersBooking()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var owner = await AddProviderAsync(db, business: "Owner");
        var intruder = await AddProviderAsync(db, business: "Intruder");
        var booking = await AddBookingAsync(db, customer, owner, BookingStatuses.Pending);

        foreach (var action in Enum.GetValues<BookingAction>())
        {
            var result = await svc.ChangeStatusAsync(intruder.UserId, booking.BookingId, action);

            Assert.Equal(BookingActionOutcome.NotFound, result.Outcome);       // looks exactly like a missing booking
            Assert.Equal(ProviderDashboardAccess.Allowed, result.Access);
        }

        Assert.Equal(BookingStatuses.Pending, await StatusOf(db, booking.BookingId));
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task AnotherProvidersBooking_NeverAppearsInTheList()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var owner = await AddProviderAsync(db, business: "Owner");
        var other = await AddProviderAsync(db, business: "Other");
        await AddBookingAsync(db, customer, owner, BookingStatuses.Pending);

        var page = (await svc.GetAsync(other.UserId, null, 1)).Page!;

        Assert.Empty(page.Cards);
    }

    [Fact]
    public async Task AMissingBooking_IsNotFound()
    {
        var (svc, db) = Create();
        var provider = await AddProviderAsync(db);

        var result = await svc.ChangeStatusAsync(provider.UserId, 12345, BookingAction.Accept);

        Assert.Equal(BookingActionOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task AnActionNumberThatIsNotOneOfTheFour_IsRefused()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending);

        var result = await svc.ChangeStatusAsync(provider.UserId, booking.BookingId, (BookingAction)99);

        Assert.Equal(BookingActionOutcome.InvalidTransition, result.Outcome);
        Assert.Equal(BookingStatuses.Pending, await StatusOf(db, booking.BookingId));
    }

    // ---------------------------------------------------------- accept: overlap

    [Fact]
    public async Task Accept_IsRefused_WhenItOverlapsAnAcceptedJob()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var accepted = await AddBookingAsync(db, customer, provider, BookingStatuses.Accepted, time: new TimeOnly(9, 0), minutes: 60);
        var request = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: new TimeOnly(9, 30), minutes: 60);

        var result = await svc.ChangeStatusAsync(provider.UserId, request.BookingId, BookingAction.Accept);

        Assert.Equal(BookingActionOutcome.Overlap, result.Outcome);
        Assert.Contains($"#{accepted.BookingId}", result.Message);
        Assert.Contains("9:00 AM", result.Message);
        Assert.Contains("10:00 AM", result.Message);
        Assert.Equal(BookingStatuses.Pending, await StatusOf(db, request.BookingId));
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Accept_IsRefused_WhenItOverlapsAJobAlreadyInProgress()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, customer, provider, BookingStatuses.InProgress, time: new TimeOnly(9, 0), minutes: 120);
        var request = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: new TimeOnly(10, 0), minutes: 30);

        var result = await svc.ChangeStatusAsync(provider.UserId, request.BookingId, BookingAction.Accept);

        Assert.Equal(BookingActionOutcome.Overlap, result.Outcome);
    }

    [Fact]
    public async Task Accept_IsRefused_WhenTheNewRequestContainsTheAcceptedJob()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Accepted, time: new TimeOnly(10, 0), minutes: 30);
        var request = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: new TimeOnly(9, 0), minutes: 180);

        var result = await svc.ChangeStatusAsync(provider.UserId, request.BookingId, BookingAction.Accept);

        Assert.Equal(BookingActionOutcome.Overlap, result.Outcome);
    }

    [Fact]
    public async Task Accept_Works_WhenJobsOnlyTouchEndToStart()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Accepted, time: new TimeOnly(9, 0), minutes: 60);
        var after = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: new TimeOnly(10, 0), minutes: 60);
        var before = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: new TimeOnly(8, 0), minutes: 60);

        Assert.True((await svc.ChangeStatusAsync(provider.UserId, after.BookingId, BookingAction.Accept)).Succeeded);
        Assert.True((await svc.ChangeStatusAsync(provider.UserId, before.BookingId, BookingAction.Accept)).Succeeded);
    }

    [Fact]
    public async Task Accept_IgnoresOtherDays_OtherProviders_AndJobsThatAreNotActive()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var otherProvider = await AddProviderAsync(db, business: "Other");
        var nine = new TimeOnly(9, 0);

        await AddBookingAsync(db, customer, provider, BookingStatuses.Accepted, date: Day.AddDays(1), time: nine);   // another day
        await AddBookingAsync(db, customer, otherProvider, BookingStatuses.Accepted, time: nine);                    // another provider
        await AddBookingAsync(db, customer, provider, BookingStatuses.Completed, time: nine);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Cancelled, time: nine);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Rejected, time: nine);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: nine);                          // only a request
        var request = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: nine);

        var result = await svc.ChangeStatusAsync(provider.UserId, request.BookingId, BookingAction.Accept);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AcceptingOneRequest_BlocksTheOtherOneThatClashesWithIt()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var first = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: new TimeOnly(9, 0), minutes: 120);
        var second = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: new TimeOnly(10, 0), minutes: 60);

        var a = await svc.ChangeStatusAsync(provider.UserId, first.BookingId, BookingAction.Accept);
        var b = await svc.ChangeStatusAsync(provider.UserId, second.BookingId, BookingAction.Accept);

        Assert.True(a.Succeeded);
        Assert.Equal(BookingActionOutcome.Overlap, b.Outcome);
        Assert.Equal(BookingStatuses.Pending, await StatusOf(db, second.BookingId));

        // the provider can still turn the clashing request down
        Assert.True((await svc.ChangeStatusAsync(provider.UserId, second.BookingId, BookingAction.Reject)).Succeeded);
    }

    [Fact]
    public async Task Reject_IsNeverBlockedByOverlap()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, customer, provider, BookingStatuses.Accepted, time: new TimeOnly(9, 0), minutes: 60);
        var request = await AddBookingAsync(db, customer, provider, BookingStatuses.Pending, time: new TimeOnly(9, 0), minutes: 60);

        var result = await svc.ChangeStatusAsync(provider.UserId, request.BookingId, BookingAction.Reject);

        Assert.True(result.Succeeded);
        Assert.Equal(BookingStatuses.Rejected, await StatusOf(db, request.BookingId));
    }
}
