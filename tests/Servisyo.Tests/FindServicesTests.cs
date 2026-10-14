using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class FindServicesViewModelTests
{
    [Theory]
    [InlineData("rating", "rating")]
    [InlineData("REVIEWS", "reviews")]
    [InlineData(" price ", "price")]
    [InlineData("name", "name")]
    [InlineData("nonsense", "rating")]       // unknown values fall back to the default order
    [InlineData("", "rating")]
    [InlineData(null, "rating")]
    public void Sort_Find_FallsBackToRating(string? asked, string expectedKey)
        => Assert.Equal(expectedKey, FindServicesSort.Find(asked).Key);

    [Fact]
    public void Card_Rating_ShowsOneDecimalAtMost_AndRoundsTheStars()
    {
        var five = new FindProviderCardViewModel { AvgRating = 5.0m, ReviewCount = 3 };
        Assert.Equal("5", five.RatingText);
        Assert.Equal(5, five.FilledStars);

        var fourNine = new FindProviderCardViewModel { AvgRating = 4.9m, ReviewCount = 10 };
        Assert.Equal("4.9", fourNine.RatingText);
        Assert.Equal(5, fourNine.FilledStars);

        var twoFour = new FindProviderCardViewModel { AvgRating = 2.4m, ReviewCount = 2 };
        Assert.Equal(2, twoFour.FilledStars);

        var half = new FindProviderCardViewModel { AvgRating = 3.5m, ReviewCount = 2 };
        Assert.Equal(4, half.FilledStars);
    }

    [Fact]
    public void Card_WithoutReviews_HasNoRating()
    {
        Assert.False(new FindProviderCardViewModel { AvgRating = null, ReviewCount = 0 }.HasReviews);
        Assert.False(new FindProviderCardViewModel { AvgRating = 4m, ReviewCount = 0 }.HasReviews);
        Assert.False(new FindProviderCardViewModel { AvgRating = null, ReviewCount = 2 }.HasReviews);
        Assert.True(new FindProviderCardViewModel { AvgRating = 4m, ReviewCount = 2 }.HasReviews);
        Assert.Equal(0, new FindProviderCardViewModel().FilledStars);
    }

    [Theory]
    [InlineData(1, "(1 review)")]
    [InlineData(2, "(2 reviews)")]
    [InlineData(1234, "(1,234 reviews)")]
    public void Card_ReviewsText_UsesSingularAndPlural(int count, string expected)
        => Assert.Equal(expected, new FindProviderCardViewModel { ReviewCount = count }.ReviewsText);

    [Fact]
    public void Card_MoreText_AndLocationText()
    {
        Assert.Equal("+2 more", new FindProviderCardViewModel { MoreServices = 2 }.MoreText);
        Assert.Null(new FindProviderCardViewModel { MoreServices = 0 }.MoreText);
        Assert.Equal("Cebu City", new FindProviderCardViewModel { City = "Cebu City" }.LocationText);
        Assert.Null(new FindProviderCardViewModel { City = "  " }.LocationText);
        Assert.Null(new FindProviderCardViewModel { City = null }.LocationText);
    }

    [Fact]
    public void Card_PriceText_IsFormattedAsPesos()
    {
        Assert.Equal(ServiceFormat.Peso(1500m), new FindProviderCardViewModel { Price = 1500m }.PriceText);
        Assert.Null(new FindProviderCardViewModel { Price = null }.PriceText);
    }

    [Theory]
    [InlineData(0, "No providers found")]
    [InlineData(1, "1 provider found")]
    [InlineData(4, "4 providers found")]
    [InlineData(1200, "1,200 providers found")]
    public void Page_FoundText(int total, string expected)
        => Assert.Equal(expected, new FindServicesPageViewModel { TotalMatching = total }.FoundText);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(12, 1)]
    [InlineData(13, 2)]
    [InlineData(24, 2)]
    [InlineData(25, 3)]
    public void Page_PageCount(int total, int pages)
        => Assert.Equal(pages, new FindServicesPageViewModel { TotalMatching = total }.PageCount);

    [Fact]
    public void Page_IsFiltered_WhenSearchingOrACategoryIsChosen()
    {
        Assert.False(new FindServicesPageViewModel().IsFiltered);
        Assert.True(new FindServicesPageViewModel { Q = "cebu" }.IsFiltered);
        Assert.True(new FindServicesPageViewModel { CategoryId = 3 }.IsFiltered);
    }
}

