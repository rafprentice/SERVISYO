using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Xunit;

namespace Servisyo.Tests;

public class HomePageServiceTests
{
    private static (HomePageService svc, ServisyoDbContext db) Create()
    {
        var options = new DbContextOptionsBuilder<ServisyoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ServisyoDbContext(options);
        return (new HomePageService(db), db);
    }

    private static Provider AddProvider(ServisyoDbContext db, string name, string approval, string accountStatus,
        ServiceCategory? category = null, decimal? price = null)
    {
        var user = new User
        {
            FirstName = name, LastName = "Owner", Email = $"{Guid.NewGuid():N}@test.com",
            PasswordHash = "x", Role = Roles.ServiceProvider, AccountStatus = accountStatus
        };
        var provider = new Provider { User = user, BusinessName = name, ApprovalStatus = approval, City = "Cebu City" };
        db.Providers.Add(provider);

        if (category != null && price != null)
        {
            db.Services.Add(new Service
            {
                Provider = provider, Category = category, ServiceName = "Test service",
                Price = price.Value, IsActive = true
            });
        }
        return provider;
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsZeroesAndNoFakeRating()
    {
        var (svc, _) = Create();
        var vm = await svc.GetHomePageAsync();

        Assert.Equal(0, vm.Stats.ApprovedProviders);
        Assert.Equal(0, vm.Stats.CompletedBookings);
        Assert.Null(vm.Stats.AverageRating);
        Assert.Empty(vm.Categories);
        Assert.Empty(vm.FeaturedProviders);
    }

    [Fact]
    public async Task OnlyApprovedProvidersWithActiveAccountsAreCountedAndFeatured()
    {
        var (svc, db) = Create();
        var plumbing = new ServiceCategory { CategoryName = "Plumbing" };
        db.ServiceCategories.Add(plumbing);

        AddProvider(db, "Juan Plumbing", ApprovalStatuses.Approved, AccountStatuses.Active, plumbing, 500m);
        AddProvider(db, "Pending Co", ApprovalStatuses.Pending, AccountStatuses.Active, plumbing, 100m);
        AddProvider(db, "Rejected Co", ApprovalStatuses.Rejected, AccountStatuses.Active, plumbing, 100m);
        AddProvider(db, "Suspended Co", ApprovalStatuses.Approved, AccountStatuses.Suspended, plumbing, 100m);
        await db.SaveChangesAsync();

        var vm = await svc.GetHomePageAsync();

        Assert.Equal(1, vm.Stats.ApprovedProviders);
        var featured = Assert.Single(vm.FeaturedProviders);
        Assert.Equal("Juan Plumbing", featured.BusinessName);
        Assert.Equal(500m, featured.StartingPrice);
        Assert.Contains("Plumbing", featured.Categories);
        Assert.Equal(1, Assert.Single(vm.Categories).ProviderCount);
    }

    [Fact]
    public async Task ApprovedProviderWithoutActiveService_IsCountedButNotFeatured()
    {
        var (svc, db) = Create();
        AddProvider(db, "No Services Co", ApprovalStatuses.Approved, AccountStatuses.Active);
        await db.SaveChangesAsync();

        var vm = await svc.GetHomePageAsync();

        Assert.Equal(1, vm.Stats.ApprovedProviders);
        Assert.Empty(vm.FeaturedProviders);
    }

    [Fact]
    public async Task InactiveCategories_AreHidden()
    {
        var (svc, db) = Create();
        db.ServiceCategories.AddRange(
            new ServiceCategory { CategoryName = "Plumbing", IsActive = true },
            new ServiceCategory { CategoryName = "Hidden", IsActive = false });
        await db.SaveChangesAsync();

        var vm = await svc.GetHomePageAsync();

        Assert.Equal("Plumbing", Assert.Single(vm.Categories).Name);
        Assert.Equal(1, vm.Stats.ServiceCategories);
    }
}
