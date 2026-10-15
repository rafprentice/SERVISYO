using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Xunit;

namespace Servisyo.Tests;

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class FakeEmailService : IEmailService
{
    public record Sent(string To, string Subject, string Text);
    public List<Sent> Messages { get; } = new();
    public bool Succeeds { get; set; } = true;

    public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, string textBody)
    {
        Messages.Add(new Sent(toEmail, subject, textBody));
        return Task.FromResult(Succeeds);
    }

    /// The 6-digit code inside the last email.
    public string LastCode() => Regex.Match(Messages[^1].Text, @"Your code: (\d{6})").Groups[1].Value;
}

public class OtpServiceTests
{
    private static (OtpService otp, ServisyoDbContext db, TestClock clock, PasswordHasher<User> hasher) Create()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new TestClock();
        var hasher = new PasswordHasher<User>();
        return (new OtpService(db, hasher, clock), db, clock, hasher);
    }

    private static async Task<User> AddUserAsync(ServisyoDbContext db)
    {
        var u = new User { FirstName = "Ana", LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = Roles.Customer };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task Issue_GivesASixDigitCode_AndStoresOnlyAHashOfIt()
    {
        var (otp, db, _, _) = Create();
        var user = await AddUserAsync(db);

        var result = await otp.IssueAsync(user, OtpPurposes.PasswordReset);

        Assert.Equal(OtpIssueStatus.Issued, result.Status);
        Assert.Matches(@"^\d{6}$", result.Code!);
        var row = await db.EmailOtps.SingleAsync();
        Assert.DoesNotContain(result.Code!, row.CodeHash);
        Assert.Equal(row.CreatedAt.AddMinutes(OtpService.ValidMinutes), row.ExpiresAt);
    }

    [Fact]
    public async Task Verify_RightCode_Works_ButOnlyOnce()
    {
        var (otp, db, _, _) = Create();
        var user = await AddUserAsync(db);
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Fact]
    public async Task Verify_WrongCode_IsInvalid_AndTheTryIsCounted()
    {
        var (otp, db, _, _) = Create();
        var user = await AddUserAsync(db);
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;
        var wrong = code == "000000" ? "000001" : "000000";

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, wrong));
        Assert.Equal(1, (await db.EmailOtps.SingleAsync()).Attempts);
    }

    [Fact]
    public async Task Verify_LocksAfterFiveWrongTries_EvenForTheRightCode()
    {
        var (otp, db, _, _) = Create();
        var user = await AddUserAsync(db);
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;
        var wrong = code == "000000" ? "000001" : "000000";

        for (var i = 1; i < OtpService.MaxAttempts; i++)
            Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, wrong));
        Assert.Equal(OtpVerifyStatus.TooManyAttempts, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, wrong));

        Assert.Equal(OtpVerifyStatus.TooManyAttempts, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Fact]
    public async Task Verify_ExpiredCode_IsInvalid()
    {
        var (otp, db, clock, _) = Create();
        var user = await AddUserAsync(db);
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        clock.Advance(TimeSpan.FromMinutes(OtpService.ValidMinutes + 1));

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Fact]
    public async Task Issue_WithinAMinute_IsThrottled_AndWorksAgainAfterwards()
    {
        var (otp, db, clock, _) = Create();
        var user = await AddUserAsync(db);
        await otp.IssueAsync(user, OtpPurposes.PasswordReset);

        clock.Advance(TimeSpan.FromSeconds(20));
        var again = await otp.IssueAsync(user, OtpPurposes.PasswordReset);
        Assert.Equal(OtpIssueStatus.Throttled, again.Status);
        Assert.Null(again.Code);
        Assert.Equal(40, again.RetryAfterSeconds);

        clock.Advance(TimeSpan.FromSeconds(41));
        Assert.Equal(OtpIssueStatus.Issued, (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Status);
    }

    [Fact]
    public async Task Issue_ANewCode_CancelsTheOldOne()
    {
        var (otp, db, clock, _) = Create();
        var user = await AddUserAsync(db);
        var first = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;
        clock.Advance(TimeSpan.FromSeconds(OtpService.ResendCooldownSeconds + 1));
        var second = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        if (first != second)    // (1 in a million: the two random codes are equal, and then there is nothing to compare)
            Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, first));
        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, second));
    }

    [Fact]
    public async Task Verify_ACodeForAnotherPurpose_DoesNotWork()
    {
        var (otp, db, _, _) = Create();
        var user = await AddUserAsync(db);
        var code = (await otp.IssueAsync(user, OtpPurposes.EmailVerification)).Code!;

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.EmailVerification, code));
    }

    [Fact]
    public async Task Verify_ACodeBelongingToSomeoneElse_DoesNotWork()
    {
        var (otp, db, _, _) = Create();
        var ana = await AddUserAsync(db);
        var ben = await AddUserAsync(db);
        var code = (await otp.IssueAsync(ana, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(ben, OtpPurposes.PasswordReset, code));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12 456")]
    [InlineData("abcdef")]
    public async Task Verify_RubbishInput_IsInvalid(string? input)
    {
        var (otp, db, _, _) = Create();
        var user = await AddUserAsync(db);
        await otp.IssueAsync(user, OtpPurposes.PasswordReset);

        Assert.NotEqual(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, input));
    }

    [Fact]
    public async Task Verify_IgnoresSpacesAroundTheCode()
    {
        var (otp, db, _, _) = Create();
        var user = await AddUserAsync(db);
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, $"  {code} "));
    }
}

