namespace Servisyo.Web.Models;

/// One emailed 6-digit code (password reset now, e-mail verification at sign-up later).
/// The code itself is NEVER stored, only a salted hash of it.
public class EmailOtp
{
    public int EmailOtpId { get; set; }
    public int UserId { get; set; }

    /// What the code is for: see OtpPurposes.
    public string Purpose { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    /// Wrong guesses so far. After OtpService.MaxAttempts the code is dead.
    public int Attempts { get; set; }

    /// Set when the code was used, or replaced by a newer one. A consumed code can never work again.
    public DateTime? ConsumedAt { get; set; }

    public User User { get; set; } = null!;
}
