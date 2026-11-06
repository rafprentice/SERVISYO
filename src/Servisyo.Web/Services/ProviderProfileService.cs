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
                x.ProviderId, x.ApprovalStatus, x.BusinessName, x.Address, x.Description, x.ProfilePhoto, x.BaseFee,
                x.User.FirstName, x.User.MiddleName, x.User.LastName, x.User.Email, x.User.PhoneNumber
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
            !string.IsNullOrWhiteSpace(p.Address),
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
                UnreadNotifications = await _db.CountUnreadNotificationsAsync(userId, ct),
                UnreadMessages = await _db.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct)
            },
            FullName = fullName,
            Initials = NameFormat.Initials(fullName),
            PhotoUrl = string.IsNullOrWhiteSpace(p.ProfilePhoto) ? null : p.ProfilePhoto,
            InfoLocked = ProviderLockRules.IsLocked(p.ApprovalStatus),
            BusinessName = p.BusinessName,
            StrengthPercent = percent,
            StrengthHint = nextStep is null ? "Your profile is complete." : nextStep + " to reach 100%",
            // the locked values always come from the database; after a refused save, Email and Bio show what was typed
            Form = new ProviderProfileForm
            {
                FirstName = p.FirstName,
                MiddleName = p.MiddleName,
                LastName = p.LastName,
                BusinessName = p.BusinessName,
                PhoneNumber = p.PhoneNumber,
                Address = p.Address,
                BaseFee = p.BaseFee,
                Email = posted is null ? p.Email : posted.Email,
                Description = posted is null ? p.Description : posted.Description
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

        // ---- an approved provider's core information is locked: refuse any attempt to change it (nothing is saved) ----
        // (only Approved providers get this far, so for them only Email and About / Bio can change below)
        if (ProviderLockRules.IsLocked(provider.ApprovalStatus))
        {
            var changed = ProviderLockRules.ChangedFields(form, provider);
            if (changed.Count > 0)
            {
                _logger.LogWarning("Provider {ProviderId} tried to change locked profile fields: {Fields}. Refused.",
                    provider.ProviderId, string.Join(", ", changed));
                return new(access, false, changed.ToDictionary(f => f, _ => ProviderLockRules.FieldLockedMessage),
                    ProviderLockRules.ApprovedNotice, LockViolation: true);
            }
        }

        var errors = new Dictionary<string, string>();

        // ---- email (it is also the sign-in name, so it must stay unique) ----
        var email = (form.Email ?? string.Empty).Trim().ToLowerInvariant();
        var emailChanged = !string.Equals(email, user.Email.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        if (email.Length == 0)
            errors["Email"] = "Enter your email address.";
        else if (emailChanged && email.Length > ProfileRules.MaxEmailLength)
            errors["Email"] = $"The email can be at most {ProfileRules.MaxEmailLength} characters.";
        else if (emailChanged && !InputRules.IsEmail(email))
            errors["Email"] = InputRules.EmailMessage;                           // no numbers after the @, parts of 2+ letters
        else if (emailChanged && await EmailUsedByAnotherAsync(email, user.UserId, ct))
            errors["Email"] = EmailTaken;

        // ---- About / Bio (optional) ----
        var description = ProfileRules.NormalizeText(form.Description);
        if (description is { Length: > ProfileRules.MaxDescriptionLength })
            errors["Description"] = $"The bio can be at most {ProfileRules.MaxDescriptionLength} characters.";

        if (errors.Count > 0)
        {
            _logger.LogInformation("Provider {ProviderId}: profile not saved, {Count} field(s) need attention.", provider.ProviderId, errors.Count);
            return new(access, false, errors, "Please fix the highlighted fields.");
        }

        // ---- everything is fine: only the email and the bio change ----
        if (emailChanged) user.Email = email;
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
