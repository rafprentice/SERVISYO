using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class CustomerReviewTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
    }

    private sealed class World
    {
        public ServisyoDbContext Db { get; } = new(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Customer Ana { get; set; } = null!;
        public Customer Ben { get; set; } = null!;
        public Provider Nina { get; set; } = null!;
        public Service Cleaning { get; set; } = null!;
        public CustomerReviewService Svc => new(Db, new FixedClock());
    }

    private static User NewUser(string first, string role, string status = AccountStatuses.Active) =>
        new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status };

    private static async Task<World> CreateAsync()
    {
        var w = new World();
        var cat = new ServiceCategory { CategoryName = "Cleaning" };
        w.Ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        w.Ben = new Customer { User = NewUser("Ben", Roles.Customer) };
        w.Nina = new Provider { BusinessName = "Nina Santiago", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Nina", Roles.ServiceProvider) };
        w.Db.AddRange(cat, w.Ana, w.Ben, w.Nina);
        await w.Db.SaveChangesAsync();
        w.Cleaning = new Service { ProviderId = w.Nina.ProviderId, CategoryId = cat.CategoryId, ServiceName = "Full House Deep Cleaning", Price = 1500m, DurationMinutes = 120 };
        w.Db.Services.Add(w.Cleaning);
        await w.Db.SaveChangesAsync();
        return w;
    }

    private static async Task<Booking> BookAsync(World w, Customer who, string status = BookingStatuses.Completed)
    {
        var b = new Booking
        {
            CustomerId = who.CustomerId, ProviderId = w.Nina.ProviderId, BookingDate = new DateOnly(2026, 10, 20),
            BookingTime = new TimeOnly(8, 30), TotalDurationMinutes = 120, TotalFee = 1500m,
            Status = status, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        };
        b.Services.Add(new BookingService { ServiceId = w.Cleaning.ServiceId, PriceSnapshot = 1500m, DurationMinutesSnapshot = 120 });
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        return b;
    }

    // ------------------------------------------------------------------ who may review

    [Fact]
    public async Task Page_ForMyCompletedBooking_ShowsTheBookingDetails()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana);

        var result = await w.Svc.GetPageAsync(w.Ana.UserId, b.BookingId);

        Assert.Equal(ReviewEligibility.Ok, result.Outcome);
        Assert.Equal("Full House Deep Cleaning", result.Page!.Title);
        Assert.Equal("Nina Santiago", result.Page.ProviderName);
        Assert.Equal($"BK-{b.CreatedAt.Year}-{b.BookingId:D4}", result.Page.Reference);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.InProgress)]
    [InlineData(BookingStatuses.Rejected)]
    [InlineData(BookingStatuses.Cancelled)]
    public async Task OnlyCompletedBookings_CanBeReviewed(string status)
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana, status);

        Assert.Equal(ReviewEligibility.NotCompleted, (await w.Svc.GetPageAsync(w.Ana.UserId, b.BookingId)).Outcome);
        Assert.Equal(SubmitReviewOutcome.NotCompleted, (await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = 5 })).Outcome);
        Assert.Empty(w.Db.Reviews);
    }

    [Fact]
    public async Task SomeoneElsesBooking_LooksLikeItDoesNotExist()
    {
        var w = await CreateAsync();
        var anas = await BookAsync(w, w.Ana);

        Assert.Equal(ReviewEligibility.NotFound, (await w.Svc.GetPageAsync(w.Ben.UserId, anas.BookingId)).Outcome);
        Assert.Equal(SubmitReviewOutcome.NotFound, (await w.Svc.SubmitAsync(w.Ben.UserId, anas.BookingId, new LeaveReviewForm { Rating = 1 })).Outcome);
        Assert.Equal(ReviewEligibility.NotFound, (await w.Svc.GetPageAsync(w.Ana.UserId, 99999)).Outcome);
        Assert.Empty(w.Db.Reviews);
    }

    [Fact]
    public async Task ProvidersAndSuspendedCustomers_AreNotAllowed()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana);

        Assert.Equal(ReviewEligibility.NotAllowed, (await w.Svc.GetPageAsync(w.Nina.UserId, b.BookingId)).Outcome);
        Assert.Equal(SubmitReviewOutcome.NotAllowed, (await w.Svc.SubmitAsync(w.Nina.UserId, b.BookingId, new LeaveReviewForm { Rating = 5 })).Outcome);

        var anaUser = await w.Db.Users.FirstAsync(u => u.UserId == w.Ana.UserId);
        anaUser.AccountStatus = AccountStatuses.Suspended;
        await w.Db.SaveChangesAsync();
        Assert.Equal(SubmitReviewOutcome.NotAllowed, (await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = 5 })).Outcome);
        Assert.Empty(w.Db.Reviews);
    }

    // ------------------------------------------------------------------ submitting

    [Fact]
    public async Task Submit_SavesAVisibleReview_UpdatesTheRating_AndTellsTheProvider()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana);

        var result = await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = 4, Comment = "  On time and careful.  " });

        Assert.Equal(SubmitReviewOutcome.Ok, result.Outcome);
        var review = await w.Db.Reviews.SingleAsync();
        Assert.Equal(b.BookingId, review.BookingId);
        Assert.Equal(4, review.Rating);
        Assert.Equal("On time and careful.", review.Comment);
        Assert.Equal(ReviewStatuses.Visible, review.Status);

        var nina = await w.Db.Providers.AsNoTracking().SingleAsync(p => p.ProviderId == w.Nina.ProviderId);
        Assert.Equal(1, nina.ReviewCount);
        Assert.Equal(4.0m, nina.AvgRating);

        var note = await w.Db.Notifications.SingleAsync();
        Assert.Equal(w.Nina.UserId, note.UserId);
        Assert.Contains("4-star review for booking #" + b.BookingId, note.Message);
    }

    [Fact]
    public async Task Submit_Twice_IsRefused_TheSecondTime()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana);

        Assert.Equal(SubmitReviewOutcome.Ok, (await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = 5 })).Outcome);
        Assert.Equal(SubmitReviewOutcome.AlreadyReviewed, (await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = 1 })).Outcome);
        Assert.Equal(ReviewEligibility.AlreadyReviewed, (await w.Svc.GetPageAsync(w.Ana.UserId, b.BookingId)).Outcome);

        Assert.Equal(5, (await w.Db.Reviews.SingleAsync()).Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Submit_RatingOutside1To5_IsInvalid(int rating)
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana);

        Assert.Equal(SubmitReviewOutcome.Invalid, (await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = rating })).Outcome);
        Assert.Empty(w.Db.Reviews);
    }

    [Fact]
    public async Task Submit_TooLongComment_IsInvalid_AndAnEmptyCommentIsStoredAsNothing()
    {
        var w = await CreateAsync();
        var b = await BookAsync(w, w.Ana);

        var longText = new string('a', CustomerReviewService.MaxCommentLength + 1);
        Assert.Equal(SubmitReviewOutcome.Invalid, (await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = 3, Comment = longText })).Outcome);

        Assert.Equal(SubmitReviewOutcome.Ok, (await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = 3, Comment = "   " })).Outcome);
        Assert.Null((await w.Db.Reviews.SingleAsync()).Comment);
    }

    [Fact]
    public async Task Rating_IsTheAverageOfVisibleReviewsOnly()
    {
        var w = await CreateAsync();
        var hidden = await BookAsync(w, w.Ben);
        w.Db.Reviews.Add(new Review { BookingId = hidden.BookingId, Rating = 1, Status = ReviewStatuses.Removed });   // hidden by an admin
        var earlier = await BookAsync(w, w.Ben);
        w.Db.Reviews.Add(new Review { BookingId = earlier.BookingId, Rating = 4, Status = ReviewStatuses.Visible });
        await w.Db.SaveChangesAsync();
        var b = await BookAsync(w, w.Ana);

        await w.Svc.SubmitAsync(w.Ana.UserId, b.BookingId, new LeaveReviewForm { Rating = 5 });

        var nina = await w.Db.Providers.AsNoTracking().SingleAsync(p => p.ProviderId == w.Nina.ProviderId);
        Assert.Equal(2, nina.ReviewCount);       // the removed review does not count
        Assert.Equal(4.5m, nina.AvgRating);      // (4 + 5) / 2
    }
}

