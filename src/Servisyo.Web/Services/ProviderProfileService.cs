using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.ViewModels;

namespace Servisyo.Web.Services;

public interface IProviderProfileService
{
    /// The My Profile page for the signed-in provider. When a save was rejected, pass what was typed (and the result)
    /// so the form shows the same text again together with the problems.
    Task<ProviderProfilePageResult> GetAsync(int userId, ProviderProfileForm? posted = null, ProviderProfileSaveResult? rejected = null, CancellationToken ct = default);

    /// Saves the Business Information form for the signed-in provider. Everything is checked first;
    /// if anything is wrong nothing at all is saved.
    Task<ProviderProfileSaveResult> SaveAsync(int userId, ProviderProfileForm form, CancellationToken ct = default);
}

public class ProviderProfileService : IProviderProfileService
{
    private const int ReviewsShown = 3;
    private const int ManilaOffsetHours = 8;          // the Philippines has no daylight saving

    private const string EmailTaken = "That email address is already used by another account.";

    private readonly ServisyoDbContext _db;
    private readonly ILogger<ProviderProfileService> _logger;

    public ProviderProfileService(ServisyoDbContext db, ILogger<ProviderProfileService>? logger = null)
    {
        _db = db;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderProfileService>.Instance;
    }

    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();

    /// Only an Active account with an Approved application may edit a provider profile.
    private static ProviderDashboardAccess AccessOf(string? approvalStatus) => approvalStatus switch
    {
        null => ProviderDashboardAccess.NotAllowed,
        ApprovalStatuses.Pending => ProviderDashboardAccess.Pending,
        ApprovalStatuses.Approved => ProviderDashboardAccess.Allowed,
        _ => ProviderDashboardAccess.Rejected
    };

    // ------------------------------------------------------------------ read

