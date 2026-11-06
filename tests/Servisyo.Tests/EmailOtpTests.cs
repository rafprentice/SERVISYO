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

/// Database v2.3: the reset code is calculated (HMAC of the user, the password hash and the 15-minute window),
/// so the EMAIL_OTPS table is gone and nothing about a code is stored.
public class OtpServiceTests
{
    internal const string Secret = "unit-test-secret-that-is-long-enough-0123456789";

    private static (OtpService otp, TestClock clock) Create(string? secret = Secret, TestClock? clock = null)
    {
        clock ??= new TestClock();
        return (new OtpService(Options.Create(new OtpOptions { CodeSecret = secret }), clock), clock);
    }

    private static User NewUser(int id = 7, string passwordHash = "HASH-ONE") => new()
    {
        UserId = id, FirstName = "Ana", LastName = "Test", Email = $"user{id}@test.com", PasswordHash = passwordHash, Role = Roles.Customer
    };

    private static string Other(string code) => code == "000000" ? "000001" : "000000";

    [Fact]
    public async Task Issue_GivesASixDigitCode_AndNothingIsStored()
    {
        var (otp, _) = Create();

        var result = await otp.IssueAsync(NewUser(), OtpPurposes.PasswordReset);

        Assert.Equal(OtpIssueStatus.Issued, result.Status);
        Assert.Matches(@"^\d{6}$", result.Code!);

        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.GetTableName() == "EMAIL_OTPS");
    }

    [Fact]
    public async Task Verify_RightCode_Works()
    {
        var (otp, _) = Create();
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Fact]
    public async Task TheCode_StopsWorking_OnceThePasswordChanges()
    {
        var (otp, _) = Create();
        var user = NewUser(passwordHash: "HASH-ONE");
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        user.PasswordHash = "HASH-TWO";       // the reset saved a new password

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Fact]
    public async Task TheCode_WorksForFifteenMinutes_ButNotAfterThirty()
    {
        var (otp, clock) = Create();
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        clock.Advance(TimeSpan.FromMinutes(OtpService.ValidMinutes));
        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));

        clock.Advance(TimeSpan.FromMinutes(OtpService.ValidMinutes) + TimeSpan.FromSeconds(1));
        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Fact]
    public async Task ACodeSentAtTheEndOfAWindow_StillGetsItsFifteenMinutes()
    {
        var clock = new TestClock();
        clock.Now = new DateTimeOffset(2026, 10, 15, 2, 14, 59, TimeSpan.Zero);     // one second before the next window
        var (otp, _) = Create(clock: clock);
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        clock.Advance(TimeSpan.FromMinutes(OtpService.ValidMinutes) - TimeSpan.FromSeconds(1));

        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Fact]
    public async Task Verify_WrongCode_IsInvalid()
    {
        var (otp, _) = Create();
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, Other(code)));
    }

    [Fact]
    public async Task Verify_LocksAfterFiveWrongTries_EvenForTheRightCode()
    {
        var (otp, _) = Create();
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        for (var i = 1; i < OtpService.MaxAttempts; i++)
            Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, Other(code)));
        Assert.Equal(OtpVerifyStatus.TooManyAttempts, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, Other(code)));

        Assert.Equal(OtpVerifyStatus.TooManyAttempts, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Fact]
    public async Task ANewEmail_DoesNotGiveMoreTries()
    {
        var (otp, clock) = Create();
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;
        for (var i = 1; i < OtpService.MaxAttempts; i++)
            await otp.VerifyAsync(user, OtpPurposes.PasswordReset, Other(code));

        clock.Advance(TimeSpan.FromSeconds(OtpService.ResendCooldownSeconds));
        Assert.Equal(OtpIssueStatus.Issued, (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Status);

        Assert.Equal(OtpVerifyStatus.TooManyAttempts, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, Other(code)));
    }

    [Fact]
    public async Task TheLock_EndsAfterThirtyMinutes()
    {
        var (otp, clock) = Create();
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;
        for (var i = 0; i < OtpService.MaxAttempts; i++)
            await otp.VerifyAsync(user, OtpPurposes.PasswordReset, Other(code));

        clock.Advance(OtpService.LockTime + TimeSpan.FromSeconds(1));
        var fresh = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, fresh));
    }

    [Fact]
    public async Task Issue_WithinAMinute_IsThrottled_AndWorksAgainAfterwards()
    {
        var (otp, clock) = Create();
        var user = NewUser();

        Assert.Equal(OtpIssueStatus.Issued, (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Status);

        clock.Advance(TimeSpan.FromSeconds(20));
        var second = await otp.IssueAsync(user, OtpPurposes.PasswordReset);
        Assert.Equal(OtpIssueStatus.Throttled, second.Status);
        Assert.Null(second.Code);
        Assert.Equal(40, second.RetryAfterSeconds);

        clock.Advance(TimeSpan.FromSeconds(41));
        Assert.Equal(OtpIssueStatus.Issued, (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Status);
    }

    [Fact]
    public async Task Issue_AgainInTheSameWindow_GivesTheSameCode()
    {
        var (otp, clock) = Create();
        var user = NewUser();
        var first = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(first, (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code);
    }

    [Fact]
    public async Task Verify_ACodeForAnotherPurpose_DoesNotWork()
    {
        var (otp, _) = Create();
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, "SomethingElse", code));
    }

    [Fact]
    public async Task Verify_ACodeBelongingToSomeoneElse_DoesNotWork()
    {
        var (otp, _) = Create();
        var ana = NewUser(id: 7);
        var ben = NewUser(id: 8);
        var anasCode = (await otp.IssueAsync(ana, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(ben, OtpPurposes.PasswordReset, anasCode));
    }

    [Fact]
    public async Task TheSameSecret_GivesTheSameCode_AnotherSecretDoesNot()
    {
        var clock = new TestClock();
        var user = NewUser();
        var code = (await Create(Secret, clock).otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        // e.g. after the website restarted: the code is worked out again from the same secret
        Assert.Equal(OtpVerifyStatus.Ok, await Create(Secret, clock).otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
        Assert.Equal(OtpVerifyStatus.Invalid,
            await Create("another-secret-that-is-also-long-enough-987654321", clock).otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public async Task WithoutAGoodSecret_ItStillWorks_WithARandomKey(string? secret)
    {
        var (otp, clock) = Create(secret);
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
        // another instance has another random key: nobody can guess the codes
        Assert.Equal(OtpVerifyStatus.Invalid, await Create(secret, clock).otp.VerifyAsync(user, OtpPurposes.PasswordReset, code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("12 456")]
    public async Task Verify_RubbishInput_IsInvalid(string? input)
    {
        var (otp, _) = Create();
        var user = NewUser();
        await otp.IssueAsync(user, OtpPurposes.PasswordReset);

        Assert.Equal(OtpVerifyStatus.Invalid, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, input));
    }

    [Fact]
    public async Task Verify_IgnoresSpacesAroundTheCode()
    {
        var (otp, _) = Create();
        var user = NewUser();
        var code = (await otp.IssueAsync(user, OtpPurposes.PasswordReset)).Code!;

        Assert.Equal(OtpVerifyStatus.Ok, await otp.VerifyAsync(user, OtpPurposes.PasswordReset, "  " + code + " "));
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
        var svc = new PasswordResetService(db, new OtpService(Options.Create(new OtpOptions { CodeSecret = OtpServiceTests.Secret }), clock), mail, hasher);
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

        s.Clock.Advance(TimeSpan.FromMinutes(OtpService.StepMinutes * OtpService.AcceptedSteps) + TimeSpan.FromSeconds(1));   // past both windows

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
