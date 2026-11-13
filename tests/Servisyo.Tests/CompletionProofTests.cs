using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Controllers;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.11 ("BookingCompletionProof"): "Mark Completed" needs a completion message and a proof photo.
/// The provider sends both in "Confirm Service Completion"; the server checks them again, saves the photo in
/// wwwroot/uploads/completions and BOOKINGS (message, photo, COMPLETED_AT, status) together. The customer and the
/// administrators see the proof. The trigger and the CHECK run in SQL Server (self-test 37 of the design file).
public class CompletionProofTests
{
    // Wed 14 Oct 2026, 10:00 in Manila (= 02:00 UTC)
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 2, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime NowUtc = new(2026, 10, 14, 2, 0, 0);

    private sealed record World(ServisyoDbContext Db, ProviderBookingsService Svc, MemoryPhotoStore Photos, Customer Ana, Provider Marco);

    private static User NewUser(string first, string role, string status = AccountStatuses.Active) => new()
    {
        FirstName = first, LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "x", Role = role, AccountStatus = status
    };

    private static async Task<World> WorldAsync(string providerStatus = AccountStatuses.Active)
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ana = new Customer { User = NewUser("Ana", Roles.Customer) };
        var marco = new Provider { BusinessName = "Marco's Repairs", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Marco", Roles.ServiceProvider, providerStatus) };
        db.AddRange(ana, marco);
        await db.SaveChangesAsync();
        var photos = new MemoryPhotoStore();
        return new(db, new ProviderBookingsService(db, new FixedClock(), null, null, photos), photos, ana, marco);
    }

    private static async Task<Booking> BookingAsync(World w, string status, Provider? provider = null)
    {
        var p = provider ?? w.Marco;
        var b = new Booking
        {
            CustomerId = w.Ana.CustomerId, ProviderId = p.ProviderId, BookingDate = new DateOnly(2026, 10, 20), BookingTime = new TimeOnly(9, 0),
            TotalDurationMinutes = 60, TotalFee = 750m, Status = status, ServiceLocation = "Capitol Site, Cebu City", ServiceDescription = "Clean",
            PaymentStatus = status == BookingStatuses.Pending ? PaymentStatuses.Unpaid : PaymentStatuses.Paid,
            CreatedAt = NowUtc.AddDays(-1), UpdatedAt = NowUtc.AddDays(-1), RowVersion = new byte[] { 1 }
        };
        b.Services.Add(new BookingService
        {
            Service = new Service { ProviderId = p.ProviderId, Category = new ServiceCategory { CategoryName = "Cleaning " + Guid.NewGuid() }, ServiceName = "Repairing Electronics", Price = 750m, DurationMinutes = 60 },
            PriceSnapshot = 750m, DurationMinutesSnapshot = 60
        });
        w.Db.Bookings.Add(b);
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();
        return b;
    }

