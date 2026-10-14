using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class CustomerBookingsViewModelTests
{
    [Theory]
    [InlineData("all", "all")]
    [InlineData("PENDING", "pending")]
    [InlineData(" accepted ", "accepted")]
    [InlineData("in-progress", "in-progress")]
    [InlineData("completed", "completed")]
    [InlineData("rejected", "rejected")]
    [InlineData("cancelled", "cancelled")]
    [InlineData("nonsense", "all")]          // unknown keys fall back to "All"
    [InlineData("", "all")]
    [InlineData(null, "all")]
    public void Tab_Find_FallsBackToAll(string? asked, string expectedKey)
        => Assert.Equal(expectedKey, CustomerBookingTab.Find(asked).Key);

    [Fact]
    public void Tabs_CoverEveryBookingStatus_ExactlyOnce()
    {
        var statuses = CustomerBookingTab.All.Where(t => t.Status is not null).Select(t => t.Status!).OrderBy(s => s).ToArray();
        var expected = new[]
        {
            BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress,
            BookingStatuses.Completed, BookingStatuses.Rejected, BookingStatuses.Cancelled
        }.OrderBy(s => s).ToArray();

        Assert.Equal(expected, statuses);
    }

    [Fact]
    public void FromLine_ShowsProviderAndCategory()
    {
        Assert.Equal("Marco Reyes · Plumbing", new CustomerBookingCardViewModel { ProviderName = "Marco Reyes", CategoryText = "Plumbing" }.FromLine);
        Assert.Equal("Marco Reyes", new CustomerBookingCardViewModel { ProviderName = "Marco Reyes" }.FromLine);
        Assert.Equal("Marco Reyes", new CustomerBookingCardViewModel { ProviderName = "Marco Reyes", CategoryText = "  " }.FromLine);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(21, 3)]
    public void PageCount(int total, int pages)
        => Assert.Equal(pages, new CustomerBookingsPageViewModel { TotalMatching = total }.PageCount);

    [Fact]
    public void HasAnyBookings_FollowsTheAllTab()
    {
        Assert.False(new CustomerBookingsPageViewModel().HasAnyBookings);
        Assert.False(new CustomerBookingsPageViewModel { Tabs = new[] { new CustomerBookingTabViewModel { Key = "all", Count = 0 } } }.HasAnyBookings);
        Assert.True(new CustomerBookingsPageViewModel { Tabs = new[] { new CustomerBookingTabViewModel { Key = "all", Count = 2 } } }.HasAnyBookings);
    }
}

