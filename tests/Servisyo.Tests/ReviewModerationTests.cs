using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ReviewFiltersTests
{
    [Theory]
    [InlineData("Flagged", "Flagged")]
    [InlineData("flagged", "Flagged")]
    [InlineData("Hidden", "Hidden")]
    [InlineData("removed", "Hidden")]
    [InlineData("All", "All")]
    [InlineData("nonsense", "All")]
    [InlineData("", "All")]
    [InlineData(null, "All")]
    public void Normalize_AcceptsKnownTabs_AndFallsBackToAll(string? input, string expected)
        => Assert.Equal(expected, ReviewFilters.Normalize(input));
}

public class ReviewCardViewModelTests
{
    [Theory]
    [InlineData(ReviewStatuses.Visible, "Visible", "completed")]
    [InlineData(ReviewStatuses.Flagged, "Flagged", "pending")]
    [InlineData(ReviewStatuses.Removed, "Hidden", "muted")]
    public void Status_IsShownWithItsBadge(string status, string text, string tone)
    {
        var card = new ReviewCardViewModel { Status = status };
        Assert.Equal(text, card.StatusText);
        Assert.Equal(tone, card.StatusTone);
    }

    [Theory]
    [InlineData(5, "5.0")]
    [InlineData(4, "4.0")]
    [InlineData(1, "1.0")]
    public void RatingText_HasOneDecimal(int rating, string text)
        => Assert.Equal(text, new ReviewCardViewModel { Rating = rating }.RatingText);

    [Theory]
    [InlineData(ReviewStatuses.Visible, new[] { "Hide" })]
    [InlineData(ReviewStatuses.Flagged, new[] { "Keep", "Hide" })]
    [InlineData(ReviewStatuses.Removed, new[] { "Restore" })]
    public void Buttons_DependOnTheStatus(string status, string[] expected)
        => Assert.Equal(expected, new ReviewCardViewModel { Status = status }.Actions.Select(a => a.Action));
}

public class ReviewModerationServiceTests
{
    private static (ReviewModerationService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new ReviewModerationService(db), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator, status);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    private static Customer NewCustomer(string first, string last) => new() { User = NewUser(first, last, Roles.Customer) };

    private static Provider NewProvider(string first, string last)
        => new() { BusinessName = first + "'s", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser(first, last, Roles.ServiceProvider) };

    /// A Completed booking with one review.
    private static Review NewReview(Customer c, Provider p, string serviceName, int rating, string? comment,
        string status = ReviewStatuses.Visible, DateTime? createdUtc = null, params string[] extraServices)
    {
        var cat = new ServiceCategory { CategoryName = "Cat " + Guid.NewGuid().ToString("N")[..6] };
        var booking = new Booking
        {
            Customer = c, Provider = p, BookingDate = new DateOnly(2026, 9, 10), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 500m, Status = BookingStatuses.Completed, RowVersion = new byte[] { 1 }
        };
        foreach (var name in new[] { serviceName }.Concat(extraServices))
        {
            var service = new Service { Provider = p, Category = cat, ServiceName = name, Price = 500m };
            booking.Services.Add(new BookingService { Service = service, PriceSnapshot = 500m, DurationMinutesSnapshot = 60 });
        }
        return new Review
        {
            Booking = booking, Rating = (byte)rating, Comment = comment, Status = status,
            CreatedAt = createdUtc ?? DateTime.UtcNow
        };
    }