    private static Task<Booking> Saved(World w, int bookingId) => w.Db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == bookingId);

    // ------------------------------------------------------------ completing with proof

    [Fact]
    public async Task WithAMessageAndAPhoto_TheJobIsCompleted_AndTheProofIsSavedTogether()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "  " + CompletionTestKit.Message + "  ", CompletionTestKit.Photo());

        Assert.True(result.Succeeded);
        Assert.Equal($"Booking #{b.BookingId} marked as completed.", result.Message);
        var saved = await Saved(w, b.BookingId);
        Assert.Equal(BookingStatuses.Completed, saved.Status);
        Assert.Equal(CompletionTestKit.Message, saved.CompletionMessage);          // trimmed
        Assert.Equal(Assert.Single(w.Photos.Saved), saved.CompletionPhotoUrl);
        Assert.StartsWith($"/uploads/completions/{b.BookingId}_", saved.CompletionPhotoUrl);
        Assert.EndsWith(".png", saved.CompletionPhotoUrl);
        Assert.Equal(NowUtc, saved.CompletedAt);
        Assert.Equal(NowUtc, saved.UpdatedAt);
        Assert.Empty(w.Photos.Deleted);
    }

    [Fact]
    public async Task TheSavedExtension_ComesFromThePhotosBytes_NotFromItsName()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        Assert.True((await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "Done.", CompletionTestKit.Photo(CompletionTestKit.Jpg, "proof.png"))).Succeeded);

        Assert.EndsWith(".jpg", (await Saved(w, b.BookingId)).CompletionPhotoUrl);
    }

    [Fact]
    public async Task LineBreaks_CountOnce_SoA1000CharacterMessageFits()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);
        var message = string.Join("\r\n", Enumerable.Repeat(new string('a', 99), 10));     // 990 letters + 9 line breaks

        Assert.True((await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, message, CompletionTestKit.Photo())).Succeeded);

        Assert.Equal(999, (await Saved(w, b.BookingId)).CompletionMessage!.Length);
    }

    // ------------------------------------------------------------ the message is required

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public async Task NoMessage_IsRefused_WithTheRequestedText_AndNothingChanges(string? message)
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, message, CompletionTestKit.Photo());

        Assert.Equal(BookingActionOutcome.ProofMissing, result.Outcome);
        Assert.Equal("Completion message is required. Please input a message before submitting.", result.Errors!.Message);
        Assert.Null(result.Errors.Photo);
        Assert.Equal(BookingStatuses.InProgress, (await Saved(w, b.BookingId)).Status);
        Assert.Null((await Saved(w, b.BookingId)).CompletionMessage);
        Assert.Empty(w.Photos.Saved);                                               // not even the photo is kept
    }

    [Fact]
    public async Task AMessageOver1000Characters_IsRefused()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, new string('a', 1001), CompletionTestKit.Photo());

        Assert.Equal(CompletionRules.MessageTooLong, result.Errors!.Message);
        Assert.Empty(w.Photos.Saved);
    }

    // ------------------------------------------------------------ the photo is required, and must be a real image

    [Fact]
    public async Task NoPhoto_IsRefused_WithTheRequestedText()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var none = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "Done.", null);
        var empty = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "Done.", CompletionTestKit.Photo(Array.Empty<byte>()));

        foreach (var result in new[] { none, empty })
        {
            Assert.Equal(BookingActionOutcome.ProofMissing, result.Outcome);
            Assert.Equal("Please select a proof photo from your file manager.", result.Errors!.Photo);
            Assert.Null(result.Errors.Message);
        }
        Assert.Equal(BookingStatuses.InProgress, (await Saved(w, b.BookingId)).Status);
    }

    [Fact]
    public async Task NoMessageAndNoPhoto_BothProblemsAreReported_AtOnce()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, " ", null);

        Assert.Equal(CompletionRules.MessageRequired, result.Errors!.Message);
        Assert.Equal(CompletionRules.PhotoRequired, result.Errors.Photo);
    }

    [Theory]
    [InlineData("proof.gif")]               // not an allowed name
    [InlineData("proof.exe")]
    [InlineData("proof")]
    public async Task OnlyJpgPngAndWebpNames_AreAccepted(string name)
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "Done.", CompletionTestKit.Photo(name: name));

        Assert.Equal(CompletionRules.PhotoNotAnImage, result.Errors!.Photo);
        Assert.Empty(w.Photos.Saved);
    }

    [Fact]
    public async Task AFileNamedLikeAnImage_ThatIsNotOne_IsRefused()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "Done.",
            CompletionTestKit.Photo(System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "proof.jpg"));

        Assert.Equal(CompletionRules.PhotoNotAnImage, result.Errors!.Photo);
        Assert.Empty(w.Photos.Saved);
    }

    [Fact]
    public async Task APhotoOver5MB_IsRefused()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);
        var big = new byte[CompletionRules.MaxPhotoBytes + 1];
        CompletionTestKit.Png.CopyTo(big, 0);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "Done.", CompletionTestKit.Photo(big));

        Assert.Equal(CompletionRules.PhotoTooLarge, result.Errors!.Photo);
        Assert.Empty(w.Photos.Saved);
    }

    // ------------------------------------------------------------ only the assigned provider, only In Progress

    [Fact]
    public async Task AnotherProvidersBooking_LooksMissing_AndIsNotTouched()
    {
        var w = await WorldAsync();
        var other = new Provider { BusinessName = "Other", ApprovalStatus = ApprovalStatuses.Approved, User = NewUser("Juan", Roles.ServiceProvider) };
        w.Db.Providers.Add(other);
        await w.Db.SaveChangesAsync();
        var theirs = await BookingAsync(w, BookingStatuses.InProgress, other);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, theirs.BookingId, "Done.", CompletionTestKit.Photo());

        Assert.Equal(BookingActionOutcome.NotFound, result.Outcome);
        Assert.Equal(BookingStatuses.InProgress, (await Saved(w, theirs.BookingId)).Status);
        Assert.Empty(w.Photos.Saved);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Accepted)]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.Rejected)]
    public async Task OnlyAnInProgressJob_CanBeCompleted(string status)
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, status);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "Done.", CompletionTestKit.Photo());

        Assert.Equal(BookingActionOutcome.InvalidTransition, result.Outcome);
        Assert.Equal(status, (await Saved(w, b.BookingId)).Status);
        Assert.Null((await Saved(w, b.BookingId)).CompletionPhotoUrl);
        Assert.Empty(w.Photos.Saved);
    }

    [Theory]
    [InlineData(AccountStatuses.Suspended)]
    [InlineData(AccountStatuses.Deactivated)]
    public async Task ASuspendedOrDeactivatedProvider_CannotComplete(string accountStatus)
    {
        var w = await WorldAsync(accountStatus);
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var result = await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, "Done.", CompletionTestKit.Photo());

        Assert.NotEqual(ProviderDashboardAccess.Allowed, result.Access);
        Assert.Equal(BookingStatuses.InProgress, (await Saved(w, b.BookingId)).Status);
        Assert.Empty(w.Photos.Saved);
    }

    [Fact]
    public async Task TheOldStatusForm_CannotCompleteWithoutProof()
    {
        var w = await WorldAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);

        var result = await w.Svc.ChangeStatusAsync(w.Marco.UserId, b.BookingId, BookingAction.Complete);

        Assert.Equal(BookingActionOutcome.ProofMissing, result.Outcome);
        Assert.Equal(CompletionRules.ProofRequired, result.Message);
        Assert.Equal(BookingStatuses.InProgress, (await Saved(w, b.BookingId)).Status);
    }

    // ------------------------------------------------------------ who sees the proof

    [Fact]
    public async Task TheCustomer_TheProvider_AndTheAdministrator_SeeTheProof()
    {
        var w = await WorldAsync();
        var admin = NewUser("Ada", Roles.Administrator);
        w.Db.Users.Add(admin);
        await w.Db.SaveChangesAsync();
        var b = await BookingAsync(w, BookingStatuses.InProgress);
        Assert.True((await w.Svc.CompleteAsync(w.Marco.UserId, b.BookingId, CompletionTestKit.Message, CompletionTestKit.Photo())).Succeeded);
        var url = Assert.Single(w.Photos.Saved);

        var customerCard = (await new CustomerBookingsService(w.Db, new FixedClock()).GetAsync(w.Ana.UserId, null, 1)).Page!.Cards.Single();
        var providerCard = (await w.Svc.GetAsync(w.Marco.UserId, null, 1)).Page!.Cards.Single();
        var adminDetails = (await new BookingMonitorService(w.Db).GetDetailsAsync(admin.UserId, b.BookingId)).Details!;

        foreach (var proof in new[] { customerCard.Proof, providerCard.Proof, adminDetails.Proof })
        {
            Assert.NotNull(proof);
            Assert.Equal(CompletionTestKit.Message, proof!.Message);
            Assert.Equal(url, proof.PhotoUrl);
            Assert.Equal("Oct 14, 2026 10:00 AM", proof.CompletedText);           // Cebu time
        }
        Assert.True(adminDetails.IsCompleted);
    }

    [Fact]
    public void TheProof_IsOnlyShownForCompletedJobs_AndOnlyPhotosFromTheProofFolder()
    {
        var at = new DateTime(2026, 11, 14, 7, 5, 0);
        Assert.Null(CompletionProofViewModel.From(BookingStatuses.InProgress, "Done", "/uploads/completions/1_a.jpg", at));
        Assert.Null(CompletionProofViewModel.From(BookingStatuses.Completed, null, null, null));       // completed before v2.11

        var shown = CompletionProofViewModel.From(BookingStatuses.Completed, " Done ", "/uploads/completions/31_a.jpg", at)!;
        Assert.Equal(("Done", "/uploads/completions/31_a.jpg", "Nov 14, 2026 3:05 PM"), (shown.Message, shown.PhotoUrl, shown.CompletedText));

        foreach (var notOurs in new[] { "/uploads/avatars/1.png", "/uploads/completions/../avatars/1.png", "/uploads/completions/a/b.jpg",
                                        "https://example.com/x.jpg", "/uploads/completions/", "javascript:alert(1)" })
        {
            Assert.False(CompletionPhotoStore.IsOurs(notOurs), notOurs);
            Assert.Null(CompletionProofViewModel.From(BookingStatuses.Completed, "Done", notOurs, at)!.PhotoUrl);
        }
    }

    [Fact]
    public async Task ThePhotoStore_SavesInTheCompletionsFolder_UnderANameTheServerMakes_AndCanDeleteIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "servisyo-completions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CompletionPhotoStore(folder);
            var url = await store.SaveAsync(31, CompletionTestKit.Png, ".png");

            Assert.Matches(@"^/uploads/completions/31_[0-9a-f-]{36}\.png$", url);
            var file = Path.Combine(folder, url["/uploads/completions/".Length..]);
            Assert.Equal(CompletionTestKit.Png, await File.ReadAllBytesAsync(file));

            store.Delete("/uploads/completions/../outside.png");                    // never outside the folder
            store.Delete(url);
            Assert.False(File.Exists(file));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(31, CompletionTestKit.Png, ".exe"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    // ------------------------------------------------------------ the endpoint

    [Fact]
    public void TheEndpoint_IsAPost_WithTheAntiForgeryCheck_AndASizeLimit()
    {
        var method = typeof(ProviderBookingsController).GetMethod(nameof(ProviderBookingsController.Complete))!;
        Assert.Equal("{id:int}/complete", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(method.GetCustomAttribute<RequestSizeLimitAttribute>());
        Assert.Equal(CompletionRules.MaxRequestBytes, method.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit);
        Assert.Equal(5 * 1024 * 1024, CompletionRules.MaxPhotoBytes);
        Assert.Equal(new[] { ".jpg", ".jpeg", ".png", ".webp" }, CompletionRules.AllowedExtensions);
    }

    [Fact]
    public void TheModal_AndItsScript_UseTheSameRulesAndWordsAsTheServer()
    {
        var root = RepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "Servisyo.Web", "Views", "ProviderBookings", "Index.cshtml"));
        Assert.Contains("Confirm Service Completion", view);
        Assert.Contains("accept=\"image/png, image/jpeg, image/webp\"", view);
        Assert.Contains("Completion Message <span class=\"sv-cmp__req\" aria-hidden=\"true\">*</span>", view);
        Assert.Contains("name=\"completionMessage\"", view);
        Assert.Contains("name=\"proofPhoto\"", view);
        Assert.Contains("enctype=\"multipart/form-data\"", view);
        Assert.Contains("data-complete-open", view);

        var script = File.ReadAllText(Path.Combine(root, "src", "Servisyo.Web", "wwwroot", "js", "booking-completion.js"));
        foreach (var text in new[] { CompletionRules.MessageRequired, CompletionRules.PhotoRequired, CompletionRules.PhotoNotAnImage,
                                     CompletionRules.PhotoTooLarge, CompletionRules.MessageTooLong })
            Assert.Contains("\"" + text + "\"", script);
        Assert.Contains("var MAX_BYTES = 5 * 1024 * 1024;", script);
        Assert.Contains("var MAX_CHARS = 1000;", script);
    }

    // ------------------------------------------------------------ the database model, the SQL, the design file, the tools

    private static IModel Model()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return db.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void TheThreeColumns_AreNullable_WithTheRequestedSizes_AndTheCheckKeepsThemTogether()
    {
        var bookings = Model().FindEntityType(typeof(Booking))!;
        var message = bookings.FindProperty(nameof(Booking.CompletionMessage))!;
        var photo = bookings.FindProperty(nameof(Booking.CompletionPhotoUrl))!;
        var at = bookings.FindProperty(nameof(Booking.CompletedAt))!;
        Assert.Equal(("COMPLETION_MESSAGE", (int?)1000, true), (message.GetColumnName(), message.GetMaxLength(), message.IsNullable));
        Assert.Equal(("COMPLETION_PHOTO_URL", (int?)500, true), (photo.GetColumnName(), photo.GetMaxLength(), photo.IsNullable));
        Assert.Equal(("COMPLETED_AT", true), (at.GetColumnName(), at.IsNullable));

        var check = Assert.Single(bookings.GetCheckConstraints(), c => c.Name == "CK_BOOKINGS_COMPLETION");
        Assert.Equal(ServisyoDbContext.BookingCompletionCheck, check.Sql);
        Assert.Contains("[STATUS] = 'Completed'", check.Sql);
        Assert.Contains("LEN([COMPLETION_MESSAGE]) > 0", check.Sql);
        Assert.Contains("[COMPLETION_MESSAGE] IS NULL AND [COMPLETION_PHOTO_URL] IS NULL AND [COMPLETED_AT] IS NULL", check.Sql);
    }

    [Fact]
    public void TheV211Trigger_IsTheV210One_PlusTheProof_InTheStandardFormat()
    {
        var sql = BookingCompletionSql.BookingsAudit;
        Assert.StartsWith("CREATE OR ALTER TRIGGER dbo.trg_Bookings_Audit ON dbo.BOOKINGS", sql);
        Assert.Contains(DatabaseV2Sql.Marker + " (v2.11", sql);
        Assert.Contains("(9, N'completion message', d.COMPLETION_MESSAGE, i.COMPLETION_MESSAGE, N'')", sql);
        Assert.Contains("(10, N'proof photo', d.COMPLETION_PHOTO_URL, i.COMPLETION_PHOTO_URL, N'')", sql);
        Assert.Contains("THEN N'" + BookingCompletionSql.ProofNote + "' ELSE N'' END", sql);
        Assert.Contains("AND NOT (x.WITH_PROOF = 1 AND f.SEQ IN (9, 10))", sql);
        Assert.Contains("IF TRY_CAST(SESSION_CONTEXT(N'servisyo_audit_off') AS INT) = 1 RETURN;", sql);
        Assert.Contains("ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2", sql);
        Assert.Equal(" (proof photo and completion message saved)", BookingCompletionSql.ProofNote);

        // the INSERT and DELETE parts are exactly the v2.10 ones
        foreach (var part in new[] { "IF @ins = 1 AND @del = 0", "IF @ins = 0 AND @del = 1" })
            Assert.Equal(Block(AuditStandardSql.BookingsAudit, part), Block(sql, part));

        // every VALUES row has 5 items: sequence, field, old, new, note; the sequence runs 1..10
        var rows = Regex.Matches(sql, @"^\s+\((\d+), N'[^']+', ", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(Enumerable.Range(1, 10), rows);

        Assert.Equal(new[] { AuditStandardSql.Exec(sql) }, BookingCompletionSql.CreateAll);
        Assert.Equal(new[] { AuditStandardSql.Exec(AuditStandardSql.BookingsAudit) }, BookingCompletionSql.RestoreOld);
        Assert.Contains("trg_Bookings_Audit", AuditStandardSql.ObjectNames);          // PART 0 and the v2.10 checks still know the name
    }

    /// One "IF ..." part of a trigger, up to its ";" (line endings made the same first: Windows checks out CRLF).
    private static string Block(string sql, string start)
    {
        var text = sql.Replace("\r\n", "\n");
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        var to = text.IndexOf(";\n", from, StringComparison.Ordinal);
        Assert.True(to > from, start);
        return text[from..to];
    }

    [Fact]
    public void TheDesignFile_IsOnV211_WithTheSameSql()
    {
        var design = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "sql", "DatabaseDesign-v2.sql")).Replace("\r\n", "\n");
        Assert.Contains("DATABASE DESIGN v2.12", design);
        Assert.Contains("WHAT CHANGED IN v2.11 (EF migration \"BookingCompletionProof\")", design);
        Assert.Contains("MigrationId LIKE N'%[_]StartWindow'", design);
        Assert.Contains("CONSTRAINT CK_BOOKINGS_COMPLETION CHECK (" + ServisyoDbContext.BookingCompletionCheck + ")", design);
        Assert.Contains("COMPLETION_MESSAGE           NVARCHAR(1000)  NULL", design);
        Assert.Contains("COMPLETION_PHOTO_URL         NVARCHAR(500)   NULL", design);
        Assert.Contains("COMPLETED_AT                 DATETIME2       NULL", design);
        // v2.12 replaced trg_Bookings_Audit again (StartWindowSql.BookingsAudit, it keeps the proof part; StartWindowTests checks it)
        Assert.DoesNotContain(BookingCompletionSql.BookingsAudit.Replace("\r\n", "\n").Trim(), design);
        Assert.Contains(StartWindowSql.BookingsAudit.Replace("\r\n", "\n").Trim(), design);
        Assert.DoesNotContain(AuditStandardSql.BookingsAudit.Replace("\r\n", "\n").Trim(), design);    // the v2.10 trigger was replaced
        Assert.Contains("37 Completing a job with proof", design);
    }

    [Fact]
    public void TheTools_KnowTheNewMigration()
    {
        var root = RepoRoot();
        var add = File.ReadAllText(Path.Combine(root, "tools", "add-booking-completion.ps1"));
        Assert.Contains("BookingCompletionSql.CreateAll", add);
        Assert.Contains("BookingCompletionSql.RestoreOld", add);
        foreach (var op in new[] { "AddColumn BOOKINGS COMPLETED_AT", "AddColumn BOOKINGS COMPLETION_MESSAGE",
                                   "AddColumn BOOKINGS COMPLETION_PHOTO_URL", "AddCheckConstraint BOOKINGS CK_BOOKINGS_COMPLETION" })
            Assert.Contains(op, add);
        var deploy = File.ReadAllText(Path.Combine(root, "tools", "make-monster-deploy.ps1"));
        Assert.Contains("$Latest = \"StartWindow\"", deploy);
        Assert.Contains("\"wwwroot\\uploads\"", deploy);                                // proof photos online are never overwritten
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
