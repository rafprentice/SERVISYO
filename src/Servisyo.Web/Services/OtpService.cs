using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

public static class OtpPurposes
{
    public const string PasswordReset = "PasswordReset";
    public const string EmailVerification = "EmailVerification";
}

public enum OtpIssueStatus { Issued, Throttled }

/// Code is only filled when Status is Issued. RetryAfterSeconds is only filled when Throttled.
public record OtpIssueResult(OtpIssueStatus Status, string? Code, int RetryAfterSeconds);

public enum OtpVerifyStatus { Ok, Invalid, TooManyAttempts }

public interface IOtpService
{
    /// Creates a new 6-digit code for this user and purpose, and cancels any older unused one.
    Task<OtpIssueResult> IssueAsync(User user, string purpose);

    /// Checks a code. A correct code is used up (it can never work twice).
    Task<OtpVerifyStatus> VerifyAsync(User user, string purpose, string? code);
}

public class OtpService : IOtpService
{
    public const int CodeLength = 6;
    public const int ValidMinutes = 15;
    public const int MaxAttempts = 5;
    public const int ResendCooldownSeconds = 60;

    private readonly ServisyoDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly TimeProvider _clock;

    public OtpService(ServisyoDbContext db, IPasswordHasher<User> hasher, TimeProvider? clock = null)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<OtpIssueResult> IssueAsync(User user, string purpose)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        var mine = await _db.EmailOtps
            .Where(o => o.UserId == user.UserId && o.Purpose == purpose)
            .ToListAsync();

        // One new code per minute: stops someone from using the Send button to flood an inbox.
        var last = mine.OrderByDescending(o => o.CreatedAt).FirstOrDefault();
        if (last is not null)
        {
            var elapsed = (now - last.CreatedAt).TotalSeconds;
            if (elapsed < ResendCooldownSeconds)
                return new OtpIssueResult(OtpIssueStatus.Throttled, null, (int)Math.Ceiling(ResendCooldownSeconds - elapsed));
        }

        // A newer code replaces every older one.
        foreach (var open in mine.Where(o => o.ConsumedAt is null))
            open.ConsumedAt = now;

        // Housekeeping: codes older than a day are of no use to anybody.
        _db.EmailOtps.RemoveRange(mine.Where(o => o.CreatedAt < now.AddDays(-1)));

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");   // cryptographically random, keeps leading zeros
        _db.EmailOtps.Add(new EmailOtp
        {
            UserId = user.UserId,
            Purpose = purpose,
            CodeHash = _hasher.HashPassword(user, code),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(ValidMinutes)
        });
        await _db.SaveChangesAsync();

        return new OtpIssueResult(OtpIssueStatus.Issued, code, 0);
    }

    public async Task<OtpVerifyStatus> VerifyAsync(User user, string purpose, string? code)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        code = (code ?? string.Empty).Trim();

        var otp = (await _db.EmailOtps
                .Where(o => o.UserId == user.UserId && o.Purpose == purpose && o.ConsumedAt == null && o.ExpiresAt > now)
                .ToListAsync())
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefault();

        if (otp is null)
        {
            _hasher.HashPassword(user, code);        // similar work, so timing does not reveal whether a code exists
            return OtpVerifyStatus.Invalid;
        }

        if (otp.Attempts >= MaxAttempts) return OtpVerifyStatus.TooManyAttempts;

        // Count the try BEFORE checking, so a guesser cannot get a free look.
        otp.Attempts++;

        var looksRight = code.Length == CodeLength && code.All(char.IsAsciiDigit);
        var matches = looksRight && _hasher.VerifyHashedPassword(user, otp.CodeHash, code) != PasswordVerificationResult.Failed;

        if (matches) otp.ConsumedAt = now;
        await _db.SaveChangesAsync();

        if (matches) return OtpVerifyStatus.Ok;
        return otp.Attempts >= MaxAttempts ? OtpVerifyStatus.TooManyAttempts : OtpVerifyStatus.Invalid;
    }
}
