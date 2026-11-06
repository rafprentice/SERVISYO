using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

public static class OtpPurposes
{
    public const string PasswordReset = "PasswordReset";
}

public enum OtpIssueStatus { Issued, Throttled }

/// Code is only filled when Status is Issued. RetryAfterSeconds is only filled when Throttled.
public record OtpIssueResult(OtpIssueStatus Status, string? Code, int RetryAfterSeconds);

public enum OtpVerifyStatus { Ok, Invalid, TooManyAttempts }

public interface IOtpService
{
    /// The 6-digit code for this user and purpose (at most one email per minute).
    Task<OtpIssueResult> IssueAsync(User user, string purpose);

    /// Checks a code (at most MaxAttempts wrong tries).
    Task<OtpVerifyStatus> VerifyAsync(User user, string purpose, string? code);
}

/// appsettings / user-secrets section "PasswordReset". CodeSecret is a long random text (at least 32 characters).
/// It is a secret: keep it in user-secrets on your laptop and in appsettings.Production.json on the server, never in Git.
public class OtpOptions
{
    public const string SectionName = "PasswordReset";
    public string? CodeSecret { get; set; }
}

/// Database v2.3: the EMAIL_OTPS table is gone - nothing about a code is stored in the database.
/// The code is CALCULATED: HMAC-SHA256(secret, user id + purpose + the user's current password hash + 15-minute window).
///   - The same code comes out again while it is valid, so the server can check it without having saved it.
///   - It stops working by itself once the password changes (the hash is part of it), so it can be used only once.
///   - It is accepted in its own 15-minute window and the next one: at least 15 minutes (what the email says),
///     at most 30. At any moment only two codes can work, so guessing stays hopeless.
///   - Without the secret nobody can work it out, even with the whole database.
/// The "one email per minute" and "5 wrong tries" limits are kept in this process's memory; they start again if the
/// website restarts (the code itself still expires on time). 5 wrong tries lock the reset for 30 minutes; asking for
/// another email does not give more tries.
public class OtpService : IOtpService
{
    public const int CodeLength = 6;
    public const int ValidMinutes = 15;          // what the email says; a code is accepted for 15 to 30 minutes
    public const int StepMinutes = 15;           // the time window
    public const int MaxAttempts = 5;
    public const int ResendCooldownSeconds = 60;
    public const int MinSecretLength = 32;

    // windows accepted: the current one and the one before it, so a code is valid for at least ValidMinutes
    public const int AcceptedSteps = 2;
    public static readonly TimeSpan LockTime = TimeSpan.FromMinutes(StepMinutes * AcceptedSteps);   // after MaxAttempts wrong tries

    private readonly byte[] _key;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Until)> _wrongTries = new();

    public OtpService(IOptions<OtpOptions>? options = null, TimeProvider? clock = null, ILogger<OtpService>? logger = null)
    {
        _clock = clock ?? TimeProvider.System;
        var secret = options?.Value.CodeSecret;
        if (string.IsNullOrWhiteSpace(secret) || secret.Trim().Length < MinSecretLength)
        {
            // still safe (a random key nobody knows), but codes stop working when the website restarts
            _key = RandomNumberGenerator.GetBytes(32);
            (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OtpService>.Instance).LogWarning(
                "PasswordReset:CodeSecret is not set (or shorter than {Min} characters): using a random key until the website restarts. "
                + "Set it with: dotnet user-secrets set \"PasswordReset:CodeSecret\" \"<a long random text>\" --project src/Servisyo.Web",
                MinSecretLength);
        }
        else
        {
            _key = Encoding.UTF8.GetBytes(secret.Trim());
        }
    }

    private static string Key(User user, string purpose) => $"{user.UserId}|{purpose}";

    private long StepAt(DateTimeOffset when) => when.ToUnixTimeSeconds() / (StepMinutes * 60);

    /// The code of one time step. The password hash is part of it, so a new password makes every old code useless.
    private string CodeFor(User user, string purpose, long step)
    {
        var data = Encoding.UTF8.GetBytes($"{user.UserId}|{purpose}|{user.PasswordHash}|{step}");
        var mac = HMACSHA256.HashData(_key, data);
        // the "dynamic truncation" of RFC 4226 (the same idea as authenticator apps): 31 bits -> 6 digits
        var offset = mac[^1] & 0x0F;
        var number = ((mac[offset] & 0x7F) << 24) | (mac[offset + 1] << 16) | (mac[offset + 2] << 8) | mac[offset + 3];
        return (number % 1_000_000).ToString("D6");
    }

    public Task<OtpIssueResult> IssueAsync(User user, string purpose)
    {
        var now = _clock.GetUtcNow();
        var key = Key(user, purpose);

        // One email per minute: stops someone from using the Send button to flood an inbox.
        if (_lastSent.TryGetValue(key, out var last))
        {
            var elapsed = (now - last).TotalSeconds;
            if (elapsed < ResendCooldownSeconds)
                return Task.FromResult(new OtpIssueResult(OtpIssueStatus.Throttled, null, (int)Math.Ceiling(ResendCooldownSeconds - elapsed)));
        }
        _lastSent[key] = now;                         // (a new email does NOT give new tries: see VerifyAsync)
        Prune(now);

        return Task.FromResult(new OtpIssueResult(OtpIssueStatus.Issued, CodeFor(user, purpose, StepAt(now)), 0));
    }

    public Task<OtpVerifyStatus> VerifyAsync(User user, string purpose, string? code)
    {
        var now = _clock.GetUtcNow();
        var key = Key(user, purpose);
        code = (code ?? string.Empty).Trim();

        if (_wrongTries.TryGetValue(key, out var tries) && tries.Until > now && tries.Count >= MaxAttempts)
            return Task.FromResult(OtpVerifyStatus.TooManyAttempts);

        var looksRight = code.Length == CodeLength && code.All(char.IsAsciiDigit);
        var given = Encoding.ASCII.GetBytes(looksRight ? code : "------");
        var matches = false;
        var step = StepAt(now);
        for (var i = 0; i < AcceptedSteps; i++)          // always every step: the time taken does not depend on the code
            matches |= CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(CodeFor(user, purpose, step - i)));
        matches &= looksRight;

        if (matches)
        {
            _wrongTries.TryRemove(key, out _);
            return Task.FromResult(OtpVerifyStatus.Ok);
        }

        // count the wrong try; the count is forgotten LockTime after the first wrong try (every code it could
        // have been for has expired by then)
        var until = now + LockTime;
        var updated = _wrongTries.AddOrUpdate(key, _ => (1, until),
            (_, old) => old.Until > now ? (old.Count + 1, old.Until) : (1, until));
        return Task.FromResult(updated.Count >= MaxAttempts ? OtpVerifyStatus.TooManyAttempts : OtpVerifyStatus.Invalid);
    }

    /// Forgets limits that no longer matter, so the memory used stays small.
    private void Prune(DateTimeOffset now)
    {
        if (_lastSent.Count + _wrongTries.Count < 1000) return;
        foreach (var (k, at) in _lastSent)
            if ((now - at).TotalSeconds >= ResendCooldownSeconds) _lastSent.TryRemove(k, out _);
        foreach (var (k, v) in _wrongTries)
            if (v.Until <= now) _wrongTries.TryRemove(k, out _);
    }
}
