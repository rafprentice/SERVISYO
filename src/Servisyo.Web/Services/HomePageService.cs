using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IHomePageService
{
    Task<HomePageViewModel> GetHomePageAsync(CancellationToken ct = default);
}

public class HomePageService : IHomePageService
{
    private const int FeaturedProviderLimit = 6;
    private readonly ServisyoDbContext _db;

    public HomePageService(ServisyoDbContext db) => _db = db;

    public async Task<HomePageViewModel> GetHomePageAsync(CancellationToken ct = default)
    {
        // One eligibility rule for the whole page: Approved provider AND Active user account.
        var eligibleProviders = _db.Providers.AsNoTracking()
            .Where(p => p.ApprovalStatus == ApprovalStatuses.Approved
                     && p.User.AccountStatus == AccountStatuses.Active);

        // ---- Statistics (real counts only) ----
        var approvedProviders = await eligibleProviders.CountAsync(ct);

        var completedBookings = await _db.Bookings.AsNoTracking()
            .CountAsync(b => b.Status == BookingStatuses.Completed, ct);

        var visibleReviews = _db.Reviews.AsNoTracking().Where(r => r.Status == ReviewStatuses.Visible);
        var reviewCount = await visibleReviews.CountAsync(ct);
        double? averageRating = null;
        if (reviewCount > 0)
        {
            averageRating = Math.Round(await visibleReviews.AverageAsync(r => (double)r.Rating, ct), 1);
        }

        // ---- Categories, with a count of eligible providers that offer an active service in each ----
        var categories = await _db.ServiceCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CategoryName)
            .Select(c => new CategoryCardViewModel
            {
                CategoryId = c.CategoryId,
                Name = c.CategoryName,
                Description = c.Description,
                ProviderCount = _db.Services
                    .Where(s => s.CategoryId == c.CategoryId
                             && s.IsActive
                             && s.Provider.ApprovalStatus == ApprovalStatuses.Approved
                             && s.Provider.User.AccountStatus == AccountStatuses.Active)
                    .Select(s => s.ProviderId)
                    .Distinct()
                    .Count()
            })
            .ToListAsync(ct);

        // ---- Featured providers: eligible and offering at least one active service ----
        var providers = await eligibleProviders
            .Where(p => p.Services.Any(s => s.IsActive))
            .OrderByDescending(p => p.AvgRating ?? 0m)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.BusinessName)
            .Take(FeaturedProviderLimit)
            .Select(p => new
            {
                p.ProviderId,
                p.BusinessName,
                p.City,
                p.Description,
                p.AvgRating,
                p.ReviewCount,
                StartingPrice = p.Services.Where(s => s.IsActive).Min(s => (decimal?)s.Price)
            })
            .ToListAsync(ct);

        var providerIds = providers.Select(p => p.ProviderId).ToList();
        var categoryRows = await _db.Services.AsNoTracking()
            .Where(s => s.IsActive && providerIds.Contains(s.ProviderId))
            .Select(s => new { s.ProviderId, Name = s.Category.CategoryName })
            .Distinct()
            .ToListAsync(ct);

        var categoriesByProvider = categoryRows
            .GroupBy(r => r.ProviderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).OrderBy(n => n).ToList());

        var featured = providers.Select(p => new FeaturedProviderViewModel
        {
            ProviderId = p.ProviderId,
            BusinessName = p.BusinessName,
            City = p.City,
            Description = p.Description,
            AvgRating = p.AvgRating,
            ReviewCount = p.ReviewCount,
            StartingPrice = p.StartingPrice,
            Categories = categoriesByProvider.TryGetValue(p.ProviderId, out var names)
                ? names
                : Array.Empty<string>()
        }).ToList();

        return new HomePageViewModel
        {
            Stats = new HomeStatsViewModel
            {
                ApprovedProviders = approvedProviders,
                CompletedBookings = completedBookings,
                VisibleReviewCount = reviewCount,
                AverageRating = averageRating,
                ServiceCategories = categories.Count
            },
            Categories = categories,
            FeaturedProviders = featured
        };
    }
}