    /// The three reviews of the design: two Visible, one Flagged.
    private static async Task SeedDesignAsync(ServisyoDbContext db)
    {
        var elena = NewCustomer("Elena", "Cruz");
        var carla = NewCustomer("Carla", "Tan");
        var rhea = NewProvider("Rhea", "Villanueva");
        var marco = NewProvider("Marco", "Reyes");
        var danilo = NewProvider("Danilo", "Cruz");
        db.Reviews.AddRange(
            NewReview(elena, rhea, "Laundry", 5, "Rhea is amazing! Everything came back perfectly clean and folded.", createdUtc: new DateTime(2026, 9, 11, 4, 0, 0, DateTimeKind.Utc)),
            NewReview(elena, marco, "Drain Unclogging", 4, "Very professional and fast.", createdUtc: new DateTime(2026, 9, 13, 4, 0, 0, DateTimeKind.Utc)),
            NewReview(carla, danilo, "Appliance Repair", 2, "Minor issue with cleanup afterward.", ReviewStatuses.Flagged, new DateTime(2026, 9, 6, 4, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- list

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanSeeTheList()
    {
        var (svc, db) = Create();
        var customer = NewCustomer("Ana", "Reyes");
        var provider = NewProvider("Marco", "Reyes");
        db.Customers.Add(customer);
        db.Providers.Add(provider);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        Assert.Null(await svc.GetReviewsAsync(customer.UserId, new ReviewListQuery()));
        Assert.Null(await svc.GetReviewsAsync(provider.UserId, new ReviewListQuery()));
        Assert.Null(await svc.GetReviewsAsync(suspendedAdmin.UserId, new ReviewListQuery()));
        Assert.Null(await svc.GetReviewsAsync(99999, new ReviewListQuery()));
    }

    [Fact]
    public async Task NoReviews_ShowsAnEmptyList()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var vm = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery()))!;

        Assert.Empty(vm.Cards);
        Assert.Equal(new[] { "All:0", "Flagged:0", "Hidden:0" }, vm.Tabs.Select(t => $"{t.Label}:{t.Count}"));
    }

    [Fact]
    public async Task Tabs_CountAllFlaggedAndHidden()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);
        db.Reviews.Add(NewReview(NewCustomer("Ben", "Lim"), NewProvider("Liza", "Santos"), "Deep Cleaning", 1, "Rude.", ReviewStatuses.Removed));
        await db.SaveChangesAsync();

