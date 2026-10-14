using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

public class ProviderStatusesTests
{
    [Theory]
    [InlineData(ApprovalStatuses.Pending, AccountStatuses.Active, "Pending")]
    [InlineData(ApprovalStatuses.Approved, AccountStatuses.Active, "Approved")]
    [InlineData(ApprovalStatuses.Rejected, AccountStatuses.Active, "Rejected")]
    [InlineData(ApprovalStatuses.Approved, AccountStatuses.Suspended, "Suspended")]
    [InlineData(ApprovalStatuses.Approved, AccountStatuses.Deactivated, "Deactivated")]
    [InlineData(ApprovalStatuses.Pending, AccountStatuses.Suspended, "Suspended")]
    public void Primary_AccountProblemsWin_OtherwiseTheApplicationStatus(string approval, string account, string expected)
        => Assert.Equal(expected, ProviderStatuses.Primary(approval, account));

    [Theory]
    [InlineData("pending", "Pending")]
    [InlineData(" APPROVED ", "Approved")]
    [InlineData("Suspended", "Suspended")]
    [InlineData("rejected", "Rejected")]
    [InlineData("deactivated", "Deactivated")]
    [InlineData("All", "All")]
    [InlineData("nonsense", "All")]
    [InlineData(null, "All")]
    public void Normalize_AcceptsKnownTabs_AndFallsBackToAll(string? input, string expected)
        => Assert.Equal(expected, ProviderStatuses.Normalize(input));
}

public class ProviderCardViewModelTests
{
    [Theory]
    [InlineData(4.9, "4.9", 5)]
    [InlineData(5.0, "5", 5)]
    [InlineData(4.7, "4.7", 5)]
    [InlineData(4.4, "4.4", 4)]
    [InlineData(2.5, "2.5", 3)]
    [InlineData(1.0, "1", 1)]
    public void Rating_IsShownShort_AndStarsAreRounded(double rating, string text, int stars)
    {
        var card = new ProviderCardViewModel { Rating = (decimal)rating, ReviewCount = 10 };

        Assert.True(card.HasRating);
        Assert.Equal(text, card.RatingText);
        Assert.Equal(stars, card.StarsFilled);
    }

    [Fact]
    public void NoReviews_MeansNoRating()
    {
        var card = new ProviderCardViewModel { Rating = 4.9m, ReviewCount = 0 };

        Assert.False(card.HasRating);
        Assert.Equal("0 reviews", card.ReviewsText);
        Assert.Equal("1 review", new ProviderCardViewModel { ReviewCount = 1 }.ReviewsText);
        Assert.Equal("112 reviews", new ProviderCardViewModel { ReviewCount = 112 }.ReviewsText);
    }
}

