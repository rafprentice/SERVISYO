using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IFindServicesService
{
    /// The "Find a Service Provider" page for the signed-in customer, or Allowed = false when that user is not an
    /// Active customer (the controller then ends the session).
    Task<FindServicesResult> GetAsync(int userId, FindServicesQuery query, CancellationToken ct = default);
}

public class FindServicesService : IFindServicesService
{
    private const int MaxSearchLength = 100;
    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);     // the Philippines has no daylight saving
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private readonly ServisyoDbContext _db;
    private readonly TimeProvider _clock;

    public FindServicesService(ServisyoDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
    }

    private sealed class Row
    {
        public int ProviderId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? Description { get; set; }
        public decimal? AvgRating { get; set; }
        public int ReviewCount { get; set; }
        public int MinNoticeDays { get; set; }
        public bool HasBaseFee { get; set; }
        public decimal? EffectivePrice { get; set; }
    }

    /// "  plumbing   works " -> "plumbing works" (at most 100 characters)
    private static string CleanTerm(string? q)
    {
        var t = Spaces.Replace((q ?? string.Empty).Trim(), " ");
        return t.Length > MaxSearchLength ? t[..MaxSearchLength].TrimEnd() : t;
    }

    public async Task<FindServicesResult> GetAsync(int userId, FindServicesQuery query, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().ToOffset(ManilaOffset);
        var today = DateOnly.FromDateTime(now.DateTime);
        var nowTime = TimeOnly.FromDateTime(now.DateTime);

        // The customer is found from the SIGNED-IN user id, never from anything the browser sends.
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.UserId == userId
                     && c.User.Role == Roles.Customer
                     && c.User.AccountStatus == AccountStatuses.Active)
            .Select(c => new { c.User.FirstName, c.User.LastName })
            .FirstOrDefaultAsync(ct);
        if (customer is null) return new(false, null);

        var term = CleanTerm(query.Q);
        var lowered = term.ToLowerInvariant();
        var sort = FindServicesSort.Find(query.Sort);

        // ---- who may be shown: Approved provider + Active account + at least one active service (same rule as the home page) ----
        IQueryable<Provider> matching = _db.Providers.AsNoTracking()
            .WhereEligible()
            .Where(p => p.Services.Any(s => s.IsActive));

        if (lowered.Length > 0)
        {
            matching = matching.Where(p =>
                (p.User.FirstName + " " + p.User.LastName).ToLower().Contains(lowered)
                || p.BusinessName.ToLower().Contains(lowered)
                || (p.City != null && p.City.ToLower().Contains(lowered)));
        }

        // ---- category chips: how many of the providers matching the search offer each category ----
        var activeCategories = await _db.ServiceCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CategoryName)
            .Select(c => new { c.CategoryId, c.CategoryName })
            .ToListAsync(ct);

        var matchingIds = matching.Select(p => p.ProviderId);
        var countRows = await _db.Services.AsNoTracking()
            .Where(s => s.IsActive && s.Category.IsActive && matchingIds.Contains(s.ProviderId))
            .GroupBy(s => s.CategoryId)
            .Select(g => new { CategoryId = g.Key, N = g.Select(s => s.ProviderId).Distinct().Count() })
            .ToListAsync(ct);
        var countByCategory = countRows.ToDictionary(r => r.CategoryId, r => r.N);

        var allCount = await matching.CountAsync(ct);

        // a category number that is not an active category is ignored (shows everything)
        int? categoryId = query.Category is { } asked && activeCategories.Any(c => c.CategoryId == asked) ? asked : null;
        var activeCategoryName = activeCategories.FirstOrDefault(c => c.CategoryId == categoryId)?.CategoryName;

        var chips = activeCategories
            .Select(c => new CategoryChipViewModel { CategoryId = c.CategoryId, Name = c.CategoryName, Count = countByCategory.GetValueOrDefault(c.CategoryId) })
            .Where(c => c.Count > 0 || c.CategoryId == categoryId)          // no row of "(0)" chips
            .ToList();

        // ---- the providers on this page ----
        var filtered = categoryId is { } chosen
            ? matching.Where(p => p.Services.Any(s => s.IsActive && s.CategoryId == chosen))
            : matching;

        var total = await filtered.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)FindServicesPageViewModel.PageSize));
        var page = Math.Clamp(query.Page, 1, pageCount);

        var rows = filtered.Select(p => new Row
        {
            ProviderId = p.ProviderId,
            FirstName = p.User.FirstName,
            LastName = p.User.LastName,
            BusinessName = p.BusinessName,
            City = p.City,
            Description = p.Description,
            AvgRating = p.AvgRating,
            ReviewCount = p.ReviewCount,
            MinNoticeDays = p.MinNoticeDays,
            HasBaseFee = p.BaseFee > 0,
            // the price shown on the card: the base fee, or (when there is none) the cheapest active service
            EffectivePrice = p.BaseFee > 0 ? p.BaseFee : p.Services.Where(s => s.IsActive).Min(s => (decimal?)s.Price)
        });

        IOrderedQueryable<Row> ordered = sort.Key switch
        {
            FindServicesSort.ReviewsKey => rows
                .OrderByDescending(r => r.ReviewCount).ThenByDescending(r => r.AvgRating ?? 0m).ThenBy(r => r.BusinessName),
            FindServicesSort.PriceKey => rows
                .OrderBy(r => r.EffectivePrice == null).ThenBy(r => r.EffectivePrice).ThenByDescending(r => r.AvgRating ?? 0m).ThenBy(r => r.BusinessName),
            FindServicesSort.NameKey => rows
                .OrderBy(r => r.FirstName).ThenBy(r => r.LastName).ThenBy(r => r.BusinessName),
            _ => rows
                .OrderByDescending(r => r.AvgRating ?? 0m).ThenByDescending(r => r.ReviewCount).ThenBy(r => r.BusinessName)
        };

        var pageRows = await ordered
            .ThenBy(r => r.ProviderId)                                  // a fixed order for ties, so pages never repeat a provider
            .Skip((page - 1) * FindServicesPageViewModel.PageSize)
            .Take(FindServicesPageViewModel.PageSize)
            .ToListAsync(ct);

        var ids = pageRows.Select(r => r.ProviderId).ToList();

        var serviceRows = await _db.Services.AsNoTracking()
            .Where(s => s.IsActive && ids.Contains(s.ProviderId))
            .OrderBy(s => s.ServiceId)
            .Select(s => new { s.ProviderId, s.ServiceName, CategoryName = s.Category.CategoryName })
            .ToListAsync(ct);
        var servicesByProvider = serviceRows.ToLookup(x => x.ProviderId);

        // "Available Today" hints, worked out the same way as the dashboard's recommended providers
        var scheduleRows = await _db.ProviderAvailabilities.AsNoTracking()
            .Where(a => ids.Contains(a.ProviderId) && a.IsAvailable)
            .Select(a => new { a.ProviderId, a.DayOfWeek, a.EndTime })
            .ToListAsync(ct);
        var lastDay = today.AddDays(AvailabilityHint.LookAheadDays);
        var offRows = await _db.ProviderUnavailableDates.AsNoTracking()
            .Where(u => ids.Contains(u.ProviderId) && u.UnavailableDate >= today && u.UnavailableDate <= lastDay)
            .Select(u => new { u.ProviderId, u.UnavailableDate })
            .ToListAsync(ct);

        var cards = pageRows.Select(r =>
        {
            var fullName = $"{r.FirstName} {r.LastName}".Trim();
            var services = servicesByProvider[r.ProviderId].ToList();

            var endByDay = scheduleRows.Where(s => s.ProviderId == r.ProviderId)
                                       .GroupBy(s => s.DayOfWeek)
                                       .ToDictionary(g => g.Key, g => g.Max(x => x.EndTime));
            var off = offRows.Where(o => o.ProviderId == r.ProviderId).Select(o => o.UnavailableDate).ToHashSet();
            var (availabilityText, availabilityTone) = Describe(AvailabilityHint.Next(r.MinNoticeDays, endByDay, off, today, nowTime));

            return new FindProviderCardViewModel
            {
                ProviderId = r.ProviderId,
                Name = fullName,
                Initials = NameFormat.Initials(fullName),
                BusinessName = r.BusinessName,
                City = string.IsNullOrWhiteSpace(r.City) ? null : r.City.Trim(),
                // when a category is chosen the badge says so; otherwise it shows the provider's first category (A–Z)
                CategoryName = activeCategoryName ?? services.Select(s => s.CategoryName).OrderBy(n => n).FirstOrDefault(),
                AvailabilityText = availabilityText,
                AvailabilityTone = availabilityTone,
                AvgRating = r.ReviewCount > 0 ? r.AvgRating : null,
                ReviewCount = r.ReviewCount,
                Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(),
                ServiceTags = services.Take(FindProviderCardViewModel.MaxServiceTags).Select(s => s.ServiceName).ToList(),
                MoreServices = Math.Max(0, services.Count - FindProviderCardViewModel.MaxServiceTags),
                Price = r.EffectivePrice,
                PriceLabel = r.HasBaseFee ? "Base Fee" : "Starting at"
            };
        }).ToList();

        var fullCustomerName = $"{customer.FirstName} {customer.LastName}".Trim();
        var vm = new FindServicesPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Customer",
                Subtitle = "Customer Account",
                FirstName = customer.FirstName,
                FullName = fullCustomerName,
                Initials = NameFormat.Initials(fullCustomerName),
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            Q = term,
            CategoryId = categoryId,
            Sort = sort.Key,
            AllCount = allCount,
            Chips = chips,
            Cards = cards,
            Page = page,
            TotalMatching = total
        };

        return new(true, vm);
    }

    /// The dashboard's hint says "Available Today / Tomorrow / This Week"; here the later ones read "Next: ...".
    private static (string? Text, string Tone) Describe(string? hint) => hint switch
    {
        "Available Today" => ("Available Today", "completed"),
        "Available Tomorrow" => ("Next: Tomorrow", "pending"),
        "Available This Week" => ("Next: This Week", "pending"),
        _ => (null, "muted")
    };
}
