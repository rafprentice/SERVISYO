using System.Data;
using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

/// Database v2.9: accounts nobody has used for a year are removed, so the database does not keep data nobody needs.
public static class InactiveAccountRules
{
    public const int InactiveDays = 365;                         // no sign-in for this long = inactive
    public const int WarningDays = 30;                           // the warning email comes this long before
    public const int WarnAfterDays = InactiveDays - WarningDays; // 335 days without a sign-in

    /// Bookings that are not finished yet: an account that still has one is never removed (or warned).
    /// SERVISYO's names for "Requested / Quotation / Scheduled / In progress"; a Completed job whose cash has not been
    /// collected yet counts as unfinished too (PAYMENTS_DTL = PendingCollection).
    public static readonly string[] UnfinishedStatuses = { BookingStatuses.Pending, BookingStatuses.Accepted, BookingStatuses.InProgress };

    // An account WITH finished bookings is not deleted (the other person's history, ratings and reports need them):
    // the person is erased instead and the row stays under these values, with no way to sign in.
    public const string DeletedFirstName = "Deleted";
    public const string DeletedLastName = "User";
    public const string DeletedBusinessName = "Deleted provider";
    public const string RemovedLocation = "Removed (account deleted)";
    /// No real account has an empty password hash (every sign-up and reset stores a hash), so "" marks an erased account
    /// and keeps it out of later runs.
    public const string ErasedPasswordHash = "";

    public static string DeletedEmail(int userId) => $"deleted-user-{userId}@deleted.servisyo.invalid";

    /// SYSTEM_LOGS.DESCRIPTION of each removal (USER_ID null, USER_ROLE System, ACTION_TYPE DELETE, ENTITY User), in the
    /// v2.10 standard format: "System deleted User #12: Automatically purged inactive account (> 1 year): ana@gmail.com."
    public static string PurgedDescription(int userId, string email, bool erased) =>
        AuditFormat.Deleted(AuditFormat.SystemRole, "User", userId,
            $"Automatically purged inactive account (> 1 year): {email}" + (erased ? KeptNote : ""));
    public const string KeptNote = " (finished bookings kept under \"Deleted User\")";
}

/// appsettings: "InactiveAccounts": { "DeleteEnabled": false }
public class InactiveAccountOptions
{
    public const string SectionName = "InactiveAccounts";

    /// false (the default): REPORT ONLY - the daily run changes nothing (no emails, no deletions); the admin dashboard
    /// shows how many accounts it would warn (database v2.10, worked out live, nothing stored).
    /// true: it emails the warnings and removes the accounts whose 30 days have passed.
    public bool DeleteEnabled { get; set; }
}

public enum PurgeOutcome { Deleted, Erased, Skipped }

/// The searches of the clean-up, shared with the admin dashboard (database v2.10: the report-only numbers are shown
/// there, worked out when the page opens, instead of being stored in SYSTEM_LOGS).
public static class InactiveAccountQueries
{
    /// Accounts the rules may touch: not an administrator, not already erased, and no unfinished booking
    /// (as the customer OR as the provider).
    public static IQueryable<User> Candidates(ServisyoDbContext db) =>
        db.Users.Where(u => u.Role != Roles.Administrator
            && u.PasswordHash != InactiveAccountRules.ErasedPasswordHash
            && !db.Bookings.Any(b => (b.Customer.UserId == u.UserId || b.Provider.UserId == u.UserId)
                && (InactiveAccountRules.UnfinishedStatuses.Contains(b.Status)
                    || (b.Status == BookingStatuses.Completed && b.PaymentDetail != null
                        && b.PaymentDetail.PaymentStatus == PaymentRecordStatuses.PendingCollection))));

    /// 335+ days without a sign-in and not warned yet for this inactive period.
    public static IQueryable<User> DueForWarning(IQueryable<User> users, DateTime now)
    {
        var warnFrom = now.AddDays(-InactiveAccountRules.WarnAfterDays);
        return users.Where(u => u.LastActivityAt <= warnFrom
            && (u.InactivityWarnedAt == null || u.InactivityWarnedAt < u.LastActivityAt));
    }

    /// 365+ days without a sign-in AND warned at least 30 days ago (after the last sign-in): never without notice.
    public static IQueryable<User> DueForRemoval(IQueryable<User> users, DateTime now)
    {
        var inactiveFrom = now.AddDays(-InactiveAccountRules.InactiveDays);
        var warnedBy = now.AddDays(-InactiveAccountRules.WarningDays);
        return users.Where(u => u.LastActivityAt <= inactiveFrom
            && u.InactivityWarnedAt != null && u.InactivityWarnedAt >= u.LastActivityAt && u.InactivityWarnedAt <= warnedBy);
    }