        var vm = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery()))!;

        Assert.Equal(new[] { "All:4", "Flagged:1", "Hidden:1" }, vm.Tabs.Select(t => $"{t.Label}:{t.Count}"));
        Assert.Equal(4, vm.Cards.Count);
    }

    [Theory]
    [InlineData("All", 3)]
    [InlineData("Flagged", 1)]
    [InlineData("Hidden", 0)]
    [InlineData("nonsense", 3)]
    public async Task Tabs_FilterTheCards_AndTheNumbersStayTheSame(string tab, int expected)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery { Status = tab }))!;

        Assert.Equal(expected, vm.Cards.Count);
        Assert.Equal(3, vm.CountAll);
    }

    [Fact]
    public async Task HiddenTab_ShowsRemovedReviews()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.Reviews.Add(NewReview(NewCustomer("Ben", "Lim"), NewProvider("Liza", "Santos"), "Deep Cleaning", 1, "Rude.", ReviewStatuses.Removed));
        await db.SaveChangesAsync();

        var vm = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery { Status = "Hidden" }))!;

        var card = Assert.Single(vm.Cards);
        Assert.Equal("Hidden", card.StatusText);
        Assert.Equal(new[] { "Restore" }, card.Actions.Select(a => a.Action));
    }

    [Fact]
    public async Task Cards_ShowNamesServiceRatingCommentAndCebuDate()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery()))!;

        var flagged = vm.Cards.Single(c => c.Status == ReviewStatuses.Flagged);
        Assert.Equal("Carla Tan", flagged.CustomerName);
        Assert.Equal("Danilo Cruz", flagged.ProviderName);
        Assert.Equal("Appliance Repair", flagged.Service);
        Assert.Equal(2, flagged.Rating);
        Assert.Equal("Minor issue with cleanup afterward.", flagged.Comment);
        Assert.Equal(new DateOnly(2026, 9, 6), flagged.SubmittedOn);
        Assert.Equal(new[] { "Keep", "Hide" }, flagged.Actions.Select(a => a.Action));
    }

    [Fact]
    public async Task SubmittedDate_IsShownInCebuTime_NotUtc()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        // 20:30 UTC on Sep 10 is already 04:30 on Sep 11 in Cebu (UTC+8)
        db.Reviews.Add(NewReview(NewCustomer("Ana", "Reyes"), NewProvider("Marco", "Reyes"), "Pipe Repair", 5, "Great",
            createdUtc: new DateTime(2026, 9, 10, 20, 30, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var vm = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery()))!;

        Assert.Equal(new DateOnly(2026, 9, 11), Assert.Single(vm.Cards).SubmittedOn);
    }

    [Fact]
    public async Task Cards_AreNewestFirst()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery()))!;

        Assert.Equal(new[] { "Drain Unclogging", "Laundry", "Appliance Repair" }, vm.Cards.Select(c => c.Service));
    }

    [Fact]
    public async Task ReviewWithoutComment_AndBookingWithSeveralServices_AreShownSensibly()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.Reviews.Add(NewReview(NewCustomer("Ana", "Reyes"), NewProvider("Marco", "Reyes"), "Pipe Repair", 3, null,
            extraServices: new[] { "Waterproofing" }));
        await db.SaveChangesAsync();

        var card = Assert.Single((await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery()))!.Cards);

        Assert.Null(card.Comment);
        Assert.Equal("Pipe Repair & Waterproofing", card.Service);
    }

    [Fact]
    public async Task Paging_ShowsTwentyPerPage_AndClampsThePageNumber()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var customer = NewCustomer("Ana", "Reyes");
        var provider = NewProvider("Marco", "Reyes");
        for (var i = 0; i < 45; i++)
            db.Reviews.Add(NewReview(customer, provider, $"Job {i:00}", 4, "ok", createdUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i)));
        await db.SaveChangesAsync();

        var first = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery { Page = 1 }))!;
        var last = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery { Page = 3 }))!;
        var tooFar = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery { Page = 99 }))!;
        var negative = (await svc.GetReviewsAsync(admin.UserId, new ReviewListQuery { Page = -1 }))!;

        Assert.Equal(20, first.Cards.Count);
        Assert.Equal(5, last.Cards.Count);
        Assert.Equal(3, first.PageCount);
        Assert.Equal(45, first.TotalMatching);
        Assert.Equal(3, tooFar.Page);
        Assert.Equal(1, negative.Page);
    }

    // ------------------------------------------------------------ moderate

    [Fact]
    public async Task Hide_RemovesTheReview_AndLogsIt()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var review = NewReview(NewCustomer("Ana", "Reyes"), NewProvider("Marco", "Reyes"), "Pipe Repair", 1, "Rude");
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        var result = await svc.ModerateAsync(admin.UserId, review.ReviewId, ReviewAction.Hide, "  Abusive language  ");

        Assert.True(result.Succeeded);
        Assert.Equal(ReviewStatuses.Removed, (await db.Reviews.SingleAsync()).Status);
        var log = await db.AdminActions.SingleAsync();
        Assert.Equal("Review Removed", log.ActionType);
        Assert.Equal("REVIEW", log.TargetType);
        Assert.Equal(review.ReviewId, log.TargetId);
        Assert.Equal(admin.UserId, log.AdminUserId);
        Assert.Equal("Abusive language", log.Reason);
    }

    [Fact]
    public async Task Keep_UnflagsAFlaggedReview()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var review = NewReview(NewCustomer("Ana", "Reyes"), NewProvider("Marco", "Reyes"), "Pipe Repair", 2, "Meh", ReviewStatuses.Flagged);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        var result = await svc.ModerateAsync(admin.UserId, review.ReviewId, ReviewAction.Keep, null);

        Assert.True(result.Succeeded);
        Assert.Equal(ReviewStatuses.Visible, (await db.Reviews.SingleAsync()).Status);
        var log = await db.AdminActions.SingleAsync();
        Assert.Equal("Review Visible", log.ActionType);
        Assert.Null(log.Reason);
    }

    [Fact]
    public async Task Restore_BringsBackAHiddenReview()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var review = NewReview(NewCustomer("Ana", "Reyes"), NewProvider("Marco", "Reyes"), "Pipe Repair", 5, "Great", ReviewStatuses.Removed);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        Assert.True((await svc.ModerateAsync(admin.UserId, review.ReviewId, ReviewAction.Restore, null)).Succeeded);
        Assert.Equal(ReviewStatuses.Visible, (await db.Reviews.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(ReviewStatuses.Visible, ReviewAction.Keep)]       // only a flagged review can be "kept"
    [InlineData(ReviewStatuses.Visible, ReviewAction.Restore)]
    [InlineData(ReviewStatuses.Flagged, ReviewAction.Restore)]
    [InlineData(ReviewStatuses.Removed, ReviewAction.Keep)]
    [InlineData(ReviewStatuses.Removed, ReviewAction.Hide)]       // already hidden
    public async Task WrongMoves_AreRefused_ChangeNothing_AndWriteNoLog(string current, ReviewAction action)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var provider = NewProvider("Marco", "Reyes");
        provider.AvgRating = 3.5m; provider.ReviewCount = 7;
        var review = NewReview(NewCustomer("Ana", "Reyes"), provider, "Pipe Repair", 5, "x", current);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        var result = await svc.ModerateAsync(admin.UserId, review.ReviewId, action, null);

        Assert.Equal(ReviewOutcome.NoChange, result.Outcome);
        Assert.Equal(current, (await db.Reviews.SingleAsync()).Status);
        Assert.Empty(db.AdminActions);
        var p = await db.Providers.SingleAsync();
        Assert.Equal(3.5m, p.AvgRating);       // rating untouched
        Assert.Equal(7, p.ReviewCount);
    }

    [Fact]
    public async Task Moderate_UnknownReview_IsNotFound()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        Assert.Equal(ReviewOutcome.NotFound, (await svc.ModerateAsync(admin.UserId, 424242, ReviewAction.Hide, null)).Outcome);
    }

    [Fact]
    public async Task Moderate_RejectsAReasonOverTheLimit_ButAcceptsTheLimit()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var review = NewReview(NewCustomer("Ana", "Reyes"), NewProvider("Marco", "Reyes"), "Pipe Repair", 1, "Rude");
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        var tooLong = await svc.ModerateAsync(admin.UserId, review.ReviewId, ReviewAction.Hide, new string('x', 256));
        Assert.Equal(ReviewOutcome.Invalid, tooLong.Outcome);
        Assert.Equal(ReviewStatuses.Visible, (await db.Reviews.SingleAsync()).Status);

        var atLimit = await svc.ModerateAsync(admin.UserId, review.ReviewId, ReviewAction.Hide, new string('x', 255));
        Assert.True(atLimit.Succeeded);
    }

    [Fact]
    public async Task Moderate_IsRefusedForEveryoneButAnActiveAdministrator()
    {
        var (svc, db) = Create();
        var customer = NewCustomer("Ana", "Reyes");
        var provider = NewProvider("Marco", "Reyes");
        var review = NewReview(customer, provider, "Pipe Repair", 1, "Rude");
        db.Reviews.Add(review);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        // a provider must never be able to hide a review of their own work
        foreach (var userId in new[] { customer.User.UserId, provider.User.UserId, suspendedAdmin.UserId, 99999 })
            Assert.Equal(ReviewOutcome.NotAllowed, (await svc.ModerateAsync(userId, review.ReviewId, ReviewAction.Hide, null)).Outcome);

        Assert.Equal(ReviewStatuses.Visible, (await db.Reviews.SingleAsync()).Status);
        Assert.Empty(db.AdminActions);
    }

    // ------------------------------------------------ provider rating (PDF rule)

    [Fact]
    public async Task HidingAReview_RecalculatesTheProviderRating_FromVisibleReviewsOnly()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var marco = NewProvider("Marco", "Reyes");
        var ana = NewCustomer("Ana", "Reyes");
        var five = NewReview(ana, marco, "A", 5, "great");
        var four = NewReview(ana, marco, "B", 4, "good");
        var one = NewReview(ana, marco, "C", 1, "awful");
        var alreadyHidden = NewReview(ana, marco, "D", 1, "old", ReviewStatuses.Removed);   // never counts
        db.Reviews.AddRange(five, four, one, alreadyHidden);
        marco.AvgRating = 3.3m; marco.ReviewCount = 3;
        await db.SaveChangesAsync();

        await svc.ModerateAsync(admin.UserId, one.ReviewId, ReviewAction.Hide, null);

        var p = await db.Providers.SingleAsync();
        Assert.Equal(2, p.ReviewCount);
        Assert.Equal(4.5m, p.AvgRating);
    }

    [Fact]
    public async Task RestoringAReview_AddsItBackIntoTheRating()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var marco = NewProvider("Marco", "Reyes");
        var ana = NewCustomer("Ana", "Reyes");
        var five = NewReview(ana, marco, "A", 5, "great");
        var one = NewReview(ana, marco, "B", 1, "awful", ReviewStatuses.Removed);
        db.Reviews.AddRange(five, one);
        await db.SaveChangesAsync();

        await svc.ModerateAsync(admin.UserId, one.ReviewId, ReviewAction.Restore, null);

        var p = await db.Providers.SingleAsync();
        Assert.Equal(2, p.ReviewCount);
        Assert.Equal(3.0m, p.AvgRating);
    }

    [Fact]
    public async Task KeepingAFlaggedReview_MakesItCount()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var marco = NewProvider("Marco", "Reyes");
        var ana = NewCustomer("Ana", "Reyes");
        var visible = NewReview(ana, marco, "A", 5, "great");
        var flagged = NewReview(ana, marco, "B", 2, "meh", ReviewStatuses.Flagged);
        db.Reviews.AddRange(visible, flagged);
        await db.SaveChangesAsync();

        await svc.ModerateAsync(admin.UserId, flagged.ReviewId, ReviewAction.Keep, null);

        var p = await db.Providers.SingleAsync();
        Assert.Equal(2, p.ReviewCount);
        Assert.Equal(3.5m, p.AvgRating);
    }

    [Fact]
    public async Task HidingTheLastVisibleReview_LeavesNoRating()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var marco = NewProvider("Marco", "Reyes");
        marco.AvgRating = 5.0m; marco.ReviewCount = 1;
        var only = NewReview(NewCustomer("Ana", "Reyes"), marco, "A", 5, "great");
        db.Reviews.Add(only);
        await db.SaveChangesAsync();

        await svc.ModerateAsync(admin.UserId, only.ReviewId, ReviewAction.Hide, null);

        var p = await db.Providers.SingleAsync();
        Assert.Equal(0, p.ReviewCount);
        Assert.Null(p.AvgRating);
    }

    [Fact]
    public async Task Rating_IsRoundedToOneDecimal_HalfAwayFromZero_AndOnlyTheRightProviderChanges()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var marco = NewProvider("Marco", "Reyes");
        var liza = NewProvider("Liza", "Santos");
        liza.AvgRating = 2.0m; liza.ReviewCount = 9;
        var ana = NewCustomer("Ana", "Reyes");
        // visible after the change: 5, 4, 4  -> 4.333.. -> 4.3 ; then flagged 5 is kept: 5,4,4,5 -> 4.5
        var a = NewReview(ana, marco, "A", 5, "x");
        var b = NewReview(ana, marco, "B", 4, "x");
        var c = NewReview(ana, marco, "C", 4, "x");
        var d = NewReview(ana, marco, "D", 5, "x", ReviewStatuses.Flagged);
        var other = NewReview(ana, liza, "E", 1, "x");
        db.Reviews.AddRange(a, b, c, d, other);
        await db.SaveChangesAsync();

        // hide a (5): visible are b(4) c(4) = 4.0
        await svc.ModerateAsync(admin.UserId, a.ReviewId, ReviewAction.Hide, null);
        Assert.Equal(4.0m, (await db.Providers.SingleAsync(p => p.ProviderId == marco.ProviderId)).AvgRating);

        // keep d (5): visible are b(4) c(4) d(5) = 4.333 -> 4.3
        await svc.ModerateAsync(admin.UserId, d.ReviewId, ReviewAction.Keep, null);
        Assert.Equal(4.3m, (await db.Providers.SingleAsync(p => p.ProviderId == marco.ProviderId)).AvgRating);

        // restore a (5): visible are 4,4,5,5 = 4.5
        await svc.ModerateAsync(admin.UserId, a.ReviewId, ReviewAction.Restore, null);
        Assert.Equal(4.5m, (await db.Providers.SingleAsync(p => p.ProviderId == marco.ProviderId)).AvgRating);

        var lizaAfter = await db.Providers.SingleAsync(p => p.ProviderId == liza.ProviderId);
        Assert.Equal(2.0m, lizaAfter.AvgRating);      // other providers are not touched
        Assert.Equal(9, lizaAfter.ReviewCount);
    }
}