public class PasswordResetServiceTests
{
    private sealed record Setup(PasswordResetService Svc, ServisyoDbContext Db, FakeEmailService Mail, TestClock Clock, PasswordHasher<User> Hasher);

    private static Setup Create()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new TestClock();
        var hasher = new PasswordHasher<User>();
        var mail = new FakeEmailService();
        var svc = new PasswordResetService(db, new OtpService(db, hasher, clock), mail, hasher);
        return new Setup(svc, db, mail, clock, hasher);
    }

    private static async Task<User> AddUserAsync(Setup s, string email = "ana@test.com", string status = AccountStatuses.Active, bool pending = false)
    {
        var u = new User { FirstName = "Ana", LastName = "Test", Email = email, Role = Roles.Customer, AccountStatus = status, EmailVerificationPending = pending };
        u.PasswordHash = s.Hasher.HashPassword(u, "OldPass123");
        s.Db.Users.Add(u);
        await s.Db.SaveChangesAsync();
        return u;
    }

    private static bool PasswordIs(Setup s, User u, string password)
        => s.Hasher.VerifyHashedPassword(u, u.PasswordHash, password) != PasswordVerificationResult.Failed;

    [Fact]
    public async Task Request_ForARegisteredEmail_SendsTheCodeToThatAddress()
    {
        var s = Create();
        await AddUserAsync(s, "ana@test.com");

        await s.Svc.RequestResetAsync("  ANA@Test.com ");

        var mail = Assert.Single(s.Mail.Messages);
        Assert.Equal("ana@test.com", mail.To);
        Assert.Contains("password reset", mail.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"^\d{6}$", s.Mail.LastCode());
    }

    [Theory]
    [InlineData("nobody@test.com")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Request_ForAnUnknownEmail_SendsNothing_AndDoesNotFail(string? email)
    {
        var s = Create();
        await AddUserAsync(s);

        await s.Svc.RequestResetAsync(email);

        Assert.Empty(s.Mail.Messages);
        Assert.Empty(s.Db.EmailOtps);
    }

    [Theory]
    [InlineData(AccountStatuses.Suspended)]
    [InlineData(AccountStatuses.Deactivated)]
    public async Task Request_ForASuspendedOrDeactivatedAccount_SendsNothing(string status)
    {
        var s = Create();
        await AddUserAsync(s, "ana@test.com", status);

        await s.Svc.RequestResetAsync("ana@test.com");

        Assert.Empty(s.Mail.Messages);
    }

    [Fact]
    public async Task Request_Twice_WithinAMinute_SendsOnlyOneEmail_AndStaysSilent()
    {
        var s = Create();
        await AddUserAsync(s);

        await s.Svc.RequestResetAsync("ana@test.com");
        await s.Svc.RequestResetAsync("ana@test.com");

        Assert.Single(s.Mail.Messages);
    }

    [Fact]
    public async Task Request_WhenTheEmailCannotBeSent_DoesNotThrow()
    {
        var s = Create();
        s.Mail.Succeeds = false;
        await AddUserAsync(s);

        await s.Svc.RequestResetAsync("ana@test.com");      // logged, never shown to the visitor

        Assert.Single(s.Mail.Messages);
    }

    [Fact]
    public async Task Reset_WithTheEmailedCode_ChangesThePassword()
    {
        var s = Create();
        var user = await AddUserAsync(s, pending: true);
        await s.Svc.RequestResetAsync("ana@test.com");

        var status = await s.Svc.ResetPasswordAsync("ana@test.com", s.Mail.LastCode(), "NewPass456");

        Assert.Equal(PasswordResetStatus.Ok, status);
        var saved = await s.Db.Users.SingleAsync();
        Assert.True(PasswordIs(s, saved, "NewPass456"));
        Assert.False(PasswordIs(s, saved, "OldPass123"));
        Assert.False(saved.EmailVerificationPending);       // the emailed code also proved the address
    }

    [Fact]
    public async Task Reset_WithAWrongCode_LeavesThePasswordAlone()
    {
        var s = Create();
        await AddUserAsync(s);
        await s.Svc.RequestResetAsync("ana@test.com");
        var real = s.Mail.LastCode();
        var wrong = real == "000000" ? "000001" : "000000";

        var status = await s.Svc.ResetPasswordAsync("ana@test.com", wrong, "NewPass456");

        Assert.Equal(PasswordResetStatus.InvalidCode, status);
        Assert.True(PasswordIs(s, await s.Db.Users.SingleAsync(), "OldPass123"));
    }

    [Fact]
    public async Task Reset_TheSameCodeCannotBeUsedTwice()
    {
        var s = Create();
        await AddUserAsync(s);
        await s.Svc.RequestResetAsync("ana@test.com");
        var code = s.Mail.LastCode();

        Assert.Equal(PasswordResetStatus.Ok, await s.Svc.ResetPasswordAsync("ana@test.com", code, "NewPass456"));
        Assert.Equal(PasswordResetStatus.InvalidCode, await s.Svc.ResetPasswordAsync("ana@test.com", code, "Hacker789"));

        Assert.True(PasswordIs(s, await s.Db.Users.SingleAsync(), "NewPass456"));
    }

    [Fact]
    public async Task Reset_ForAnUnknownEmail_LooksLikeAWrongCode()
    {
        var s = Create();
        await AddUserAsync(s);

        Assert.Equal(PasswordResetStatus.InvalidCode, await s.Svc.ResetPasswordAsync("nobody@test.com", "123456", "NewPass456"));
    }

    [Fact]
    public async Task Reset_AfterFiveWrongTries_IsLocked_EvenWithTheRightCode()
    {
        var s = Create();
        await AddUserAsync(s);
        await s.Svc.RequestResetAsync("ana@test.com");
        var real = s.Mail.LastCode();
        var wrong = real == "000000" ? "000001" : "000000";

        for (var i = 0; i < OtpService.MaxAttempts; i++)
            await s.Svc.ResetPasswordAsync("ana@test.com", wrong, "NewPass456");

        Assert.Equal(PasswordResetStatus.TooManyAttempts, await s.Svc.ResetPasswordAsync("ana@test.com", real, "NewPass456"));
        Assert.True(PasswordIs(s, await s.Db.Users.SingleAsync(), "OldPass123"));
    }

    [Fact]
    public async Task Reset_ExpiredCode_IsRefused()
    {
        var s = Create();
        await AddUserAsync(s);
        await s.Svc.RequestResetAsync("ana@test.com");
        var code = s.Mail.LastCode();

        s.Clock.Advance(TimeSpan.FromMinutes(OtpService.ValidMinutes + 1));

        Assert.Equal(PasswordResetStatus.InvalidCode, await s.Svc.ResetPasswordAsync("ana@test.com", code, "NewPass456"));
    }

    [Fact]
    public async Task Reset_ForASuspendedAccount_IsRefused()
    {
        var s = Create();
        var user = await AddUserAsync(s);
        await s.Svc.RequestResetAsync("ana@test.com");
        var code = s.Mail.LastCode();
        user.AccountStatus = AccountStatuses.Suspended;     // suspended after the code was sent
        await s.Db.SaveChangesAsync();

        Assert.Equal(PasswordResetStatus.InvalidCode, await s.Svc.ResetPasswordAsync("ana@test.com", code, "NewPass456"));
    }
}

