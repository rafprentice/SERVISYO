namespace Servisyo.Web.ViewModels;

public class HomePageViewModel
{
    public HomeStatsViewModel Stats { get; set; } = new();
    public IReadOnlyList<CategoryCardViewModel> Categories { get; set; } = Array.Empty<CategoryCardViewModel>();
    public IReadOnlyList<FeaturedProviderViewModel> FeaturedProviders { get; set; } = Array.Empty<FeaturedProviderViewModel>();

    /// True when the database could not be read. The page still renders, with a notice.
    public bool LoadFailed { get; set; }
}

/// Every number here comes from the database. Nothing is hardcoded.
public class HomeStatsViewModel
{
    public int ApprovedProviders { get; set; }
    public int CompletedBookings { get; set; }
    public int VisibleReviewCount { get; set; }
    /// Null when there are no visible reviews yet (the page shows a dash instead of a made-up rating).
    public double? AverageRating { get; set; }
    public int ServiceCategories { get; set; }
}

public class CategoryCardViewModel
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ProviderCount { get; set; }
}

public class FeaturedProviderViewModel
{
    public int ProviderId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Description { get; set; }
    public decimal? AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public decimal? StartingPrice { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    public string Initials
    {
        get
        {
            var parts = BusinessName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
            return (parts[0][..1] + parts[1][..1]).ToUpperInvariant();
        }
    }
}
