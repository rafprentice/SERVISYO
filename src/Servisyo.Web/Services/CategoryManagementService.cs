using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public enum CategoryOutcome { Success, Invalid, Duplicate, NotFound, NoChange, NotAllowed }

public sealed record CategoryResult(CategoryOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == CategoryOutcome.Success;
}

public interface ICategoryManagementService
{
    /// The category list for the admin. Returns null when the signed-in user is not an Active administrator.
    Task<CategoriesPageViewModel?> GetCategoriesAsync(int adminUserId, CancellationToken ct = default);

    Task<CategoryResult> CreateAsync(int adminUserId, string? name, string? description, CancellationToken ct = default);
    Task<CategoryResult> UpdateAsync(int adminUserId, int categoryId, string? name, string? description, CancellationToken ct = default);
    Task<CategoryResult> SetActiveAsync(int adminUserId, int categoryId, bool enable, CancellationToken ct = default);
}

public class CategoryManagementService : ICategoryManagementService
{
    public const int MaxNameLength = 100;          // SERVICE_CATEGORIES.CATEGORY_NAME
    public const int MaxDescriptionLength = 255;   // SERVICE_CATEGORIES.DESCRIPTION

    private readonly ServisyoDbContext _db;
    private readonly ILogger<CategoryManagementService> _logger;

    public CategoryManagementService(ServisyoDbContext db, ILogger<CategoryManagementService>? logger = null)
    {
        _db = db;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CategoryManagementService>.Instance;
    }

    // ------------------------------------------------------------------ read