public class FindServicesServiceTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private const byte Wednesday = 3;
    private const byte Thursday = 4;
    private const byte Saturday = 6;

    private static (FindServicesService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new FindServicesService(db, new FixedClock()), db);
    }

    private static async Task<User> AddCustomerAsync(ServisyoDbContext db, string status = AccountStatuses.Active, string first = "Ana", string last = "Reyes")
    {
        var user = new User
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = Roles.Customer, AccountStatus = status,
            Customer = new Customer()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<ServiceCategory> AddCategoryAsync(ServisyoDbContext db, string name, bool active = true)
    {
        var c = new ServiceCategory { CategoryName = name, IsActive = active };
        db.ServiceCategories.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    /// An eligible provider by default (Approved + Active account); add services with AddServiceAsync.
    private static async Task<Provider> AddProviderAsync(ServisyoDbContext db, string business, string first = "Marco", string last = "Reyes",
        string? city = "Cebu City", decimal? rating = null, int reviews = 0, decimal? baseFee = null, int minNotice = 1,
        string approval = ApprovalStatuses.Approved, string account = AccountStatuses.Active, string? description = null)
    {
        var p = new Provider
        {
            BusinessName = business, City = city, Description = description, AvgRating = rating, ReviewCount = reviews, BaseFee = baseFee,
            MinNoticeDays = minNotice, ApprovalStatus = approval,
            User = new User
            {
                FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
                Role = Roles.ServiceProvider, AccountStatus = account
            }
        };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<Service> AddServiceAsync(ServisyoDbContext db, Provider p, ServiceCategory category, string name,
        decimal price = 500m, bool active = true)
    {
        var s = new Service { ProviderId = p.ProviderId, CategoryId = category.CategoryId, ServiceName = name, Price = price, DurationMinutes = 60, IsActive = active };
        db.Services.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    /// One eligible provider with one active service in the category.
    private static async Task<Provider> AddListedAsync(ServisyoDbContext db, ServiceCategory category, string business, string first = "Marco", string last = "Reyes",
        string? city = "Cebu City", decimal? rating = null, int reviews = 0, decimal? baseFee = null, decimal price = 500m)
    {
        var p = await AddProviderAsync(db, business, first, last, city, rating, reviews, baseFee);
        await AddServiceAsync(db, p, category, "Service of " + business, price);
        return p;
    }

    private static async Task<FindServicesPageViewModel> OpenAsync(FindServicesService svc, User customer, FindServicesQuery? query = null)
    {
        var result = await svc.GetAsync(customer.UserId, query ?? new FindServicesQuery());
        Assert.True(result.Allowed);
        return result.Page!;
    }

    // ---------------------------------------------------------------- who may open the page

    [Fact]
    public async Task UnknownUser_IsNotAllowed()
    {
        var (svc, _) = Create();
        var result = await svc.GetAsync(999, new FindServicesQuery());
        Assert.False(result.Allowed);
        Assert.Null(result.Page);
    }

    [Theory]
    [InlineData(AccountStatuses.Suspended)]
    [InlineData(AccountStatuses.Deactivated)]
    public async Task InactiveCustomer_IsNotAllowed(string status)
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db, status);
        Assert.False((await svc.GetAsync(customer.UserId, new FindServicesQuery())).Allowed);
    }

    [Fact]
    public async Task AProvider_IsNotAllowed_BecauseThePageIsForCustomers()
    {
        var (svc, db) = Create();
        var provider = await AddProviderAsync(db, "Marco's Plumbing");
        Assert.False((await svc.GetAsync(provider.UserId, new FindServicesQuery())).Allowed);
    }

    [Fact]
    public async Task Shell_ShowsTheCustomer_AndTheirUnreadCounts()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db, first: "Ana", last: "Reyes");
        var other = await AddCustomerAsync(db, first: "Ben", last: "Lim");
        db.Notifications.AddRange(
            new Notification { UserId = customer.UserId, Message = "a" },
            new Notification { UserId = customer.UserId, Message = "b", IsRead = true },
            new Notification { UserId = other.UserId, Message = "c" });
        db.Messages.Add(new Message { SenderId = other.UserId, ReceiverId = customer.UserId, MessageText = "hi" });
        await db.SaveChangesAsync();

        var page = await OpenAsync(svc, customer);

        Assert.Equal("Customer", page.Shell.Role);
        Assert.Equal("Ana", page.Shell.FirstName);
        Assert.Equal("Ana Reyes", page.Shell.FullName);
        Assert.Equal("AR", page.Shell.Initials);
        Assert.Equal(1, page.Shell.UnreadNotifications);
        Assert.Equal(1, page.Shell.UnreadMessages);
    }

    // ---------------------------------------------------------------- who is listed

    [Fact]
    public async Task OnlyApprovedActiveProviders_WithAnActiveService_AreListed()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");

        await AddListedAsync(db, plumbing, "Shown Plumbing");

        var pending = await AddProviderAsync(db, "Pending Plumbing", approval: ApprovalStatuses.Pending);
        await AddServiceAsync(db, pending, plumbing, "x");
        var rejected = await AddProviderAsync(db, "Rejected Plumbing", approval: ApprovalStatuses.Rejected);
        await AddServiceAsync(db, rejected, plumbing, "x");
        var suspended = await AddProviderAsync(db, "Suspended Plumbing", account: AccountStatuses.Suspended);
        await AddServiceAsync(db, suspended, plumbing, "x");
        var deactivated = await AddProviderAsync(db, "Deactivated Plumbing", account: AccountStatuses.Deactivated);
        await AddServiceAsync(db, deactivated, plumbing, "x");
        var noServices = await AddProviderAsync(db, "No Services Plumbing");
        var onlyInactive = await AddProviderAsync(db, "Inactive Service Plumbing");
        await AddServiceAsync(db, onlyInactive, plumbing, "x", active: false);

        var page = await OpenAsync(svc, customer);

        var card = Assert.Single(page.Cards);
        Assert.Equal("Shown Plumbing", card.BusinessName);
        Assert.Equal(1, page.AllCount);
        Assert.Equal(1, page.TotalMatching);
        Assert.Equal("1 provider found", page.FoundText);
    }

    [Fact]
    public async Task Card_ShowsThePersonBusinessCityAndInitials()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, plumbing, "Marco's Plumbing Works", "Marco", "Reyes", "Quezon City");

        var card = Assert.Single((await OpenAsync(svc, customer)).Cards);

        Assert.Equal("Marco Reyes", card.Name);
        Assert.Equal("MR", card.Initials);
        Assert.Equal("Marco's Plumbing Works", card.BusinessName);
        Assert.Equal("Quezon City", card.City);
    }

    [Fact]
    public async Task NoProviders_GivesAnEmptyPage_NotAnError()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);

        var page = await OpenAsync(svc, customer);

        Assert.Empty(page.Cards);
        Assert.Empty(page.Chips);
        Assert.Equal(0, page.AllCount);
        Assert.Equal(1, page.PageCount);
        Assert.Equal("No providers found", page.FoundText);
    }

    // ---------------------------------------------------------------- search

    [Theory]
    [InlineData("marco")]
    [InlineData("MARCO")]
    [InlineData("reyes")]
    [InlineData("marco reyes")]
    [InlineData("  marco    reyes  ")]       // extra spaces are collapsed
    [InlineData("plumbing works")]           // business name
    [InlineData("quezon")]                   // city
    public async Task Search_MatchesNameBusinessAndCity_IgnoringCaseAndSpaces(string typed)
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, plumbing, "Marco's Plumbing Works", "Marco", "Reyes", "Quezon City");
        await AddListedAsync(db, plumbing, "Other Cleaning Co", "Lina", "Santos", "Cebu City");

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Q = typed });

        var card = Assert.Single(page.Cards);
        Assert.Equal("Marco's Plumbing Works", card.BusinessName);
        Assert.DoesNotContain("  ", page.Q);                         // the search text is shown back with single spaces
        Assert.Equal(page.Q, page.Q.Trim());
    }

    [Fact]
    public async Task Search_DoesNotMatchServiceNames_OrDescriptions()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var p = await AddProviderAsync(db, "Marco's Works", description: "pipe specialist");
        await AddServiceAsync(db, p, plumbing, "Drain Unclogging");

        Assert.Empty((await OpenAsync(svc, customer, new FindServicesQuery { Q = "drain" })).Cards);
        Assert.Empty((await OpenAsync(svc, customer, new FindServicesQuery { Q = "pipe" })).Cards);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsNothing_AndIsFlaggedAsFiltered()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, plumbing, "Marco's Plumbing Works");

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Q = "zzz" });

        Assert.Empty(page.Cards);
        Assert.True(page.IsFiltered);
        Assert.Equal("No providers found", page.FoundText);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[a-z]")]
    public async Task Search_TreatsWildcardCharactersAsPlainText(string typed)
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, plumbing, "Marco's Plumbing Works");

        Assert.Empty((await OpenAsync(svc, customer, new FindServicesQuery { Q = typed })).Cards);
    }

    [Fact]
    public async Task Search_NeverShowsHiddenProviders()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var pending = await AddProviderAsync(db, "Secret Plumbing", approval: ApprovalStatuses.Pending);
        await AddServiceAsync(db, pending, plumbing, "x");

        Assert.Empty((await OpenAsync(svc, customer, new FindServicesQuery { Q = "secret" })).Cards);
    }

    [Fact]
    public async Task Search_VeryLongText_IsCutTo100Characters()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Q = new string('a', 500) });

        Assert.Equal(100, page.Q.Length);
    }

    // ---------------------------------------------------------------- categories

    [Fact]
    public async Task Chips_CountProviders_NotServices_AndKeepTheirOrder()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var cleaning = await AddCategoryAsync(db, "Cleaning");

        var a = await AddListedAsync(db, plumbing, "A Works");
        await AddServiceAsync(db, a, plumbing, "Second plumbing service");     // same provider again: still counts once
        await AddServiceAsync(db, a, cleaning, "Cleaning too");
        await AddListedAsync(db, plumbing, "B Works");

        var page = await OpenAsync(svc, customer);

        Assert.Equal(2, page.AllCount);
        Assert.Equal(new[] { "Cleaning", "Plumbing" }, page.Chips.Select(c => c.Name).ToArray());      // A-Z
        Assert.Equal(1, page.Chips.Single(c => c.Name == "Cleaning").Count);
        Assert.Equal(2, page.Chips.Single(c => c.Name == "Plumbing").Count);
    }

    [Fact]
    public async Task Chips_HideInactiveCategories_AndCategoriesNobodyOffers()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        await AddCategoryAsync(db, "Gardening");                              // active, but nobody offers it
        var closed = await AddCategoryAsync(db, "Closed", active: false);     // switched off by the administrator
        var p = await AddListedAsync(db, plumbing, "A Works");
        await AddServiceAsync(db, p, closed, "Old service");

        var page = await OpenAsync(svc, customer);

        Assert.Equal(new[] { "Plumbing" }, page.Chips.Select(c => c.Name).ToArray());
        Assert.Equal(1, page.AllCount);
    }

    [Fact]
    public async Task Chips_FollowTheSearchText()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var cleaning = await AddCategoryAsync(db, "Cleaning");
        await AddListedAsync(db, plumbing, "Marco Works", "Marco", "Reyes");
        await AddListedAsync(db, cleaning, "Lina Cleaning", "Lina", "Santos");

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Q = "marco" });

        Assert.Equal(1, page.AllCount);
        Assert.Equal("Plumbing", Assert.Single(page.Chips).Name);
    }

    [Fact]
    public async Task CategoryFilter_ShowsOnlyProvidersOfferingIt_AndTheBadgeNamesIt()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var cleaning = await AddCategoryAsync(db, "Cleaning");
        await AddListedAsync(db, plumbing, "Plumber One");
        var both = await AddListedAsync(db, cleaning, "Both Co");
        await AddServiceAsync(db, both, plumbing, "Also plumbing");
        await AddListedAsync(db, cleaning, "Cleaner One");

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Category = plumbing.CategoryId });

        Assert.Equal(plumbing.CategoryId, page.CategoryId);
        Assert.Equal(new[] { "Both Co", "Plumber One" }, page.Cards.Select(c => c.BusinessName).OrderBy(n => n).ToArray());
        Assert.All(page.Cards, c => Assert.Equal("Plumbing", c.CategoryName));
        Assert.Equal(3, page.AllCount);                    // the "All" chip still counts everybody
        Assert.True(page.IsFiltered);
    }

    [Fact]
    public async Task CategoryFilter_IgnoresInactiveInvalidAndUnknownCategories()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var closed = await AddCategoryAsync(db, "Closed", active: false);
        await AddListedAsync(db, plumbing, "A Works");

        foreach (var asked in new[] { closed.CategoryId, 9999, -1, 0 })
        {
            var page = await OpenAsync(svc, customer, new FindServicesQuery { Category = asked });
            Assert.Null(page.CategoryId);
            Assert.Single(page.Cards);
        }
    }

    [Fact]
    public async Task CategoryBadge_WithoutAFilter_IsTheFirstCategoryAlphabetically()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var cleaning = await AddCategoryAsync(db, "Cleaning");
        var p = await AddListedAsync(db, plumbing, "Both Co");
        await AddServiceAsync(db, p, cleaning, "Cleaning");

        Assert.Equal("Cleaning", Assert.Single((await OpenAsync(svc, customer)).Cards).CategoryName);
    }

    [Fact]
    public async Task SearchAndCategory_WorkTogether()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var plumbing = await AddCategoryAsync(db, "Plumbing");
        var cleaning = await AddCategoryAsync(db, "Cleaning");
        await AddListedAsync(db, plumbing, "Cebu Plumbing", city: "Cebu City");
        await AddListedAsync(db, plumbing, "Manila Plumbing", city: "Manila");
        await AddListedAsync(db, cleaning, "Cebu Cleaning", city: "Cebu City");

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Q = "cebu", Category = plumbing.CategoryId });

        Assert.Equal("Cebu Plumbing", Assert.Single(page.Cards).BusinessName);
    }

    // ---------------------------------------------------------------- sorting

    [Fact]
    public async Task Sort_Default_IsHighestRatingThenMostReviewsThenName()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, c, "D New", rating: null, reviews: 0);
        await AddListedAsync(db, c, "C Four", rating: 4.0m, reviews: 5);
        await AddListedAsync(db, c, "B Five", rating: 5.0m, reviews: 2);
        await AddListedAsync(db, c, "A Four Many", rating: 4.0m, reviews: 20);

        var page = await OpenAsync(svc, customer);

        Assert.Equal(new[] { "B Five", "A Four Many", "C Four", "D New" }, page.Cards.Select(x => x.BusinessName).ToArray());
        Assert.Equal("rating", page.Sort);
    }

    [Fact]
    public async Task Sort_MostReviews()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, c, "A", rating: 5.0m, reviews: 1);
        await AddListedAsync(db, c, "B", rating: 3.0m, reviews: 30);
        await AddListedAsync(db, c, "C", rating: 4.0m, reviews: 10);

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Sort = "reviews" });

        Assert.Equal(new[] { "B", "C", "A" }, page.Cards.Select(x => x.BusinessName).ToArray());
    }

    [Fact]
    public async Task Sort_LowestPrice_UsesTheBaseFee_ElseTheCheapestActiveService()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, c, "Expensive Fee", baseFee: 900m, price: 100m);       // base fee wins over the cheap service
        await AddListedAsync(db, c, "Cheap Service", baseFee: null, price: 300m);
        var mixed = await AddListedAsync(db, c, "Mixed Services", baseFee: null, price: 700m);
        await AddServiceAsync(db, mixed, c, "Cheaper one", 200m);
        await AddServiceAsync(db, mixed, c, "Cheapest but switched off", 50m, active: false);   // not counted

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Sort = "price" });

        Assert.Equal(new[] { "Mixed Services", "Cheap Service", "Expensive Fee" }, page.Cards.Select(x => x.BusinessName).ToArray());
        Assert.Equal(200m, page.Cards[0].Price);
        Assert.Equal("Starting at", page.Cards[0].PriceLabel);
        Assert.Equal(900m, page.Cards[2].Price);
        Assert.Equal("Base Fee", page.Cards[2].PriceLabel);
    }

    [Fact]
    public async Task Sort_Name_IsAlphabeticalByThePersonsName()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, c, "Z Biz", "Carlo", "Zamora");
        await AddListedAsync(db, c, "A Biz", "Zed", "Alvarez");
        await AddListedAsync(db, c, "M Biz", "Carlo", "Abad");

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Sort = "name" });

        Assert.Equal(new[] { "M Biz", "Z Biz", "A Biz" }, page.Cards.Select(x => x.BusinessName).ToArray());
    }

    [Fact]
    public async Task Sort_UnknownValue_FallsBackToRating()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, c, "Low", rating: 2m, reviews: 1);
        await AddListedAsync(db, c, "High", rating: 5m, reviews: 1);

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Sort = "'; DROP TABLE USERS;--" });

        Assert.Equal("rating", page.Sort);
        Assert.Equal("High", page.Cards[0].BusinessName);
    }

    // ---------------------------------------------------------------- paging

    [Fact]
    public async Task Paging_Shows12PerPage_WithNoRepeats()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        for (var i = 1; i <= 13; i++) await AddListedAsync(db, c, $"Provider {i:D2}", rating: 4m, reviews: 3);     // identical ratings: ties

        var first = await OpenAsync(svc, customer, new FindServicesQuery { Page = 1 });
        var second = await OpenAsync(svc, customer, new FindServicesQuery { Page = 2 });

        Assert.Equal(12, first.Cards.Count);
        Assert.Single(second.Cards);
        Assert.Equal(13, first.TotalMatching);
        Assert.Equal(2, first.PageCount);
        Assert.Equal(13, first.Cards.Concat(second.Cards).Select(x => x.ProviderId).Distinct().Count());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(99, 2)]
    public async Task Paging_OutOfRangePageNumbers_AreCorrected(int asked, int expectedPage)
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        for (var i = 1; i <= 13; i++) await AddListedAsync(db, c, $"Provider {i:D2}");

        var page = await OpenAsync(svc, customer, new FindServicesQuery { Page = asked });

        Assert.Equal(expectedPage, page.Page);
        Assert.NotEmpty(page.Cards);
    }

    // ---------------------------------------------------------------- what a card shows

    [Fact]
    public async Task Card_ListsTheFirstThreeActiveServices_AndCountsTheRest()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        var p = await AddProviderAsync(db, "Marco's Works");
        await AddServiceAsync(db, p, c, "Pipe Repair");
        await AddServiceAsync(db, p, c, "Drain Unclogging");
        await AddServiceAsync(db, p, c, "Faucet Replacement");
        await AddServiceAsync(db, p, c, "Water Heater Repair");
        await AddServiceAsync(db, p, c, "Waterproofing");
        await AddServiceAsync(db, p, c, "Switched off", active: false);      // never shown, never counted

        var card = Assert.Single((await OpenAsync(svc, customer)).Cards);

        Assert.Equal(new[] { "Pipe Repair", "Drain Unclogging", "Faucet Replacement" }, card.ServiceTags.ToArray());
        Assert.Equal(2, card.MoreServices);
        Assert.Equal("+2 more", card.MoreText);
    }

    [Fact]
    public async Task Card_WithThreeServicesOrFewer_HasNoMoreTag()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        var p = await AddProviderAsync(db, "Marco's Works");
        await AddServiceAsync(db, p, c, "One");
        await AddServiceAsync(db, p, c, "Two");
        await AddServiceAsync(db, p, c, "Three");

        var card = Assert.Single((await OpenAsync(svc, customer)).Cards);

        Assert.Equal(3, card.ServiceTags.Count);
        Assert.Equal(0, card.MoreServices);
        Assert.Null(card.MoreText);
    }

    [Fact]
    public async Task Card_Rating_IsHiddenUntilThereIsAReview()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, c, "New Biz", rating: null, reviews: 0);
        await AddListedAsync(db, c, "Rated Biz", rating: 4.5m, reviews: 8);

        var cards = (await OpenAsync(svc, customer)).Cards;

        var rated = cards.Single(x => x.BusinessName == "Rated Biz");
        Assert.True(rated.HasReviews);
        Assert.Equal("4.5", rated.RatingText);
        Assert.Equal("(8 reviews)", rated.ReviewsText);

        var fresh = cards.Single(x => x.BusinessName == "New Biz");
        Assert.False(fresh.HasReviews);
        Assert.Null(fresh.AvgRating);
    }

    [Fact]
    public async Task Card_Description_IsTrimmed_AndBlankBecomesNull()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        var a = await AddProviderAsync(db, "A Biz", description: "  Licensed plumber.  ");
        await AddServiceAsync(db, a, c, "x");
        var b = await AddProviderAsync(db, "B Biz", description: "   ", city: "  ");
        await AddServiceAsync(db, b, c, "x");

        var cards = (await OpenAsync(svc, customer)).Cards;

        Assert.Equal("Licensed plumber.", cards.Single(x => x.BusinessName == "A Biz").Description);
        var blank = cards.Single(x => x.BusinessName == "B Biz");
        Assert.Null(blank.Description);
        Assert.Null(blank.City);
    }

    // ---------------------------------------------------------------- availability hint

    private static async Task AddScheduleAsync(ServisyoDbContext db, Provider p, byte day, bool available = true)
    {
        db.ProviderAvailabilities.Add(new ProviderAvailability
        {
            ProviderId = p.ProviderId, DayOfWeek = day, IsAvailable = available,
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Availability_Today_WhenWorkingTodayAndNoNoticeIsNeeded()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        var p = await AddProviderAsync(db, "Now Biz", minNotice: 0);
        await AddServiceAsync(db, p, c, "x");
        await AddScheduleAsync(db, p, Wednesday);

        var card = Assert.Single((await OpenAsync(svc, customer)).Cards);

        Assert.Equal("Available Today", card.AvailabilityText);
        Assert.Equal("completed", card.AvailabilityTone);
    }

    [Fact]
    public async Task Availability_Tomorrow_ReadsAsNextTomorrow()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        var p = await AddProviderAsync(db, "Soon Biz", minNotice: 1);
        await AddServiceAsync(db, p, c, "x");
        await AddScheduleAsync(db, p, Wednesday);        // today's hours do not count: one day of notice is needed
        await AddScheduleAsync(db, p, Thursday);

        var card = Assert.Single((await OpenAsync(svc, customer)).Cards);

        Assert.Equal("Next: Tomorrow", card.AvailabilityText);
        Assert.Equal("pending", card.AvailabilityTone);
    }

    [Fact]
    public async Task Availability_LaterThisWeek_ReadsAsNextThisWeek()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        var p = await AddProviderAsync(db, "Weekend Biz", minNotice: 0);
        await AddServiceAsync(db, p, c, "x");
        await AddScheduleAsync(db, p, Saturday);

        var card = Assert.Single((await OpenAsync(svc, customer)).Cards);

        Assert.Equal("Next: This Week", card.AvailabilityText);
    }

    [Fact]
    public async Task Availability_SkipsUnavailableDates_AndDaysSwitchedOff()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        var p = await AddProviderAsync(db, "Busy Biz", minNotice: 0);
        await AddServiceAsync(db, p, c, "x");
        await AddScheduleAsync(db, p, Wednesday);                          // but today is marked unavailable
        await AddScheduleAsync(db, p, Thursday, available: false);         // a day that is switched off
        db.ProviderUnavailableDates.Add(new ProviderUnavailableDate { ProviderId = p.ProviderId, UnavailableDate = new DateOnly(2026, 10, 14) });
        await db.SaveChangesAsync();

        var card = Assert.Single((await OpenAsync(svc, customer)).Cards);

        // next Wednesday (7 days away) is the first working day left
        Assert.Equal("Next: This Week", card.AvailabilityText);
    }

    [Fact]
    public async Task Availability_NoSchedule_ShowsNoBadge()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, c, "No Hours Biz");

        var card = Assert.Single((await OpenAsync(svc, customer)).Cards);

        Assert.Null(card.AvailabilityText);
    }

    [Fact]
    public async Task Availability_IsPerProvider()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        var a = await AddProviderAsync(db, "A Biz", minNotice: 0, rating: 5m, reviews: 1);
        await AddServiceAsync(db, a, c, "x");
        await AddScheduleAsync(db, a, Wednesday);
        var b = await AddProviderAsync(db, "B Biz", minNotice: 0, rating: 4m, reviews: 1);
        await AddServiceAsync(db, b, c, "x");

        var cards = (await OpenAsync(svc, customer)).Cards;

        Assert.Equal("Available Today", cards.Single(x => x.BusinessName == "A Biz").AvailabilityText);
        Assert.Null(cards.Single(x => x.BusinessName == "B Biz").AvailabilityText);
    }

    // ---------------------------------------------------------------- the page changes nothing

    [Fact]
    public async Task OpeningThePage_ChangesNothing()
    {
        var (svc, db) = Create();
        var customer = await AddCustomerAsync(db);
        var c = await AddCategoryAsync(db, "Plumbing");
        await AddListedAsync(db, c, "A Works", rating: 4m, reviews: 2);
        var users = await db.Users.CountAsync();
        var providers = await db.Providers.CountAsync();
        var notes = await db.Notifications.CountAsync();

        await OpenAsync(svc, customer, new FindServicesQuery { Q = "a", Sort = "price", Category = c.CategoryId });

        Assert.Equal(users, await db.Users.CountAsync());
        Assert.Equal(providers, await db.Providers.CountAsync());
        Assert.Equal(notes, await db.Notifications.CountAsync());
        Assert.Equal(4m, (await db.Providers.SingleAsync()).AvgRating);
    }
}