public class BookAgainTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
    }

    private static User NewUser(string first, string role) =>
        new() { FirstName = first, LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = AccountStatuses.Active };

    [Fact]
    public async Task Dashboard_RecentBooking_KnowsItsProviderAndServices()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var cat = new ServiceCategory { CategoryName = "Cleaning" };
        var ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        var nina = new Provider { BusinessName = "Nina Santiago", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Nina", Roles.ServiceProvider) };
        db.AddRange(cat, ana, nina);
        await db.SaveChangesAsync();
        var s1 = new Service { ProviderId = nina.ProviderId, CategoryId = cat.CategoryId, ServiceName = "Deep Cleaning", Price = 1500m };
        var s2 = new Service { ProviderId = nina.ProviderId, CategoryId = cat.CategoryId, ServiceName = "Disinfection", Price = 800m };
        db.Services.AddRange(s1, s2);
        await db.SaveChangesAsync();
        var b = new Booking
        {
            CustomerId = ana.CustomerId, ProviderId = nina.ProviderId, BookingDate = new DateOnly(2026, 10, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 120, TotalFee = 2300m, Status = BookingStatuses.Completed, PaymentStatus = PaymentStatuses.Unpaid, RowVersion = new byte[] { 1 }
        };
        b.Services.Add(new BookingService { ServiceId = s1.ServiceId, PriceSnapshot = 1500m, DurationMinutesSnapshot = 60 });
        b.Services.Add(new BookingService { ServiceId = s2.ServiceId, PriceSnapshot = 800m, DurationMinutesSnapshot = 60 });
        db.Bookings.Add(b);
        await db.SaveChangesAsync();

        var vm = await new CustomerDashboardService(db, new FixedClock()).GetAsync(ana.UserId);

        var recent = Assert.Single(vm!.Recent);
        Assert.Equal(nina.ProviderId, recent.ProviderId);
        Assert.Equal(new[] { s1.ServiceId, s2.ServiceId }, recent.ServiceIds.ToArray());
    }
}