public class CustomerBookingsServiceTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime NowUtc = new(2026, 10, 14, 2, 0, 0);
    private static readonly DateOnly Day = new(2026, 10, 20);

    private static (CustomerBookingsService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new CustomerBookingsService(db, new FixedClock()), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string account = AccountStatuses.Active,
        string first = "Marco", string last = "Reyes", string approval = ApprovalStatuses.Approved)
    {
        var p = new Provider { BusinessName = $"{first}'s Works", ApprovalStatus = approval, User = NewUser(first, last, Roles.ServiceProvider, account) };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<Customer> AddCustomerAsync(ServisyoDbContext db, string first = "Ana", string last = "Reyes",
        string status = AccountStatuses.Active)
    {
        var c = new Customer { User = NewUser(first, last, Roles.Customer, status) };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static async Task<Booking> AddBookingAsync(ServisyoDbContext db, Customer c, Provider p, string status,
        DateOnly? date = null, TimeOnly? time = null, DateTime? createdUtc = null,
        string? location = "45 Mabini St, Quezon City", string? notes = "Kitchen sink pipe is leaking.",
        (string Service, string Category)[]? services = null)
    {
        var b = new Booking
        {
            CustomerId = c.CustomerId, ProviderId = p.ProviderId,
            BookingDate = date ?? Day, BookingTime = time ?? new TimeOnly(9, 0), TotalDurationMinutes = 60,
            TotalFee = 600m, Status = status, ServiceLocation = location, ServiceDescription = notes,
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

    private static async Task AddReviewAsync(ServisyoDbContext db, Booking b, string status = ReviewStatuses.Visible)
    {
        db.Reviews.Add(new Review { BookingId = b.BookingId, Rating = 4, Comment = "Good", Status = status });
        await db.SaveChangesAsync();
    }

    private static async Task<CustomerBookingsPageViewModel> OpenAsync(CustomerBookingsService svc, Customer c, string? tab = null, int page = 1)
    {
        var result = await svc.GetAsync(c.UserId, tab, page);
        Assert.True(result.Allowed);
        return result.Page!;
    }

    private static async Task<string> StatusOf(ServisyoDbContext db, int bookingId)
        => (await db.Bookings.AsNoTracking().FirstAsync(b => b.BookingId == bookingId)).Status;

    // ------------------------------------------------------------------ access

    [Fact]
    public async Task OnlyAnActiveCustomer_CanOpenThePage()
    {
        var (svc, db) = Create();
        var active = await AddCustomerAsync(db);
        var suspended = await AddCustomerAsync(db, status: AccountStatuses.Suspended);
        var deactivated = await AddCustomerAsync(db, status: AccountStatuses.Deactivated);
        var provider = await AddProviderAsync(db);

        Assert.True((await svc.GetAsync(active.UserId, null, 1)).Allowed);
        Assert.False((await svc.GetAsync(suspended.UserId, null, 1)).Allowed);
        Assert.False((await svc.GetAsync(deactivated.UserId, null, 1)).Allowed);
        Assert.False((await svc.GetAsync(provider.UserId, null, 1)).Allowed);      // the page is for customers
        Assert.False((await svc.GetAsync(999_999, null, 1)).Allowed);
    }

    [Fact]
    public async Task Shell_ShowsTheCustomer_AndTheirUnreadCounts()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var other = await AddCustomerAsync(db, "Ben", "Lim");
        db.Notifications.AddRange(
            new Notification { UserId = ana.UserId, Message = "a" },
            new Notification { UserId = ana.UserId, Message = "b", IsRead = true },
            new Notification { UserId = other.UserId, Message = "c" });
        db.Messages.Add(new Message { SenderId = other.UserId, ReceiverId = ana.UserId, MessageText = "hi" });
        await db.SaveChangesAsync();

        var page = (await OpenAsync(svc, ana)).Shell;

        Assert.Equal("Customer", page.Role);
        Assert.Equal("Ana", page.FirstName);
        Assert.Equal("Ana Reyes", page.FullName);
        Assert.Equal("AR", page.Initials);
        Assert.Equal("Customer Account", page.Subtitle);
        Assert.Equal(1, page.UnreadNotifications);
        Assert.Equal(1, page.UnreadMessages);
    }

    // ------------------------------------------------------------------ ownership

    [Fact]
    public async Task OnlyTheCustomersOwnBookings_AreShown()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var ben = await AddCustomerAsync(db, "Ben", "Lim");
        var provider = await AddProviderAsync(db);
        var mine = await AddBookingAsync(db, ana, provider, BookingStatuses.Pending);
        await AddBookingAsync(db, ben, provider, BookingStatuses.Pending, time: new TimeOnly(11, 0));
        await AddBookingAsync(db, ben, provider, BookingStatuses.Completed, time: new TimeOnly(13, 0));

        var page = await OpenAsync(svc, ana);

        Assert.Equal(mine.BookingId, Assert.Single(page.Cards).BookingId);
        Assert.Equal(1, page.Tabs.Single(t => t.Key == "all").Count);          // the counts never include other customers either
    }

    [Fact]
    public async Task ACustomerWithNoBookings_SeesAnEmptyPage()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);

        var page = await OpenAsync(svc, ana);

        Assert.Empty(page.Cards);
        Assert.False(page.HasAnyBookings);
        Assert.Equal("all", Assert.Single(page.Tabs).Key);
        Assert.Equal(0, page.Tabs[0].Count);
        Assert.Equal(1, page.PageCount);
    }

    [Fact]
    public async Task HistoryStaysVisible_EvenWhenTheProviderIsSuspendedLater()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var suspended = await AddProviderAsync(db, account: AccountStatuses.Suspended, first: "Rhea", last: "Villanueva");
        await AddBookingAsync(db, ana, suspended, BookingStatuses.Completed);

        var card = Assert.Single((await OpenAsync(svc, ana)).Cards);

        Assert.Equal("Rhea Villanueva", card.ProviderName);
    }

    // ------------------------------------------------------------------ tabs

    [Fact]
    public async Task Tabs_ShowAllPlusTheStatusesThatExist_WithCounts()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, provider, BookingStatuses.Accepted, time: new TimeOnly(9, 0));
        await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(10, 0));
        await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(11, 0));

        var tabs = (await OpenAsync(svc, ana)).Tabs;

        Assert.Equal(new[] { "all", "accepted", "completed" }, tabs.Select(t => t.Key).ToArray());     // lifecycle order, no empty tabs
        Assert.Equal(new[] { 3, 1, 2 }, tabs.Select(t => t.Count).ToArray());
        Assert.Equal(new[] { "All", "Accepted", "Completed" }, tabs.Select(t => t.Label).ToArray());
    }

    [Fact]
    public async Task Tabs_KeepTheOpenTab_EvenWhenItIsEmpty()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, provider, BookingStatuses.Accepted);

        var page = await OpenAsync(svc, ana, "pending");

        Assert.Equal("pending", page.ActiveTab);
        Assert.Contains(page.Tabs, t => t.Key == "pending" && t.Count == 0);
        Assert.Empty(page.Cards);
        Assert.True(page.HasAnyBookings);
    }

    [Fact]
    public async Task ChoosingATab_ShowsOnlyThatStatus()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var accepted = await AddBookingAsync(db, ana, provider, BookingStatuses.Accepted, time: new TimeOnly(9, 0));
        await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(10, 0));
        await AddBookingAsync(db, ana, provider, BookingStatuses.Cancelled, time: new TimeOnly(11, 0));

        var page = await OpenAsync(svc, ana, "accepted");

        Assert.Equal(accepted.BookingId, Assert.Single(page.Cards).BookingId);
        Assert.Equal(1, page.TotalMatching);
    }

    [Fact]
    public async Task UnknownTab_ShowsEverything()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, provider, BookingStatuses.Accepted, time: new TimeOnly(9, 0));
        await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(10, 0));

        var page = await OpenAsync(svc, ana, "'; DROP TABLE BOOKINGS;--");

        Assert.Equal("all", page.ActiveTab);
        Assert.Equal(2, page.Cards.Count);
    }

    // ------------------------------------------------------------------ the cards

    [Fact]
    public async Task Cards_AreNewestRequestFirst()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var oldest = await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(9, 0), createdUtc: NowUtc.AddDays(-10));
        var newest = await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(10, 0), createdUtc: NowUtc.AddDays(-1));
        var middle = await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(11, 0), createdUtc: NowUtc.AddDays(-5));

        var page = await OpenAsync(svc, ana);

        Assert.Equal(new[] { newest.BookingId, middle.BookingId, oldest.BookingId }, page.Cards.Select(c => c.BookingId).ToArray());
    }

    [Fact]
    public async Task Card_ShowsTheDetailsFromTheDesign()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        // 18:00 UTC on 15 Sep is 02:00 on 16 Sep in the Philippines
        await AddBookingAsync(db, ana, provider, BookingStatuses.Accepted,
            date: new DateOnly(2026, 9, 20), time: new TimeOnly(9, 0),
            createdUtc: new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc),
            location: "45 Mabini St, Quezon City", notes: "Kitchen sink pipe is leaking underneath the cabinet.");

        var card = Assert.Single((await OpenAsync(svc, ana)).Cards);

        Assert.Equal("Pipe Repair", card.Title);
        Assert.Equal("Marco Reyes", card.ProviderName);
        Assert.Equal("Plumbing", card.CategoryText);
        Assert.Equal("Marco Reyes · Plumbing", card.FromLine);
        Assert.Equal("Accepted", card.Status);
        Assert.Equal("accepted", card.Tone);
        Assert.Equal("Sep 20, 2026 · 9:00 AM", card.WhenText);
        Assert.Equal("45 Mabini St, Quezon City", card.Location);
        Assert.Equal("Sep 16, 2026", card.SubmittedText);
        Assert.Equal("Kitchen sink pipe is leaking underneath the cabinet.", card.Notes);
    }

    [Fact]
    public async Task Card_WithSeveralServices_JoinsTheTitleAndCategories()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, provider, BookingStatuses.Pending,
            services: new[] { ("Pipe Repair", "Plumbing"), ("Waterproofing", "Plumbing"), ("Deep Cleaning", "Cleaning") });

        var card = Assert.Single((await OpenAsync(svc, ana)).Cards);

        Assert.Equal("Pipe Repair & Waterproofing & Deep Cleaning", card.Title);
        Assert.Equal("Plumbing, Cleaning", card.CategoryText);          // each category once
    }

    [Fact]
    public async Task Card_WithBlankLocationAndNotes_HidesThem()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, provider, BookingStatuses.Pending, location: "   ", notes: null);

        var card = Assert.Single((await OpenAsync(svc, ana)).Cards);

        Assert.Null(card.Location);
        Assert.Null(card.Notes);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending, "pending")]
    [InlineData(BookingStatuses.Accepted, "accepted")]
    [InlineData(BookingStatuses.InProgress, "progress")]
    [InlineData(BookingStatuses.Completed, "completed")]
    [InlineData(BookingStatuses.Rejected, "danger")]
    [InlineData(BookingStatuses.Cancelled, "muted")]
    public async Task Card_BadgeColour_FollowsTheStatus(string status, string tone)
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, provider, status);

        var card = Assert.Single((await OpenAsync(svc, ana)).Cards);

        Assert.Equal(status, card.Status);
        Assert.Equal(tone, card.Tone);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending, true)]
    [InlineData(BookingStatuses.Accepted, false)]
    [InlineData(BookingStatuses.InProgress, false)]
    [InlineData(BookingStatuses.Completed, false)]
    [InlineData(BookingStatuses.Rejected, false)]
    [InlineData(BookingStatuses.Cancelled, false)]
    public async Task OnlyAPendingBooking_OffersCancel(string status, bool canCancel)
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        await AddBookingAsync(db, ana, provider, status);

        Assert.Equal(canCancel, Assert.Single((await OpenAsync(svc, ana)).Cards).CanCancel);
    }

    [Fact]
    public async Task LeaveAReview_IsOfferedOnlyForACompletedBookingWithoutOne()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var plain = await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(9, 0));
        var reviewed = await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(10, 0));
        var removed = await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(11, 0));
        var accepted = await AddBookingAsync(db, ana, provider, BookingStatuses.Accepted, time: new TimeOnly(12, 0));
        await AddReviewAsync(db, reviewed);
        await AddReviewAsync(db, removed, ReviewStatuses.Removed);      // a moderated review still counts: one review per booking

        var cards = (await OpenAsync(svc, ana)).Cards;

        var c1 = cards.Single(c => c.BookingId == plain.BookingId);
        Assert.True(c1.CanReview);
        Assert.False(c1.IsReviewed);

        var c2 = cards.Single(c => c.BookingId == reviewed.BookingId);
        Assert.False(c2.CanReview);
        Assert.True(c2.IsReviewed);

        var c3 = cards.Single(c => c.BookingId == removed.BookingId);
        Assert.False(c3.CanReview);
        Assert.True(c3.IsReviewed);

        var c4 = cards.Single(c => c.BookingId == accepted.BookingId);
        Assert.False(c4.CanReview);
        Assert.False(c4.IsReviewed);
    }

    // ------------------------------------------------------------------ paging

    [Fact]
    public async Task Paging_Shows10PerPage_WithNoRepeats()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        for (var i = 0; i < 11; i++)
            await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(6 + i, 0), createdUtc: NowUtc.AddDays(-i - 1));

        var first = await OpenAsync(svc, ana, page: 1);
        var second = await OpenAsync(svc, ana, page: 2);

        Assert.Equal(10, first.Cards.Count);
        Assert.Single(second.Cards);
        Assert.Equal(11, first.TotalMatching);
        Assert.Equal(2, first.PageCount);
        Assert.Equal(11, first.Cards.Concat(second.Cards).Select(c => c.BookingId).Distinct().Count());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(99, 2)]
    public async Task Paging_OutOfRangePageNumbers_AreCorrected(int asked, int expected)
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        for (var i = 0; i < 11; i++)
            await AddBookingAsync(db, ana, provider, BookingStatuses.Completed, time: new TimeOnly(6 + i, 0));

        var page = await OpenAsync(svc, ana, page: asked);

        Assert.Equal(expected, page.Page);
        Assert.NotEmpty(page.Cards);
    }

    // ------------------------------------------------------------------ cancelling

    [Fact]
    public async Task Cancel_APendingBooking_Works_AndTheProviderIsTold()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, ana, provider, BookingStatuses.Pending);

        var result = await svc.CancelAsync(ana.UserId, booking.BookingId);

        Assert.True(result.Allowed);
        Assert.True(result.Succeeded);
        Assert.Equal($"Booking #{booking.BookingId} has been cancelled. The provider has been notified.", result.Message);

        var saved = await db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking.BookingId);
        Assert.Equal(BookingStatuses.Cancelled, saved.Status);
        Assert.Equal(NowUtc, saved.UpdatedAt);

        var note = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(provider.UserId, note.UserId);                               // the PROVIDER's account, not the customer's
        Assert.Equal($"Booking #{booking.BookingId} was cancelled by the customer.", note.Message);
        Assert.False(note.IsRead);
    }

    [Fact]
    public async Task Cancel_MovesTheBookingToTheCancelledTab()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, ana, provider, BookingStatuses.Pending);

        await svc.CancelAsync(ana.UserId, booking.BookingId);
        var page = await OpenAsync(svc, ana);

        Assert.Equal(new[] { "all", "cancelled" }, page.Tabs.Select(t => t.Key).ToArray());      // "Pending" has no bookings left
        var card = Assert.Single(page.Cards);
        Assert.Equal("Cancelled", card.Status);
        Assert.False(card.CanCancel);
    }

    [Theory]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Rejected)]
    [InlineData(BookingStatuses.Cancelled)]
    public async Task Cancel_AnythingButPending_IsRefused_AndNothingChanges(string status)
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, ana, provider, status);

        var result = await svc.CancelAsync(ana.UserId, booking.BookingId);

        Assert.True(result.Allowed);
        Assert.Equal(CustomerCancelOutcome.NotPending, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(status, await StatusOf(db, booking.BookingId));
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Cancel_ExplainsWhyItWasRefused()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var accepted = await AddBookingAsync(db, ana, provider, BookingStatuses.Accepted, time: new TimeOnly(9, 0));
        var cancelled = await AddBookingAsync(db, ana, provider, BookingStatuses.Cancelled, time: new TimeOnly(10, 0));

        Assert.Equal($"Booking #{accepted.BookingId} is accepted, so it can no longer be cancelled here. Only pending requests can be cancelled.",
            (await svc.CancelAsync(ana.UserId, accepted.BookingId)).Message);
        Assert.Equal($"Booking #{cancelled.BookingId} is already cancelled.",
            (await svc.CancelAsync(ana.UserId, cancelled.BookingId)).Message);
    }

    [Fact]
    public async Task Cancel_Twice_SecondTimeIsRefused_AndOnlyOneNotificationIsSent()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, ana, provider, BookingStatuses.Pending);

        var first = await svc.CancelAsync(ana.UserId, booking.BookingId);
        var second = await svc.CancelAsync(ana.UserId, booking.BookingId);        // a double click / a refreshed form

        Assert.True(first.Succeeded);
        Assert.Equal(CustomerCancelOutcome.NotPending, second.Outcome);
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task Cancel_SomeoneElsesBooking_LooksLikeAMissingOne_AndNothingChanges()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var ben = await AddCustomerAsync(db, "Ben", "Lim");
        var provider = await AddProviderAsync(db);
        var bensBooking = await AddBookingAsync(db, ben, provider, BookingStatuses.Pending);

        var result = await svc.CancelAsync(ana.UserId, bensBooking.BookingId);       // Ana guesses Ben's booking number

        Assert.Equal(CustomerCancelOutcome.NotFound, result.Outcome);
        Assert.Equal("That booking could not be found.", result.Message);
        Assert.Equal(BookingStatuses.Pending, await StatusOf(db, bensBooking.BookingId));
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Cancel_ABookingThatDoesNotExist_IsNotFound()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);

        var result = await svc.CancelAsync(ana.UserId, 424242);

        Assert.Equal(CustomerCancelOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Cancel_OnlyTouchesTheChosenBooking()
    {
        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var first = await AddBookingAsync(db, ana, provider, BookingStatuses.Pending, time: new TimeOnly(9, 0));
        var second = await AddBookingAsync(db, ana, provider, BookingStatuses.Pending, time: new TimeOnly(11, 0));

        await svc.CancelAsync(ana.UserId, first.BookingId);

        Assert.Equal(BookingStatuses.Cancelled, await StatusOf(db, first.BookingId));
        Assert.Equal(BookingStatuses.Pending, await StatusOf(db, second.BookingId));
    }

    [Fact]
    public async Task Cancel_NotAnActiveCustomer_IsNotAllowed_AndNothingChanges()
    {
        var (svc, db) = Create();
        var suspended = await AddCustomerAsync(db, status: AccountStatuses.Suspended);
        var deactivated = await AddCustomerAsync(db, "Dee", "Go", AccountStatuses.Deactivated);
        var provider = await AddProviderAsync(db);
        var a = await AddBookingAsync(db, suspended, provider, BookingStatuses.Pending, time: new TimeOnly(9, 0));
        var b = await AddBookingAsync(db, deactivated, provider, BookingStatuses.Pending, time: new TimeOnly(11, 0));

        Assert.False((await svc.CancelAsync(suspended.UserId, a.BookingId)).Allowed);
        Assert.False((await svc.CancelAsync(deactivated.UserId, b.BookingId)).Allowed);
        Assert.False((await svc.CancelAsync(provider.UserId, a.BookingId)).Allowed);        // a provider cannot cancel through this page
        Assert.False((await svc.CancelAsync(999_999, a.BookingId)).Allowed);

        Assert.Equal(BookingStatuses.Pending, await StatusOf(db, a.BookingId));
        Assert.Equal(BookingStatuses.Pending, await StatusOf(db, b.BookingId));
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Cancel_FreesTheSlot_SoTheProviderCanBeBookedAgainLater()
    {
        // Cancelled is not an "active" status, so the one-active-booking-per-slot rule no longer counts this booking.
        Assert.DoesNotContain(BookingStatuses.Cancelled, new[] { BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress });

        var (svc, db) = Create();
        var ana = await AddCustomerAsync(db);
        var provider = await AddProviderAsync(db);
        var booking = await AddBookingAsync(db, ana, provider, BookingStatuses.Pending);

        await svc.CancelAsync(ana.UserId, booking.BookingId);

        Assert.Equal(0, await db.Bookings.CountAsync(b => b.ProviderId == provider.ProviderId
            && (b.Status == BookingStatuses.Pending || b.Status == BookingStatuses.Accepted || b.Status == BookingStatuses.InProgress)));
    }
}