    public async Task<ProviderProfilePageResult> GetAsync(int userId, ProviderProfileForm? posted = null, ProviderProfileSaveResult? rejected = null, CancellationToken ct = default)
    {
        // The provider is found from the SIGNED-IN user id only (never from the browser).
        var p = await _db.Providers.AsNoTracking()
            .Where(x => x.UserId == userId
                     && x.User.Role == Roles.ServiceProvider
                     && x.User.AccountStatus == AccountStatuses.Active)
            .Select(x => new
            {
                x.ProviderId, x.ApprovalStatus, x.BusinessName, x.City, x.Address, x.Description, x.ProfilePhoto,
                x.User.FirstName, x.User.LastName, x.User.Email, x.User.PhoneNumber
            })
            .FirstOrDefaultAsync(ct);

        var access = AccessOf(p?.ApprovalStatus);
        if (p is null || access != ProviderDashboardAccess.Allowed) return new(access, null);

        var serviceNames = await _db.Services.AsNoTracking()
            .Where(s => s.ProviderId == p.ProviderId && s.IsActive)
            .OrderBy(s => s.ServiceId)
            .Select(s => s.ServiceName)
            .ToListAsync(ct);

        var hasAvailability = await _db.ProviderAvailabilities.AnyAsync(a => a.ProviderId == p.ProviderId && a.IsAvailable, ct);

        // the same six checks as the dashboard's Profile Strength card
        var (percent, nextStep) = ProfileStrength.Calculate(
            serviceNames.Count > 0, hasAvailability,
            !string.IsNullOrWhiteSpace(p.Description),
            !string.IsNullOrWhiteSpace(p.ProfilePhoto),
            !string.IsNullOrWhiteSpace(p.Address) && !string.IsNullOrWhiteSpace(p.City),
            !string.IsNullOrWhiteSpace(p.PhoneNumber));

        var reviewRows = await _db.Reviews.AsNoTracking()
            .Where(r => r.Status == ReviewStatuses.Visible && r.Booking.ProviderId == p.ProviderId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ReviewId)
            .Take(ReviewsShown)
            .Select(r => new
            {
                r.Rating, r.Comment, r.CreatedAt,
                First = r.Booking.Customer.User.FirstName,
                Last = r.Booking.Customer.User.LastName
            })
            .ToListAsync(ct);

        var fullName = $"{p.FirstName} {p.LastName}".Trim();
        var vm = new ProviderProfilePageViewModel
        {
            Shell = new PortalShellViewModel
            {
                Role = "Provider",
                FirstName = p.FirstName,
                FullName = fullName,
                Initials = NameFormat.Initials(fullName),
                Subtitle = p.BusinessName,
                UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            FullName = fullName,
            Initials = NameFormat.Initials(fullName),
            BusinessName = p.BusinessName,
            City = string.IsNullOrWhiteSpace(p.City) ? null : p.City.Trim(),
            StrengthPercent = percent,
            StrengthHint = nextStep is null ? "Your profile is complete." : nextStep + " to reach 100%",
            Form = posted ?? new ProviderProfileForm
            {
                FullName = fullName,
                BusinessName = p.BusinessName,
                PhoneNumber = p.PhoneNumber,
                Email = p.Email,
                City = p.City,
                Address = p.Address,
                Description = p.Description
            },
            FieldErrors = rejected?.FieldErrors ?? NoErrors,
            FormError = rejected is { Saved: false } && rejected.FieldErrors.Count == 0 ? rejected.Message : null,
            ServiceNames = serviceNames,
            Reviews = reviewRows.Select(r => new ReviewItemViewModel
            {
                Rating = r.Rating,
                Comment = r.Comment ?? string.Empty,
                Author = $"{r.First} {r.Last}".Trim(),
                Date = DateOnly.FromDateTime(r.CreatedAt.AddHours(ManilaOffsetHours))      // stored in UTC, shown in Cebu time
            }).ToList()
        };

        return new(ProviderDashboardAccess.Allowed, vm);
    }

    // ----------------------------------------------------------------- write

    public async Task<ProviderProfileSaveResult> SaveAsync(int userId, ProviderProfileForm form, CancellationToken ct = default)
    {
        // The provider (and the user account that belongs to it) is found from the SIGNED-IN user id only.
        var provider = await _db.Providers.Include(x => x.User)
            .FirstOrDefaultAsync(x => x.UserId == userId
                                   && x.User.Role == Roles.ServiceProvider
                                   && x.User.AccountStatus == AccountStatuses.Active, ct);

        var access = AccessOf(provider?.ApprovalStatus);
        if (provider is null || access != ProviderDashboardAccess.Allowed)
            return new(access, false, NoErrors, string.Empty);

        var user = provider.User;
        var errors = new Dictionary<string, string>();

        // ---- full name (stored as first + last name; only re-split when the provider actually changed it) ----
        var fullName = ProfileRules.NormalizeName(form.FullName);
        var currentName = ProfileRules.NormalizeName($"{user.FirstName} {user.LastName}");
        var nameChanged = !string.Equals(fullName, currentName, StringComparison.Ordinal);
        if (fullName.Length == 0)
            errors["FullName"] = "Enter your full name.";
        else if (nameChanged && fullName.Length > ProfileRules.MaxFullNameLength)
            errors["FullName"] = $"The name can be at most {ProfileRules.MaxFullNameLength} characters.";
        else if (nameChanged && !fullName.Contains(' '))
            errors["FullName"] = "Enter your first and last name.";

        // ---- business name ----
        var businessName = (form.BusinessName ?? string.Empty).Trim();
        if (businessName.Length == 0)
            errors["BusinessName"] = "Enter your business name.";
        else if (businessName.Length > ProfileRules.MaxBusinessNameLength)
            errors["BusinessName"] = $"The business name can be at most {ProfileRules.MaxBusinessNameLength} characters.";

        // ---- phone (optional; a number saved earlier in another style is left alone unless it is edited) ----
        var typedPhone = (form.PhoneNumber ?? string.Empty).Trim();
        var phoneChanged = typedPhone != (user.PhoneNumber ?? string.Empty).Trim();
        var phone = ProfileRules.NormalizePhone(typedPhone);
        if (phoneChanged && phone is not null && !ProfileRules.IsValidPhone(phone))
            errors["PhoneNumber"] = "Enter a valid phone number, for example +63 917 555 1234.";

        // ---- email (it is also the sign-in name, so it must stay unique) ----
        var email = (form.Email ?? string.Empty).Trim().ToLowerInvariant();
        var emailChanged = !string.Equals(email, user.Email.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        if (email.Length == 0)
            errors["Email"] = "Enter your email address.";
        else if (emailChanged && email.Length > ProfileRules.MaxEmailLength)
            errors["Email"] = $"The email can be at most {ProfileRules.MaxEmailLength} characters.";
        else if (emailChanged && (email.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(email)))
            errors["Email"] = "Enter a valid email address.";
        else if (emailChanged && await EmailUsedByAnotherAsync(email, user.UserId, ct))
            errors["Email"] = EmailTaken;

        // ---- city, address, bio (all optional) ----
        var city = ProfileRules.NormalizeText(form.City);
        if (city is { Length: > ProfileRules.MaxCityLength })
            errors["City"] = $"The city can be at most {ProfileRules.MaxCityLength} characters.";

        var address = ProfileRules.NormalizeText(form.Address);
        if (address is { Length: > ProfileRules.MaxAddressLength })
            errors["Address"] = $"The address can be at most {ProfileRules.MaxAddressLength} characters.";

        var description = ProfileRules.NormalizeText(form.Description);
        if (description is { Length: > ProfileRules.MaxDescriptionLength })
            errors["Description"] = $"The bio can be at most {ProfileRules.MaxDescriptionLength} characters.";

        if (errors.Count > 0)
        {
            _logger.LogInformation("Provider {ProviderId}: profile not saved, {Count} field(s) need attention.", provider.ProviderId, errors.Count);
            return new(access, false, errors, "Please fix the highlighted fields.");
        }

        // ---- everything is fine: change the account and the provider profile together ----
        if (nameChanged)
        {
            var (first, last) = ProfileRules.SplitName(fullName);
            user.FirstName = first;
            user.LastName = last;               // the middle name, if there is one, is left as it was
        }
        if (emailChanged) user.Email = email;
        if (phoneChanged) user.PhoneNumber = phone;

        provider.BusinessName = businessName;
        provider.City = city;
        provider.Address = address;
        provider.Description = description;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (emailChanged)
        {
            // someone registered the same email between our check and the save: the unique index in the database caught it
            _db.ChangeTracker.Clear();
            if (await EmailUsedByAnotherAsync(email, user.UserId, ct))
            {
                _logger.LogWarning("Provider {ProviderId}: email became taken while saving the profile.", provider.ProviderId);
                return new(access, false, new Dictionary<string, string> { ["Email"] = EmailTaken }, "Please fix the highlighted fields.");
            }
            throw;
        }

        _logger.LogInformation("Provider {ProviderId} updated their profile.", provider.ProviderId);
        return new(access, true, NoErrors, "Your profile was updated.", $"{user.FirstName} {user.LastName}".Trim(), user.Email);
    }

    private Task<bool> EmailUsedByAnotherAsync(string normalizedEmail, int userId, CancellationToken ct) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.UserId != userId && u.Email == normalizedEmail, ct);
}