    public async Task<CategoriesPageViewModel?> GetCategoriesAsync(int adminUserId, CancellationToken ct = default)
    {
        var shell = await AdminShell.BuildAsync(_db, adminUserId, ct);
        if (shell is null) return null;

        var categories = await _db.ServiceCategories.AsNoTracking()
            .OrderByDescending(c => c.IsActive).ThenBy(c => c.CategoryId)      // active first, then in the order they were created
            .Select(c => new { c.CategoryId, c.CategoryName, c.Description, c.IsActive })
            .ToListAsync(ct);

        // Providers: approved providers that picked the category (one row per provider+category is guaranteed by a unique index)
        var providerCounts = (await _db.ProviderCategories.AsNoTracking()
                .Where(pc => pc.Provider.ApprovalStatus == ApprovalStatuses.Approved)
                .GroupBy(pc => pc.CategoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.CategoryId, x => x.Count);

        // Bookings: bookings that contain at least one service of the category (a booking counts once per category)
        var bookingCounts = (await _db.BookingServices.AsNoTracking()
                .GroupBy(bs => bs.Service.CategoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Select(x => x.BookingId).Distinct().Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.CategoryId, x => x.Count);

        return new CategoriesPageViewModel
        {
            Shell = shell,
            ActiveCount = categories.Count(c => c.IsActive),
            DisabledCount = categories.Count(c => !c.IsActive),
            Rows = categories.Select(c => new CategoryRowViewModel
            {
                CategoryId = c.CategoryId,
                Name = c.CategoryName,
                Description = c.Description,
                IsActive = c.IsActive,
                Providers = providerCounts.GetValueOrDefault(c.CategoryId),
                Bookings = bookingCounts.GetValueOrDefault(c.CategoryId)
            }).ToList()
        };
    }

    // ----------------------------------------------------------------- write

    public async Task<CategoryResult> CreateAsync(int adminUserId, string? name, string? description, CancellationToken ct = default)
    {
        if (!await IsActiveAdminAsync(adminUserId, ct)) return NotAllowed();

        var (cleanName, cleanDescription, problem) = Validate(name, description);
        if (problem is not null) return new(CategoryOutcome.Invalid, problem);

        if (await NameTakenAsync(cleanName, exceptCategoryId: null, ct))
            return Duplicate(cleanName);

        var category = new ServiceCategory { CategoryName = cleanName, Description = cleanDescription, IsActive = true };
        try
        {
            await using var tx = await BeginAsync(ct);
            _db.ServiceCategories.Add(category);
            await _db.SaveChangesAsync(ct);                       // gives the category its id

            _db.AdminActions.Add(Log(adminUserId, "Category created", category.CategoryId, cleanName));
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // two admins adding the same name at the same moment: the unique index on CATEGORY_NAME stops the second one
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Category '{Name}' could not be saved (probably a duplicate).", cleanName);
            return Duplicate(cleanName);
        }

        _logger.LogInformation("Admin {AdminUserId} created category {CategoryId}.", adminUserId, category.CategoryId);
        return new(CategoryOutcome.Success, $"Category \"{cleanName}\" was added.");
    }

    public async Task<CategoryResult> UpdateAsync(int adminUserId, int categoryId, string? name, string? description, CancellationToken ct = default)
    {
        if (!await IsActiveAdminAsync(adminUserId, ct)) return NotAllowed();

        var (cleanName, cleanDescription, problem) = Validate(name, description);
        if (problem is not null) return new(CategoryOutcome.Invalid, problem);

        var category = await _db.ServiceCategories.FirstOrDefaultAsync(c => c.CategoryId == categoryId, ct);
        if (category is null) return NotFound();

        if (await NameTakenAsync(cleanName, exceptCategoryId: categoryId, ct))
            return Duplicate(cleanName);

        if (category.CategoryName == cleanName && category.Description == cleanDescription)
            return new(CategoryOutcome.NoChange, "Nothing was changed.");

        try
        {
            category.CategoryName = cleanName;
            category.Description = cleanDescription;
            _db.AdminActions.Add(Log(adminUserId, "Category updated", category.CategoryId, cleanName));
            await _db.SaveChangesAsync(ct);                       // category + log entry in one transaction
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Category {CategoryId} could not be updated (probably a duplicate name).", categoryId);
            return Duplicate(cleanName);
        }

        _logger.LogInformation("Admin {AdminUserId} updated category {CategoryId}.", adminUserId, categoryId);
        return new(CategoryOutcome.Success, $"Category \"{cleanName}\" was updated.");
    }

    public async Task<CategoryResult> SetActiveAsync(int adminUserId, int categoryId, bool enable, CancellationToken ct = default)
    {
        if (!await IsActiveAdminAsync(adminUserId, ct)) return NotAllowed();

        var category = await _db.ServiceCategories.FirstOrDefaultAsync(c => c.CategoryId == categoryId, ct);
        if (category is null) return NotFound();

        if (category.IsActive == enable)
            return new(CategoryOutcome.NoChange, $"\"{category.CategoryName}\" is already {(enable ? "active" : "disabled")}.");

        category.IsActive = enable;
        _db.AdminActions.Add(Log(adminUserId, enable ? "Category enabled" : "Category disabled", category.CategoryId, null));
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin {AdminUserId} {Verb} category {CategoryId}.", adminUserId, enable ? "enabled" : "disabled", categoryId);
        return new(CategoryOutcome.Success, $"Category \"{category.CategoryName}\" was {(enable ? "enabled" : "disabled")}.");
    }

    // --------------------------------------------------------------- helpers

    private Task<bool> IsActiveAdminAsync(int userId, CancellationToken ct) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.UserId == userId
                                            && u.Role == Roles.Administrator
                                            && u.AccountStatus == AccountStatuses.Active, ct);

    /// Trims the text and checks the lengths. Returns a message in `problem` when something is wrong.
    private static (string Name, string? Description, string? Problem) Validate(string? name, string? description)
    {
        var n = (name ?? string.Empty).Trim();
        var d = (description ?? string.Empty).Trim();

        if (n.Length == 0) return (n, null, "Please enter a category name.");
        if (n.Length > MaxNameLength) return (n, null, $"The category name can be at most {MaxNameLength} characters.");
        if (d.Length > MaxDescriptionLength) return (n, null, $"The description can be at most {MaxDescriptionLength} characters.");
        return (n, d.Length == 0 ? null : d, null);
    }

    // SQL Server compares names without caring about upper/lower case, so we do the same here.
    private Task<bool> NameTakenAsync(string name, int? exceptCategoryId, CancellationToken ct)
    {
        var lower = name.ToLower();
        return _db.ServiceCategories.AnyAsync(c => c.CategoryName.ToLower() == lower
                                                && (exceptCategoryId == null || c.CategoryId != exceptCategoryId), ct);
    }

    // Real SQL Server gets a transaction (category + log entry succeed or fail together); the in-memory test database has none.
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
        _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;

    // Same log format as the other admin actions: ACTION_TYPE says what happened, TARGET_TYPE says to what.
    private static AdminAction Log(int adminUserId, string actionType, int categoryId, string? reason) => new()
    {
        AdminUserId = adminUserId, ActionType = actionType, TargetType = "CATEGORY", TargetId = categoryId, Reason = reason
    };

    private static CategoryResult NotAllowed() =>
        new(CategoryOutcome.NotAllowed, "Your administrator account is not allowed to do this.");

    private static CategoryResult NotFound() =>
        new(CategoryOutcome.NotFound, "That category no longer exists.");

    private static CategoryResult Duplicate(string name) =>
        new(CategoryOutcome.Duplicate, $"A category named \"{name}\" already exists.");
}
