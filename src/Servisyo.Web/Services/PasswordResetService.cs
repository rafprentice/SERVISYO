using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

public enum PasswordResetStatus { Ok, InvalidCode, TooManyAttempts }

public interface IPasswordResetService
{
    /// Emails a reset code when the address belongs to an Active account. Does NOTHING visible otherwise:
    /// the page always gives the same answer, so nobody can use it to find out who is registered.
    Task RequestResetAsync(string? email);

    /// Checks the code and, if it is right, saves the new password (hashed).
    Task<PasswordResetStatus> ResetPasswordAsync(string? email, string? code, string newPassword);
}

public class PasswordResetService : IPasswordResetService
{
    private readonly ServisyoDbContext _db;
    private readonly IOtpService _otp;
    private readonly IEmailService _email;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(ServisyoDbContext db, IOtpService otp, IEmailService email,
        IPasswordHasher<User> hasher, ILogger<PasswordResetService>? logger = null)
    {
        _db = db;
        _otp = otp;
        _email = email;
        _hasher = hasher;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PasswordResetService>.Instance;
    }

    public async Task RequestResetAsync(string? email)
    {
        var user = await FindActiveUserAsync(email);
        if (user is null)
        {
            _logger.LogInformation("Password reset asked for an address that has no active account.");
            return;
        }

        var issued = await _otp.IssueAsync(user, OtpPurposes.PasswordReset);
        if (issued.Status == OtpIssueStatus.Throttled)
        {
            _logger.LogInformation("Password reset code not re-sent to user {UserId}: asked again within {Seconds}s.", user.UserId, OtpService.ResendCooldownSeconds);
            return;
        }

        var mail = EmailTemplates.PasswordReset(issued.Code!, OtpService.ValidMinutes);
        var sent = await _email.SendAsync(user.Email, "Your SERVISYO password reset code", mail.Html, mail.Text);
        if (!sent) _logger.LogError("The password reset email for user {UserId} could not be sent.", user.UserId);
    }

    public async Task<PasswordResetStatus> ResetPasswordAsync(string? email, string? code, string newPassword)
    {
        var user = await FindActiveUserAsync(email);
        if (user is null)
        {
            _hasher.HashPassword(new User(), code ?? string.Empty);   // similar work as a real check
            return PasswordResetStatus.InvalidCode;
        }

        var status = await _otp.VerifyAsync(user, OtpPurposes.PasswordReset, code);
        if (status == OtpVerifyStatus.TooManyAttempts) return PasswordResetStatus.TooManyAttempts;
        if (status != OtpVerifyStatus.Ok) return PasswordResetStatus.InvalidCode;

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.EmailVerificationPending = false;     // reading the emailed code also proves the address is theirs
        await _db.SaveChangesAsync();

        _logger.LogInformation("Password reset completed for user {UserId}.", user.UserId);
        return PasswordResetStatus.Ok;
    }

    private async Task<User?> FindActiveUserAsync(string? email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == normalized && u.AccountStatus == AccountStatuses.Active);
    }
}
