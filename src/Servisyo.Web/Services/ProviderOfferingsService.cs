using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IProviderOfferingsService
{
    /// The My Services page for the signed-in provider. When the Add dialog was rejected, pass what was typed (and the
    /// result) so the dialog opens again with the same text and the problems.
    Task<ServicesPageResult> GetAsync(int userId, AddServiceForm? posted = null, AddServiceResult? rejected = null, CancellationToken ct = default);

    /// Adds a service (same rules as usp_AddService in the PDF) and links the provider to its category.
    Task<AddServiceResult> AddAsync(int userId, AddServiceForm form, CancellationToken ct = default);

    /// Activates or deactivates one of the signed-in provider's own services.
    Task<ServiceStatusResult> SetActiveAsync(int userId, int serviceId, bool active, CancellationToken ct = default);
}

public class ProviderOfferingsService : IProviderOfferingsService
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 255;
    public const decimal MaxPrice = 99_999_999.99m;          // the database column is DECIMAL(10,2)

    private readonly ServisyoDbContext _db;
    private readonly ILogger<ProviderOfferingsService> _logger;

    public ProviderOfferingsService(ServisyoDbContext db, ILogger<ProviderOfferingsService>? logger = null)
    {
        _db = db;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderOfferingsService>.Instance;
    }

    private sealed record Who(int ProviderId, string FullName, string BusinessName);

    /// The provider is found from the SIGNED-IN user id only (never from the browser).
    /// Only an Active account with an Approved application may manage services (usp_AddService in the PDF).
    private async Task<(ProviderDashboardAccess Access, Who? Who)> FindAsync(int userId, CancellationToken ct)
    {
        var p = await _db.Providers.AsNoTracking()
            .Where(x => x.UserId == userId
                     && x.User.Role == Roles.ServiceProvider
                     && x.User.AccountStatus == AccountStatuses.Active)
            .Select(x => new { x.ProviderId, x.ApprovalStatus, x.User.FirstName, x.User.LastName, x.BusinessName })
            .FirstOrDefaultAsync(ct);

        if (p is null) return (ProviderDashboardAccess.NotAllowed, null);
        if (p.ApprovalStatus == ApprovalStatuses.Pending) return (ProviderDashboardAccess.Pending, null);
        if (p.ApprovalStatus != ApprovalStatuses.Approved) return (ProviderDashboardAccess.Rejected, null);

        return (ProviderDashboardAccess.Allowed, new Who(p.ProviderId, $"{p.FirstName} {p.LastName}".Trim(), p.BusinessName));
    }

    // ------------------------------------------------------------------ read

    public async Task<ServicesPageResult> GetAsync(int userId, AddServiceForm? posted = null, AddServiceResult? rejected = null, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, null);

        // active services first, then in the order they were added
        var rows = await _db.Services.AsNoTracking()
            .Where(s => s.ProviderId == who.ProviderId)
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.ServiceId)
            .Select(s => new
            {
                s.ServiceId, s.ServiceName, s.Description, s.Price, s.DurationMinutes, s.IsActive,
                Category = s.Category.CategoryName
            })
            .ToListAsync(ct);

        var categories = await _db.ServiceCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CategoryName)
            .Select(c => new CategoryChoice(c.CategoryId, c.CategoryName))
            .ToListAsync(ct);

        // a duration saved earlier that is not in the usual list is still offered while it is selected
        var durations = ServiceDurations.Choices.AsEnumerable();
        if (posted?.DurationMinutes is { } typed && typed > 0 && !ServiceDurations.Choices.Contains(typed))
            durations = durations.Append(typed).OrderBy(x => x);

        var fullName = who.FullName;
        return new(ProviderDashboardAccess.Allowed, new ServicesPageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Provider",
                FirstName = fullName.Split(' ')[0],
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                Subtitle = who.BusinessName,
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            Cards = rows.Select(r => new ServiceCardViewModel
            {
                ServiceId = r.ServiceId,
                Name = r.ServiceName,
                Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(),
                Price = r.Price,
                DurationMinutes = r.DurationMinutes,
                CategoryName = r.Category,
                IsActive = r.IsActive
            }).ToList(),
            Categories = categories,
            DurationOptions = durations.Select(m => (m, ServiceFormat.Duration(m))).ToList(),
            ShowAddDialog = posted is not null,
            Form = posted ?? new AddServiceForm(),
            FieldErrors = rejected?.FieldErrors ?? new Dictionary<string, string>(),
            FormError = rejected is { Added: false } && rejected.FieldErrors.Count == 0 ? rejected.Message : null
        });
    }

    // ----------------------------------------------------------------- write

    public async Task<AddServiceResult> AddAsync(int userId, AddServiceForm form, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, false, new Dictionary<string, string>(), string.Empty);

        var errors = new Dictionary<string, string>();

        // ---- name ----
        var name = form.ServiceName?.Trim() ?? string.Empty;
        if (name.Length == 0)
            errors["ServiceName"] = "Enter a service name.";
        else if (name.Length > MaxNameLength)
            errors["ServiceName"] = $"The name can be at most {MaxNameLength} characters.";

        // ---- description (optional) ----
        var description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
            errors["Description"] = $"The description can be at most {MaxDescriptionLength} characters.";

        // ---- category: must exist and be active (usp_AddService) ----
        int categoryId = 0;
        if (form.CategoryId is null or <= 0)
        {
            errors["CategoryId"] = "Choose a category.";
        }
        else
        {
            categoryId = form.CategoryId.Value;
            var usable = await _db.ServiceCategories.AsNoTracking().AnyAsync(c => c.CategoryId == categoryId && c.IsActive, ct);
            if (!usable) errors["CategoryId"] = "That category is not available. Choose another one.";
        }

        // ---- price: 0 or more, at most 2 decimals, fits DECIMAL(10,2) ----
        decimal price = 0;
        if (form.Price is null)
            errors["Price"] = "Enter a price, for example 800.";
        else if (form.Price < 0)
            errors["Price"] = "The price cannot be negative.";
        else if (form.Price > MaxPrice)
            errors["Price"] = "That price is too large.";
        else if (decimal.Round(form.Price.Value, 2) != form.Price.Value)
            errors["Price"] = "Use at most 2 decimal places.";
        else
            price = form.Price.Value;

        // ---- duration ----
        var minutes = form.DurationMinutes ?? 0;
        if (!ServiceDurations.Choices.Contains(minutes))
            errors["DurationMinutes"] = "Choose how long the service usually takes.";

        // ---- the same provider cannot have two services with the same name ----
        if (!errors.ContainsKey("ServiceName"))
        {
            var lower = name.ToLowerInvariant();
            var duplicate = await _db.Services.AsNoTracking()
                .AnyAsync(s => s.ProviderId == who.ProviderId && s.ServiceName.ToLower() == lower, ct);
            if (duplicate) errors["ServiceName"] = "You already have a service with this name.";
        }

        if (errors.Count > 0)
            return new(ProviderDashboardAccess.Allowed, false, errors, "The service was not added. Please fix the highlighted fields.");

        try
        {
            _db.Services.Add(new Service
            {
                ProviderId = who.ProviderId,
                CategoryId = categoryId,
                ServiceName = name,
                Description = description,
                Price = price,
                DurationMinutes = minutes,
                IsActive = true
            });

            // Like usp_AddService: link the provider to the category if that has not happened yet.
            // (A unique index stops duplicates.)
            var linked = await _db.ProviderCategories.AnyAsync(pc => pc.ProviderId == who.ProviderId && pc.CategoryId == categoryId, ct);
            if (!linked)
                _db.ProviderCategories.Add(new ProviderCategory { ProviderId = who.ProviderId, CategoryId = categoryId });

            await _db.SaveChangesAsync(ct);       // service + category link: one database transaction
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Could not add service '{Name}' for provider {ProviderId}.", name, who.ProviderId);
            return new(ProviderDashboardAccess.Allowed, false, new Dictionary<string, string>(),
                "The service could not be saved. Please try again.");
        }

        _logger.LogInformation("Provider {ProviderId} added service '{Name}' in category {CategoryId}.", who.ProviderId, name, categoryId);
        return new(ProviderDashboardAccess.Allowed, true, new Dictionary<string, string>(), $"“{name}” was added to your services.");
    }

    public async Task<ServiceStatusResult> SetActiveAsync(int userId, int serviceId, bool active, CancellationToken ct = default)
    {
        var (access, who) = await FindAsync(userId, ct);
        if (who is null) return new(access, ServiceStatusOutcome.NotFound, string.Empty);

        // The service must belong to THIS provider. Someone else's service looks exactly like a missing one.
        var service = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == serviceId && s.ProviderId == who.ProviderId, ct);
        if (service is null)
            return new(ProviderDashboardAccess.Allowed, ServiceStatusOutcome.NotFound, "That service no longer exists.");

        if (service.IsActive == active)
            return new(ProviderDashboardAccess.Allowed, ServiceStatusOutcome.NoChange,
                $"“{service.ServiceName}” is already {(active ? "active" : "inactive")}.");

        service.IsActive = active;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Provider {ProviderId} set service {ServiceId} to {State}.",
            who.ProviderId, serviceId, active ? "active" : "inactive");

        return new(ProviderDashboardAccess.Allowed, ServiceStatusOutcome.Success, active
            ? $"“{service.ServiceName}” is active again and customers can see it."
            : $"“{service.ServiceName}” was deactivated. Customers can no longer see or book it.");
    }
}