    /// 365+ days without a sign-in (warned or not).
    public static IQueryable<User> OverOneYear(IQueryable<User> users, DateTime now)
    {
        var inactiveFrom = now.AddDays(-InactiveAccountRules.InactiveDays);
        return users.Where(u => u.LastActivityAt <= inactiveFrom);
    }
}

public sealed record InactiveAccountRunResult(
    int WouldWarn, int OverOneYear, int Warned, int WarningsNotSent, int Deleted, int Erased, int Failed);

public interface IInactiveAccountService
{
    /// One daily run. deleteEnabled = false only reports.
    Task<InactiveAccountRunResult> RunAsync(bool deleteEnabled, CancellationToken ct = default);
}

public class InactiveAccountService : IInactiveAccountService
{
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);   // the Philippines has no daylight saving

    private readonly ServisyoDbContext _db;
    private readonly IEmailService _email;
    private readonly TimeProvider _clock;
    private readonly IWebHostEnvironment? _env;
    private readonly ILogger<InactiveAccountService> _logger;

    public InactiveAccountService(ServisyoDbContext db, IEmailService email, TimeProvider? clock = null,
        IWebHostEnvironment? env = null, ILogger<InactiveAccountService>? logger = null)
    {
        _db = db;
        _email = email;
        _clock = clock ?? TimeProvider.System;
        _env = env;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<InactiveAccountService>.Instance;
    }

    private IQueryable<User> Candidates() => InactiveAccountQueries.Candidates(_db);
    private static IQueryable<User> DueForWarning(IQueryable<User> users, DateTime now) => InactiveAccountQueries.DueForWarning(users, now);
    private static IQueryable<User> DueForRemoval(IQueryable<User> users, DateTime now) => InactiveAccountQueries.DueForRemoval(users, now);

    public async Task<InactiveAccountRunResult> RunAsync(bool deleteEnabled, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        if (!deleteEnabled)
        {
            // report only: count and change nothing. Nothing is stored either (database v2.10): the admin dashboard
            // works the same numbers out when it opens ("Needs Admin Attention").
            var wouldWarn = await DueForWarning(Candidates(), now).CountAsync(ct);
            var overOneYear = await InactiveAccountQueries.OverOneYear(Candidates(), now).CountAsync(ct);
            if (wouldWarn > 0 || overOneYear > 0)
                _logger.LogInformation("Inactive accounts (report only, nothing changed): {WouldWarn} would get the warning email, {OverOneYear} without a sign-in for over a year.",
                    wouldWarn, overOneYear);
            return new InactiveAccountRunResult(wouldWarn, overOneYear, 0, 0, 0, 0, 0);
        }

        int deleted = 0, erased = 0, failed = 0, warned = 0, notSent = 0;

        // 1. remove the accounts whose 30 days have passed (each one in its own transaction)
        var due = await DueForRemoval(Candidates(), now).OrderBy(u => u.LastActivityAt).Select(u => u.UserId).ToListAsync(ct);
        foreach (var userId in due)
        {
            try
            {
                switch (await PurgeAsync(userId, now, ct))
                {
                    case PurgeOutcome.Deleted: deleted++; break;
                    case PurgeOutcome.Erased: erased++; break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogError(ex, "Inactive account {UserId} could not be removed; it is tried again tomorrow.", userId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        // 2. warn the accounts that reached 335 days (the warning is saved only when the email went out)
        var toWarn = await DueForWarning(Candidates(), now).OrderBy(u => u.LastActivityAt)
            .Select(u => new { u.UserId, u.Email, u.FirstName, u.LastActivityAt }).ToListAsync(ct);
        foreach (var person in toWarn)
        {
            var removeOn = Max(now.AddDays(InactiveAccountRules.WarningDays), person.LastActivityAt.AddDays(InactiveAccountRules.InactiveDays));
            var (subject, html, text) = WarningEmail(person.FirstName, person.LastActivityAt, removeOn);
            bool sent;
            try { sent = await _email.SendAsync(person.Email, subject, html, text); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "The inactivity warning for user {UserId} could not be sent.", person.UserId);
                sent = false;
            }
            if (!sent) { notSent++; continue; }        // not warned = not removed: it is tried again tomorrow

            var user = await _db.Users.FirstAsync(x => x.UserId == person.UserId, ct);
            user.InactivityWarnedAt = now;
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            warned++;
        }

        _logger.LogInformation("Inactive accounts: {Warned} warned ({NotSent} emails not sent), {Deleted} deleted, {Erased} erased (finished bookings kept), {Failed} failed.",
            warned, notSent, deleted, erased, failed);
        return new InactiveAccountRunResult(0, 0, warned, notSent, deleted, erased, failed);
    }

    /// Removes ONE account, all or nothing. Checks again inside the transaction (the person may have just signed in).
    private async Task<PurgeOutcome> PurgeAsync(int userId, DateTime now, CancellationToken ct)
    {
        var relational = _db.Database.IsRelational();
        await using var tx = relational ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        if (!await DueForRemoval(Candidates(), now).AnyAsync(u => u.UserId == userId, ct))
            return PurgeOutcome.Skipped;

        var user = await _db.Users.Include(u => u.Customer).Include(u => u.Provider).FirstAsync(u => u.UserId == userId, ct);
        var email = user.Email;
        var customerId = user.Customer?.CustomerId;
        var providerId = user.Provider?.ProviderId;
        var photos = new[] { user.Customer?.ProfilePhoto, user.Provider?.ProfilePhoto };

        // finished bookings with someone else (as the customer or as the provider)?
        var hasHistory = await _db.Bookings.AnyAsync(b => b.Customer.UserId == userId || b.Provider.UserId == userId, ct);

        // what only this provider has: schedule, breaks, days off and their requests, name change requests, categories
        if (providerId is int pid)
        {
            _db.ProviderBreaks.RemoveRange(await _db.ProviderBreaks.Where(b => b.Availability.ProviderId == pid).ToListAsync(ct));
            _db.ProviderAvailabilities.RemoveRange(await _db.ProviderAvailabilities.Where(a => a.ProviderId == pid).ToListAsync(ct));
            _db.ProviderUnavailableDates.RemoveRange(await _db.ProviderUnavailableDates.Where(d => d.ProviderId == pid).ToListAsync(ct));
            _db.DayOffRequests.RemoveRange(await _db.DayOffRequests.Where(r => r.ProviderId == pid).ToListAsync(ct));
            _db.NameChangeRequests.RemoveRange(await _db.NameChangeRequests.Where(r => r.ProviderId == pid).ToListAsync(ct));
            _db.ProviderCategories.RemoveRange(await _db.ProviderCategories.Where(c => c.ProviderId == pid).ToListAsync(ct));
        }

        PurgeOutcome outcome;
        if (!hasHistory)
        {
            // 1. nothing is shared with anyone: delete the account and everything that belongs to it
            if (providerId is int p)
                _db.Services.RemoveRange(await _db.Services.Where(s => s.ProviderId == p).ToListAsync(ct));
            _db.Messages.RemoveRange(await _db.Messages.Where(m => m.SenderId == userId || m.ReceiverId == userId).ToListAsync(ct));
            // the admin history about this account (it points to the rows that go now)
            _db.AdminActions.RemoveRange(await _db.AdminActions
                .Where(a => (customerId != null && a.TargetCustomerId == customerId) || (providerId != null && a.TargetProviderId == providerId))
                .ToListAsync(ct));
            if (user.Customer is not null) _db.Customers.Remove(user.Customer);
            if (user.Provider is not null) _db.Providers.Remove(user.Provider);
            _db.Users.Remove(user);
            outcome = PurgeOutcome.Deleted;
        }
        else
        {
            // 2. finished bookings exist: keep them (and their payments, reviews and messages) for the other person,
            //    but erase who this was. The account can never sign in again.
            if (providerId is int p)
            {
                var services = await _db.Services.Where(s => s.ProviderId == p).ToListAsync(ct);
                var booked = await _db.BookingServices.Where(bs => bs.Service.ProviderId == p).Select(bs => bs.ServiceId).Distinct().ToListAsync(ct);
                _db.Services.RemoveRange(services.Where(x => !booked.Contains(x.ServiceId)));
                foreach (var kept in services.Where(x => booked.Contains(x.ServiceId))) kept.IsActive = false;

                var provider = user.Provider!;
                provider.BusinessName = InactiveAccountRules.DeletedBusinessName;
                provider.Description = null;
                provider.ProfilePhoto = null;
                provider.Address = null;
                provider.AccountStatus = AccountStatuses.Deactivated;
            }
            if (customerId is int c)
            {
                var customer = user.Customer!;
                customer.Address = null;
                customer.ProfilePhoto = null;
                customer.AccountStatus = AccountStatuses.Deactivated;
                // the address of each booking is this customer's address too
                foreach (var booking in await _db.Bookings.Where(x => x.CustomerId == c).ToListAsync(ct))
                    booking.ServiceLocation = InactiveAccountRules.RemovedLocation;
            }

            user.FirstName = InactiveAccountRules.DeletedFirstName;
            user.MiddleName = null;
            user.LastName = InactiveAccountRules.DeletedLastName;
            user.Email = InactiveAccountRules.DeletedEmail(userId);
            user.PhoneNumber = null;
            user.PasswordHash = InactiveAccountRules.ErasedPasswordHash;
            user.AccountStatus = AccountStatuses.Deactivated;
            user.NotificationsSeenAt = null;
            user.EmailVerificationPending = false;
            user.InactivityWarnedAt = null;
            outcome = PurgeOutcome.Erased;
        }

        // the ONE audit line of this removal, saved in the same transaction
        _db.SystemLogs.Add(new SystemLog
        {
            UserId = null,
            UserRole = AuditFormat.SystemRole,
            ActionType = AuditActionType.Delete,
            EntityName = "User",
            EntityId = userId,
            Description = InactiveAccountRules.PurgedDescription(userId, email, outcome == PurgeOutcome.Erased),
            CreatedAt = now
        });

        // database v2.10: the audit triggers would write every erased value (name, email, phone, address ...) into
        // SYSTEM_LOGS as "old -> new" lines. They are switched off on THIS connection for this one save.
        await SetAuditOffAsync(relational, true, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            await SetAuditOffAsync(relational, false, CancellationToken.None);
        }
        if (tx is not null) await tx.CommitAsync(ct);

        foreach (var photo in photos) DeletePhoto(photo);     // only after the database change is saved
        _logger.LogInformation("Inactive account {UserId} removed ({Outcome}).", userId, outcome);
        return outcome;
    }

    private const string AuditOffSql = "EXEC sp_set_session_context @key = N'" + AuditStandardSql.AuditOffKey + "', @value = 1;";
    private const string AuditOnSql = "EXEC sp_set_session_context @key = N'" + AuditStandardSql.AuditOffKey + "', @value = NULL;";

    /// SESSION_CONTEXT servisyo_audit_off = 1 / NULL on the connection of the current transaction (SQL Server only;
    /// the in-memory test database has no triggers). Turning it back off never throws: the connection goes back to
    /// the pool anyway, and the pool clears the session context.
    private async Task SetAuditOffAsync(bool relational, bool off, CancellationToken ct)
    {
        if (!relational) return;
        try
        {
            await _db.Database.ExecuteSqlRawAsync(off ? AuditOffSql : AuditOnSql, ct);
        }
        catch (Exception ex) when (!off && ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not switch the audit triggers back on for this connection.");
        }
    }

    private void DeletePhoto(string? url)
    {
        if (_env is null || !ProfilePhotoService.IsOurs(url)) return;
        try
        {
            var path = Path.Combine(_env.WebRootPath, "uploads", "avatars", url![ProfilePhotoService.UrlFolder.Length..]);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete the photo {Url} of a removed account.", url);   // harmless: an unused file
        }
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static string CebuDate(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(Manila).ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// The warning, 30 days before. Signing in is all it takes to keep the account.
    public static (string Subject, string Html, string Text) WarningEmail(string firstName, DateTime lastActivityUtc, DateTime removeOnUtc)
    {
        var since = CebuDate(lastActivityUtc);
        var on = CebuDate(removeOnUtc);
        var subject = $"Your SERVISYO account will be deleted on {on}";
        var text =
            $"Hi {firstName},\n\n" +
            $"You have not signed in to SERVISYO since {since}. Accounts that are not used for a year are deleted, so we do not keep your details longer than needed.\n\n" +
            $"To keep your account, just sign in before {on}.\n\n" +
            "If you do nothing, your account and your personal details (name, email, phone number, address and photo) will be deleted on or after that date. " +
            "Bookings you already finished stay in the other person's history under \"Deleted User\".\n\n" +
            "SERVISYO";
        var name = WebUtility.HtmlEncode(firstName);
        var html =
            $"<p>Hi {name},</p>" +
            $"<p>You have not signed in to SERVISYO since <strong>{since}</strong>. Accounts that are not used for a year are deleted, so we do not keep your details longer than needed.</p>" +
            $"<p><strong>To keep your account, just sign in before {on}.</strong></p>" +
            "<p>If you do nothing, your account and your personal details (name, email, phone number, address and photo) will be deleted on or after that date. " +
            "Bookings you already finished stay in the other person's history under &quot;Deleted User&quot;.</p>" +
            "<p>SERVISYO</p>";
        return (subject, html, text);
    }
}