public class EmailServiceTests
{
    private static EmailService Create(EmailSettings settings) => new(Options.Create(settings));

    [Fact]
    public async Task LogMode_DoesNotSendAnything_ButReportsSuccess()
    {
        var svc = Create(new EmailSettings { Mode = "Log" });

        Assert.True(await svc.SendAsync("ana@test.com", "Hi", "<b>Hi</b>", "Hi"));
    }

    [Fact]
    public async Task DefaultSettings_AreLogMode_SoNothingIsSentByAccident()
    {
        Assert.Equal("Log", new EmailSettings().Mode);
        Assert.True(await Create(new EmailSettings()).SendAsync("ana@test.com", "Hi", "<b>Hi</b>", "Hi"));
    }

    [Fact]
    public async Task SmtpMode_WithoutCredentials_FailsWithoutTryingToConnect()
    {
        var svc = Create(new EmailSettings { Mode = "Smtp", Username = "", AppPassword = "" });

        Assert.False(await svc.SendAsync("ana@test.com", "Hi", "<b>Hi</b>", "Hi"));
    }

    [Fact]
    public void CodeEmail_ShowsTheCode_AndEscapesTheText()
    {
        var mail = EmailTemplates.CodeEmail("A <b> heading", "Intro & more", "482910", 15);

        Assert.Contains("482910", mail.Html);
        Assert.Contains("482910", mail.Text);
        Assert.Contains("Your code: 482910", mail.Text);
        Assert.DoesNotContain("<b> heading", mail.Html);
        Assert.Contains("A &lt;b&gt; heading", mail.Html);
        Assert.Contains("15 minutes", mail.Text);
    }
}
