using System.Globalization;

namespace Servisyo.Web.ViewModels;

/// What the customer typed / clicked: the search box, a category chip, the sort list and the page number.
/// It comes from the address (?q=...&category=...&sort=...&page=...). Nothing here says WHO the customer is.
public class FindServicesQuery
{
    public string? Q { get; set; }
    public int? Category { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
}

/// The ways the provider list can be ordered. The key is what appears in the address.
public sealed record FindServicesSort(string Key, string Label)
{
    public const string RatingKey = "rating";
    public const string ReviewsKey = "reviews";
    public const string PriceKey = "price";
    public const string NameKey = "name";

    public static readonly IReadOnlyList<FindServicesSort> All = new[]
    {
        new FindServicesSort(RatingKey, "Highest Rating"),
        new FindServicesSort(ReviewsKey, "Most Reviews"),
        new FindServicesSort(PriceKey, "Lowest Price"),
        new FindServicesSort(NameKey, "Name (A–Z)")
    };

    /// Anything the browser sends that is not one of the known keys falls back to "Highest Rating".
    public static FindServicesSort Find(string? key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

public class CategoryChipViewModel
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class FindProviderCardViewModel
{
    public const int MaxServiceTags = 3;

    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;            // the person: "Marco Reyes"
    public string Initials { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? CategoryName { get; set; }                   // the badge in the corner
    public string? AvailabilityText { get; set; }               // "Available Today" | "Next: Tomorrow" | "Next: This Week" | null
    public string AvailabilityTone { get; set; } = "completed"; // the .sv-tag--{tone} colour
    public decimal? AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<string> ServiceTags { get; set; } = Array.Empty<string>();
    public int MoreServices { get; set; }
    public decimal? Price { get; set; }
    public string PriceLabel { get; set; } = "Base Fee";        // "Base Fee" | "Starting at"

    public bool HasReviews => ReviewCount > 0 && AvgRating is not null;

    /// 5.0 -> "5", 4.9 -> "4.9"
    public string RatingText => AvgRating is { } r ? r.ToString("0.#", CultureInfo.InvariantCulture) : string.Empty;

    /// How many of the five stars are filled (the rating rounded to the nearest whole star).
    public int FilledStars => AvgRating is { } r ? Math.Clamp((int)Math.Round(r, MidpointRounding.AwayFromZero), 0, 5) : 0;

    public string ReviewsText => ReviewCount == 1 ? "(1 review)" : $"({ReviewCount.ToString("N0", CultureInfo.InvariantCulture)} reviews)";

    public string? PriceText => Price is { } p ? ServiceFormat.Peso(p) : null;

    public string? MoreText => MoreServices > 0 ? $"+{MoreServices} more" : null;

    /// "Rhea's Home Care" and, below it, "Mandaluyong"
    public string? LocationText => string.IsNullOrWhiteSpace(City) ? null : City;
}

public class FindServicesPageViewModel
{
    public const int PageSize = 12;

    public PortalShellViewModel Shell { get; set; } = new() { Role = "Customer" };

    // what is being shown (already cleaned up: unknown sort/category values are gone)
    public string Q { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string Sort { get; set; } = FindServicesSort.RatingKey;

    public int AllCount { get; set; }
    public IReadOnlyList<CategoryChipViewModel> Chips { get; set; } = Array.Empty<CategoryChipViewModel>();
    public IReadOnlyList<FindProviderCardViewModel> Cards { get; set; } = Array.Empty<FindProviderCardViewModel>();

    public int Page { get; set; } = 1;
    public int TotalMatching { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalMatching / (double)PageSize));

    public bool IsFiltered => Q.Length > 0 || CategoryId is not null;

    /// "4 providers found", "1 provider found", "No providers found"
    public string FoundText => TotalMatching switch
    {
        0 => "No providers found",
        1 => "1 provider found",
        _ => $"{TotalMatching.ToString("N0", CultureInfo.InvariantCulture)} providers found"
    };
}

public record FindServicesResult(bool Allowed, FindServicesPageViewModel? Page);