public class ProviderManagementServiceTests
{
    private static (ProviderManagementService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new ProviderManagementService(db), db);
    }

    private static User NewUser(string first, string last, string role, string status = AccountStatuses.Active)
        => new()
        {
            FirstName = first, LastName = last, Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x",
            Role = role, AccountStatus = status
        };

    private static Provider NewProvider(string first, string last, string business,
        string approval = ApprovalStatuses.Approved, string account = AccountStatuses.Active,
        string? city = null, decimal? rating = null, int reviews = 0, int appliedDaysAgo = 0)
        => new()
        {
            BusinessName = business, City = city, ApprovalStatus = approval, AvgRating = rating, ReviewCount = reviews,
            DateApplied = DateTime.UtcNow.AddDays(-appliedDaysAgo),
            User = NewUser(first, last, Roles.ServiceProvider, account)
        };

    private static Service NewService(Provider p, ServiceCategory cat, string name, bool active = true)
        => new() { Provider = p, Category = cat, ServiceName = name, Price = 500m, IsActive = active };

    private static async Task<User> AddAdminAsync(ServisyoDbContext db, string status = AccountStatuses.Active)
    {
        var admin = NewUser("Admin", "Servisyo", Roles.Administrator, status);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    /// The six providers of the design: 3 approved, 1 approved-but-suspended, 2 pending.
    private static async Task SeedDesignAsync(ServisyoDbContext db)
    {
        db.Providers.AddRange(
            NewProvider("Marco", "Reyes", "Marco's Plumbing Works", city: "Cebu City", rating: 4.9m, reviews: 112, appliedDaysAgo: 60),
            NewProvider("Liza", "Santos", "Liza Clean Services", city: "Mandaue", rating: 4.8m, reviews: 87, appliedDaysAgo: 50),
            NewProvider("Danilo", "Cruz", "Cruz General Repairs", city: "Lapu-Lapu", rating: 4.7m, reviews: 95, appliedDaysAgo: 40),
            NewProvider("Rhea", "Villanueva", "Rhea's Home Care", account: AccountStatuses.Suspended, city: "Talisay", rating: 5.0m, reviews: 43, appliedDaysAgo: 30),
            NewProvider("Felix", "Torres", "Torres Plumbing Co.", ApprovalStatuses.Pending, city: "Mandaue", appliedDaysAgo: 2),
            NewProvider("Maricel", "Bautista", "MB Cleaning Solutions", ApprovalStatuses.Pending, city: "Poblacion", appliedDaysAgo: 1));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task OnlyAnActiveAdministrator_CanSeeTheList()
    {
        var (svc, db) = Create();
        var provider = NewProvider("Marco", "Reyes", "Marco's");
        var customer = new Customer { User = NewUser("Ana", "Reyes", Roles.Customer) };
        db.Providers.Add(provider);
        db.Customers.Add(customer);
        var suspendedAdmin = await AddAdminAsync(db, AccountStatuses.Suspended);
        await db.SaveChangesAsync();

        Assert.Null(await svc.GetProvidersAsync(provider.UserId, new ProviderListQuery()));
        Assert.Null(await svc.GetProvidersAsync(customer.UserId, new ProviderListQuery()));
        Assert.Null(await svc.GetProvidersAsync(suspendedAdmin.UserId, new ProviderListQuery()));
        Assert.Null(await svc.GetProvidersAsync(99999, new ProviderListQuery()));
    }

    [Fact]
    public async Task EmptyPlatform_ShowsNoCards()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var vm = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!;

        Assert.Equal(0, vm.CountAll);
        Assert.Empty(vm.Cards);
        Assert.Equal("A", vm.Shell.Initials);
    }

    [Fact]
    public async Task TabNumbers_MatchTheDesign_AndAddUp()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!;

        Assert.Equal(6, vm.CountAll);
        Assert.Equal(2, vm.CountPending);
        Assert.Equal(3, vm.CountApproved);
        Assert.Equal(1, vm.CountSuspended);
        Assert.Equal(0, vm.CountRejected);
        Assert.Equal(0, vm.CountDeactivated);
        Assert.Equal(6, vm.Cards.Count);
    }

    [Theory]
    [InlineData("Pending", new[] { "Felix Torres", "Maricel Bautista" })]
    [InlineData("Approved", new[] { "Marco Reyes", "Liza Santos", "Danilo Cruz" })]
    [InlineData("Suspended", new[] { "Rhea Villanueva" })]
    [InlineData("All", new[] { "Marco Reyes", "Liza Santos", "Danilo Cruz", "Rhea Villanueva", "Felix Torres", "Maricel Bautista" })]
    public async Task Tabs_FilterTheCards_OldestApplicationFirst(string tab, string[] expectedNames)
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery { Status = tab }))!;

        Assert.Equal(expectedNames, vm.Cards.Select(c => c.Name));
        Assert.Equal(6, vm.CountAll);              // tab numbers do not change with the filter
    }

    [Fact]
    public async Task RejectedAndDeactivatedProviders_AreCountedAndFilterable()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        db.Providers.AddRange(
            NewProvider("Rita", "Rejected", "R Co", ApprovalStatuses.Rejected),
            NewProvider("Dan", "Deactivated", "D Co", account: AccountStatuses.Deactivated));
        await db.SaveChangesAsync();

        var all = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!;
        Assert.Equal(2, all.CountAll);
        Assert.Equal(1, all.CountRejected);
        Assert.Equal(1, all.CountDeactivated);
        Assert.Equal(0, all.CountApproved);

        var rejected = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery { Status = "Rejected" }))!;
        Assert.Equal("Rita Rejected", Assert.Single(rejected.Cards).Name);
        Assert.Equal("Rejected", rejected.Cards[0].Badges[0].Text);

        var deactivated = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery { Status = "deactivated" }))!;
        Assert.Equal(new[] { "Approved", "Deactivated" }, Assert.Single(deactivated.Cards).Badges.Select(b => b.Text));
    }

    [Fact]
    public async Task Badges_FollowTheDesign()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!;

        var marco = vm.Cards.Single(c => c.Name == "Marco Reyes");
        Assert.Equal(new[] { ("Approved", "completed") }, marco.Badges.Select(b => (b.Text, b.Tone)));

        var rhea = vm.Cards.Single(c => c.Name == "Rhea Villanueva");
        Assert.Equal(new[] { ("Approved", "completed"), ("Suspended", "danger") }, rhea.Badges.Select(b => (b.Text, b.Tone)));

        var felix = vm.Cards.Single(c => c.Name == "Felix Torres");
        Assert.Equal(new[] { ("Pending", "pending") }, felix.Badges.Select(b => (b.Text, b.Tone)));
    }

    [Fact]
    public async Task Cards_ShowNameInitialsBusinessAndRating_AndPendingHaveNoRating()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        await SeedDesignAsync(db);

        var vm = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!;

        var marco = vm.Cards.Single(c => c.Name == "Marco Reyes");
        Assert.Equal("MR", marco.Initials);
        Assert.Equal("Marco's Plumbing Works", marco.BusinessName);
        Assert.True(marco.HasRating);
        Assert.Equal("4.9", marco.RatingText);
        Assert.Equal("112 reviews", marco.ReviewsText);

        var rhea = vm.Cards.Single(c => c.Name == "Rhea Villanueva");
        Assert.Equal("5", rhea.RatingText);

        var felix = vm.Cards.Single(c => c.Name == "Felix Torres");
        Assert.False(felix.HasRating);
    }

    [Fact]
    public async Task ServiceTags_ShowTwo_CountTheRest_AndIgnoreInactiveServices()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        var marco = NewProvider("Marco", "Reyes", "Marco's", city: "Cebu City");
        var cat = new ServiceCategory { CategoryName = "Plumbing" };
        db.Services.AddRange(
            NewService(marco, cat, "Pipe Repair"),
            NewService(marco, cat, "Fixture Install"),
            NewService(marco, cat, "Water Heater"),
            NewService(marco, cat, "Leak Detection"),
            NewService(marco, cat, "Old Service", active: false));
        await db.SaveChangesAsync();

        var card = Assert.Single((await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!.Cards);

        Assert.Equal(new[] { "Pipe Repair", "Fixture Install" }, card.ServiceTags);
        Assert.Equal(2, card.ExtraServices);
    }

    [Fact]
    public async Task Details_AreCategoryThenCity_WithSensibleFallbacks()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);

        var declared = NewProvider("A", "Declared", "A Co", city: "Mandaue", appliedDaysAgo: 5);
        declared.ProviderCategories.Add(new ProviderCategory { Category = new ServiceCategory { CategoryName = "Plumbing" } });

        var fromService = NewProvider("B", "FromService", "B Co", city: "Talisay", appliedDaysAgo: 4);
        db.Services.Add(NewService(fromService, new ServiceCategory { CategoryName = "Cleaning" }, "House Cleaning"));

        var cityOnly = NewProvider("C", "CityOnly", "C Co", ApprovalStatuses.Pending, city: "Poblacion", appliedDaysAgo: 3);
        var nothing = NewProvider("D", "Nothing", "D Co", ApprovalStatuses.Pending, appliedDaysAgo: 2);
        db.Providers.AddRange(declared, cityOnly, nothing);
        await db.SaveChangesAsync();

        var cards = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery()))!.Cards;

        Assert.Equal("Plumbing · Mandaue", cards.Single(c => c.Name == "A Declared").Details);
        Assert.Equal("Cleaning · Talisay", cards.Single(c => c.Name == "B FromService").Details);
        Assert.Equal("Poblacion", cards.Single(c => c.Name == "C CityOnly").Details);
        Assert.Null(cards.Single(c => c.Name == "D Nothing").Details);
    }

    [Fact]
    public async Task Paging_ShowsTwelvePerPage_AndClampsThePageNumber()
    {
        var (svc, db) = Create();
        var admin = await AddAdminAsync(db);
        for (var i = 0; i < 30; i++)
            db.Providers.Add(NewProvider($"P{i:00}", "Test", $"Business {i}", appliedDaysAgo: 100 - i));
        await db.SaveChangesAsync();

        var first = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery { Page = 1 }))!;
        var last = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery { Page = 3 }))!;
        var tooFar = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery { Page = 99 }))!;
        var negative = (await svc.GetProvidersAsync(admin.UserId, new ProviderListQuery { Page = -2 }))!;

        Assert.Equal(12, first.Cards.Count);
        Assert.Equal(6, last.Cards.Count);
        Assert.Equal(3, first.PageCount);
        Assert.Equal(30, first.TotalMatching);
        Assert.Equal(3, tooFar.Page);
        Assert.Equal(1, negative.Page);
        Assert.Equal("P00 Test", first.Cards[0].Name);      // oldest application first
    }
}
