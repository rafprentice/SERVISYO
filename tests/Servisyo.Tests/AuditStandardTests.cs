using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using Servisyo.Web.Data;
using Servisyo.Web.Helpers;
using Servisyo.Web.Models;
using Servisyo.Web.Services;
using Servisyo.Web.ViewModels;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.10 ("AuditStandard"): every SYSTEM_LOGS line in one format, written by the database audit triggers
/// (INSERT / UPDATE with old -> new per field / DELETE) and by the app (LOGIN); ACTION_TYPE limited by a CHECK;
/// two indexes for the System Logs page. The triggers themselves run in SQL Server (self-test 36 of the design file);
/// these tests make sure the EF model, the migration SQL, the design file and the tools agree.
public class AuditStandardTests
{
    private static IModel Model()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return db.GetService<IDesignTimeModel>().Model;
    }

    /// The (plain, not EXEC-wrapped) SQL without its comments.
    private static string CodeOnly(string sql) =>
        Regex.Replace(Regex.Replace(sql, @"/\*.*?\*/", "", RegexOptions.Singleline), @"--[^\n]*", "");

    private static IEnumerable<string> Triggers() => AuditStandardSql.Objects.Where(o => o.StartsWith("CREATE OR ALTER TRIGGER", StringComparison.Ordinal));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ------------------------------------------------------------ the EF model

    [Fact]
    public void SystemLogs_AllowsOnlyTheFourActionTypes_AndHasTheTwoNewIndexes()
    {
        var logs = Model().FindEntityType(typeof(SystemLog))!;
        var check = Assert.Single(logs.GetCheckConstraints());
        Assert.Equal("CK_SYSTEM_LOGS_ACTION_TYPE", check.Name);
        Assert.Equal("[ACTION_TYPE] IN ('INSERT','UPDATE','DELETE','LOGIN')", check.Sql);

        var indexes = logs.GetIndexes().ToDictionary(i => i.GetDatabaseName()!, i => i.Properties.Select(p => p.GetColumnName()).ToArray());
        Assert.Equal(new[] { "ACTION_TYPE", "CREATED_AT" }, indexes["IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT"]);
        Assert.Equal(new[] { "USER_ID", "CREATED_AT" }, indexes["IX_SYSTEM_LOGS_USER_ID_CREATED_AT"]);
        Assert.Equal(new[] { "CREATED_AT" }, indexes["IX_SYSTEM_LOGS_CREATED_AT"]);
        Assert.False(indexes.ContainsKey("IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE"));        // replaced
        Assert.Empty(logs.GetForeignKeys());                                               // log lines outlive the accounts
    }

    [Fact]
    public void EveryTableWithAnAuditTrigger_TellsEF_SoItSavesWithoutOutput()
    {
        // without HasTrigger, EF saves with OUTPUT, which SQL Server refuses on a table that has a trigger
        var model = Model();
        foreach (var sql in Triggers())
        {
            var m = Regex.Match(sql, @"^CREATE OR ALTER TRIGGER dbo\.(\w+) ON dbo\.(\w+)");
            Assert.True(m.Success, sql[..60]);
            var entity = Assert.Single(model.GetEntityTypes(), e => e.GetTableName() == m.Groups[2].Value);
            Assert.Contains(m.Groups[1].Value, entity.GetDeclaredTriggers().Select(t => t.ModelName));
        }
    }

    // ------------------------------------------------------------ the SQL

    [Fact]
    public void TheObjects_AreTheTwoFunctionsAndTenTriggers_InOrder_WrappedForTheMigration()
    {
        Assert.Equal(12, AuditStandardSql.ObjectNames.Count);
        Assert.Equal(AuditStandardSql.ObjectNames.Count, AuditStandardSql.Objects.Count);
        for (var i = 0; i < AuditStandardSql.Objects.Count; i++)
        {
            var sql = AuditStandardSql.Objects[i];
            Assert.Matches(@"^CREATE OR ALTER (FUNCTION|TRIGGER) dbo\." + AuditStandardSql.ObjectNames[i] + @"\b", sql);
            Assert.Contains(DatabaseV2Sql.Marker + " (v2.10", sql);

            var wrapped = AuditStandardSql.CreateAll[i];
            Assert.StartsWith("EXEC(N'CREATE OR ALTER ", wrapped);
            Assert.EndsWith("')", wrapped);
            Assert.Equal(sql, wrapped["EXEC(N'".Length..^2].Replace("''", "'"));
        }
        Assert.Equal(new[] { "fn_AuditValue", "fn_AuditActor" }, AuditStandardSql.ObjectNames.Take(2));   // the triggers call them
    }

    [Fact]
    public void EveryLine_IsInTheStandardFormat_AndTheVerbMatchesTheActionType()
    {
        var verbs = new Dictionary<string, string> { ["INSERT"] = "created", ["UPDATE"] = "updated", ["DELETE"] = "deleted" };
        foreach (var sql in Triggers())
        {
            var code = CodeOnly(sql);
            var inserts = Regex.Matches(code, @"INSERT INTO dbo\.SYSTEM_LOGS \(USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT\)").Count;
            var lines = Regex.Matches(code,
                @"SELECT @uid, @role, N'(?<act>[A-Z]+)', N'(?<ent>[^']+)', \w+\.\w+,\s+LEFT\(CONCAT\(@actor, N' (?<verb>\w+) (?<ent2>[^#']+) #'");
            Assert.True(inserts > 0);
            Assert.Equal(inserts, lines.Count);
            foreach (Match l in lines)
            {
                Assert.Equal(verbs[l.Groups["act"].Value], l.Groups["verb"].Value);
                Assert.Equal(l.Groups["ent"].Value, l.Groups["ent2"].Value);
            }
            Assert.DoesNotContain("N'LOGIN'", code);           // sign-ins come from the app only
            Assert.Contains("IF TRY_CAST(SESSION_CONTEXT(N'servisyo_audit_off') AS INT) = 1 RETURN;", code);
            Assert.Contains("DECLARE @actor NVARCHAR(560) = dbo.fn_AuditActor(@uid, @role);", code);
            Assert.Contains("COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System')", code);
            // every line is cut to the column (500), so a long value can never make the change itself fail
            Assert.All(Regex.Matches(code, @"LEFT\(CONCAT\(@actor[\s\S]*?\), (\d+)\), @now").Select(m => m.Groups[1].Value), n => Assert.Equal("500", n));
        }
    }

    [Fact]
    public void Updates_AreOneLinePerChangedField_OldToNew_CaseSensitive()
    {
        foreach (var sql in Triggers().Where(s => s.Contains("CROSS APPLY (VALUES", StringComparison.Ordinal)))
        {
            var code = CodeOnly(sql);
            Assert.Contains("N' ''', dbo.fn_AuditValue(f.OLD_VALUE),", sql);
            Assert.Contains("N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'", sql);
            Assert.Contains(") f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)", code);
            // "juan" -> "Juan" is a change too (binary compare), NULL and '' are the same "(none)"
            Assert.Contains("ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2", code);
            Assert.Matches(@"ORDER BY i\.\w+, f\.SEQ;", code);
        }
    }

    [Fact]
    public void TheFieldsTheAdministratorAskedFor_AreLogged()
    {
        var users = CodeOnly(AuditStandardSql.UsersAudit);
        Assert.Contains("(5, N'phone', d.PHONE_NUMBER, i.PHONE_NUMBER)", users);
        Assert.Contains("(4, N'email', d.EMAIL, i.EMAIL)", users);
        Assert.Contains("(6, N'account status', d.ACCOUNT_STATUS, i.ACCOUNT_STATUS)", users);      // Active -> Deactivated
        Assert.Contains("(1, N'address', d.ADDRESS, i.ADDRESS)", CodeOnly(AuditStandardSql.CustomersAudit));
        var providers = CodeOnly(AuditStandardSql.ProvidersAudit);
        Assert.Contains("(3, N'address', d.ADDRESS, i.ADDRESS)", providers);
        Assert.Contains("(4, N'base fee', N'PHP ' + CONVERT(NVARCHAR(30), CAST(d.BASE_FEE AS MONEY), 1)", providers);
        Assert.Contains("(7, N'approval status', d.APPROVAL_STATUS, i.APPROVAL_STATUS)", providers);
        var services = CodeOnly(AuditStandardSql.ServicesAudit);
        Assert.Contains("(4, N'price', N'PHP ' + CONVERT(NVARCHAR(30), CAST(d.PRICE AS MONEY), 1)", services);
        Assert.Contains("(6, N'active', CASE WHEN d.IS_ACTIVE = 1 THEN N'Yes' ELSE N'No' END", services);
        Assert.Contains("(1, N'status', d.STATUS, i.STATUS)", CodeOnly(AuditStandardSql.BookingsAudit));
    }

    [Fact]
    public void WhatIsNotLogged_StaysOut()
    {
        var users = CodeOnly(AuditStandardSql.UsersAudit);
        Assert.DoesNotMatch(@"fn_AuditValue\([di]\.PASSWORD_HASH|N'[^']*', d\.PASSWORD_HASH", users);   // never the password
        Assert.Contains("password changed.", users);
        foreach (var everySignIn in new[] { "LAST_ACTIVITY_AT", "INACTIVITY_WARNED_AT", "NOTIFICATIONS_SEEN_AT" })
            Assert.DoesNotContain(everySignIn, users);
        var providers = CodeOnly(AuditStandardSql.ProvidersAudit);
        foreach (var calculated in new[] { "AVG_RATING", "REVIEW_COUNT", "ACCOUNT_STATUS" })
            Assert.DoesNotContain(calculated, providers);
        Assert.DoesNotContain("ACCOUNT_STATUS", CodeOnly(AuditStandardSql.CustomersAudit));
        // the days of a request are logged through the request, not one line per day
        Assert.Equal(3, Regex.Matches(CodeOnly(AuditStandardSql.UnavailableDatesAudit), @"\b[id]\.REQUEST_ID IS NULL").Count);
    }

    [Fact]
    public void TheFunctions_FormatValuesAndWho()
    {
        var value = CodeOnly(AuditStandardSql.AuditValue);
        Assert.Contains("THEN N'(none)'", value);
        Assert.Contains("WHEN LEN(@Value) > 150 THEN LEFT(@Value, 147) + N'...'", value);
        Assert.Contains("WITH SCHEMABINDING", value);
        var actor = CodeOnly(AuditStandardSql.AuditActor);
        Assert.Contains("CONCAT(ISNULL(NULLIF(LTRIM(RTRIM(@Role)), N''), N'System'), N' ' + dbo.fn_GetFullName(@UserId))", actor);
    }

    [Fact]
    public void CheckBefore_StopsOnSomeoneElsesObject_AndOnlyRemovesTheV29ReportLines()
    {
        var sql = AuditStandardSql.CheckBefore;
        Assert.StartsWith("EXEC(N'", sql);
        foreach (var name in AuditStandardSql.ObjectNames) Assert.Contains("N''" + name + "''", sql);
        Assert.Contains("NOT LIKE N''%" + DatabaseV2Sql.Marker + "%''", sql);
        Assert.Contains("LIKE N''%servisyo[_]user[_]id%''", sql);                  // the v1 audit triggers (no marker yet)
        Assert.Contains("THROW 50210", sql);
        var delete = sql.IndexOf("DELETE FROM dbo.SYSTEM_LOGS WHERE ACTION_TYPE = N''REPORT'';", StringComparison.Ordinal);
        Assert.True(delete > sql.IndexOf("THROW 50210", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("THROW 50211", StringComparison.Ordinal) > delete);
        Assert.Single(Regex.Matches(sql, "DELETE FROM"));
    }

    [Fact]
    public void Down_PutsTheV29TriggersBack_ThenDropsTheFunctions()
    {
        var down = AuditStandardSql.RestoreOld;
        Assert.Equal(6, down.Length);
        foreach (var added in AuditStandardSql.ObjectNames.Where(n => n.StartsWith("trg_") && !AuditStandardSql.RewrittenTriggers.Contains(n)))
            Assert.Contains("DROP TRIGGER IF EXISTS dbo." + added + ";", down[0]);
        Assert.Equal(AuditTriggersSql.CreateUsers, down[1]);
        Assert.Equal(AuditTriggersSql.CreateBookings, down[2]);
        Assert.Equal(AuditTriggersSql.CreateServices, down[3]);
        Assert.Contains("CREATE OR ALTER TRIGGER dbo.trg_PaymentsDtl_Audit ON dbo.PAYMENTS_DTL", down[4]);
        Assert.Contains("payment chosen:", down[4]);                                 // the v2.3 text
        Assert.Equal("EXEC(N'DROP FUNCTION IF EXISTS dbo.fn_AuditActor; DROP FUNCTION IF EXISTS dbo.fn_AuditValue;')", down[5]);
    }

    // ------------------------------------------------------------ the design file and the tools

    [Fact]
    public void TheDesignFile_IsOnV210_WithTheSameSql()
    {
        var root = RepoRoot();
        var design = File.ReadAllText(Path.Combine(root, "docs", "sql", "DatabaseDesign-v2.sql")).Replace("\r\n", "\n");
        Assert.Contains("DATABASE DESIGN v2.12", design);                         // later versions came after (v2.12 is the newest)
        Assert.Contains("WHAT CHANGED IN v2.10 (EF migration \"AuditStandard\")", design);
        Assert.Contains("MigrationId LIKE N'%[_]StartWindow'", design);    // PART 0: the newest migration (v2.12)
        Assert.Contains("CONSTRAINT CK_SYSTEM_LOGS_ACTION_TYPE CHECK (" + AuditActionType.Check + ")", design);
        Assert.Contains("CREATE INDEX IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT ON SYSTEM_LOGS (ACTION_TYPE, CREATED_AT);", design);
        Assert.Contains("CREATE INDEX IX_SYSTEM_LOGS_USER_ID_CREATED_AT ON SYSTEM_LOGS (USER_ID, CREATED_AT);", design);
        Assert.DoesNotContain("IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE ON", design);
        Assert.DoesNotContain("payment chosen:", design);                           // the v2.3 payment trigger was replaced
        // v2.11 replaced trg_Bookings_Audit (BookingCompletionSql.BookingsAudit; CompletionProofTests checks it)
        foreach (var sql in AuditStandardSql.Objects.Where(o => o != AuditStandardSql.BookingsAudit))
            Assert.Contains(sql.Replace("\r\n", "\n").Trim(), design);
        foreach (var name in AuditStandardSql.ObjectNames)
            Assert.Contains("N'" + name + "'", design);                             // PART 0 knows the names
        Assert.Contains("self-test", design);
        Assert.Contains("36 Audit", design);
    }

    [Fact]
    public void TheTools_KnowTheNewMigration()
    {
        var root = RepoRoot();
        var add = File.ReadAllText(Path.Combine(root, "tools", "add-audit-standard.ps1"));
        Assert.Contains("AuditStandardSql.CheckBefore", add);
        Assert.Contains("AuditStandardSql.CreateAll", add);
        Assert.Contains("AuditStandardSql.RestoreOld", add);
        foreach (var op in new[] { "DropIndex SYSTEM_LOGS IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE", "CreateIndex SYSTEM_LOGS IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT",
                                   "CreateIndex SYSTEM_LOGS IX_SYSTEM_LOGS_USER_ID_CREATED_AT", "AddCheckConstraint SYSTEM_LOGS CK_SYSTEM_LOGS_ACTION_TYPE" })
            Assert.Contains(op, add);
        var deploy = File.ReadAllText(Path.Combine(root, "tools", "make-monster-deploy.ps1"));
        Assert.Contains("$Latest = \"StartWindow\"", deploy);         // v2.12 is the newest now
    }
}

/// Database v2.10: the v2.9 REPORT line became a live row of "Needs Admin Attention" (nothing stored).
public class InactiveAccountsAttentionTests
{
    private static readonly DateTimeOffset Now = new(2027, 12, 1, 2, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedOptions : IOptionsMonitor<InactiveAccountOptions>
    {
        public InactiveAccountOptions CurrentValue { get; init; } = new();
        public InactiveAccountOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<InactiveAccountOptions, string?> listener) => null;
    }

    private static User NewUser(string first, string role, int daysAgo) => new()
    {
        FirstName = first, LastName = "Reyes", Email = $"{Guid.NewGuid():N}@test.com", PasswordHash = "hash", Role = role,
        AccountStatus = AccountStatuses.Active, CreatedAt = Now.UtcDateTime.AddDays(-900), LastActivityAt = Now.UtcDateTime.AddDays(-daysAgo)
    };

    [Fact]
    public void TheRow_SaysHowMany_AndThatDeletionIsOff()
    {
        var row = Assert.Single(AdminAttention.Build(new AdminAttentionCounts { InactiveAccounts = 3, InactiveOverOneYear = 1 }));
        Assert.Equal(("inactive", "info", "View Users", AdminAttentionAction.UserList), (row.Key, row.Tone, row.ActionLabel, row.Action));
        Assert.Equal("3 inactive accounts with no sign-in for 335+ days (1 over a year); automatic deletion is off", row.Text);
        Assert.Equal("1 inactive account with no sign-in for 335+ days; automatic deletion is off",
            Assert.Single(AdminAttention.Build(new AdminAttentionCounts { InactiveAccounts = 1 })).Text);
        Assert.Empty(AdminAttention.Build(new AdminAttentionCounts()));
    }

    [Fact]
    public async Task TheDashboard_CountsThemLive_OnlyWhileDeletionIsOff()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = NewUser("Ada", Roles.Administrator, daysAgo: 800);                    // administrators never count
        db.Users.Add(admin);
        db.Customers.Add(new Customer { User = NewUser("Ana", Roles.Customer, daysAgo: 340) });
        db.Customers.Add(new Customer { User = NewUser("Ben", Roles.Customer, daysAgo: 400) });
        db.Customers.Add(new Customer { User = NewUser("Cid", Roles.Customer, daysAgo: 10) });
        await db.SaveChangesAsync();

        var off = (await new AdminDashboardService(db, new FixedClock(), new FixedOptions()).GetAsync(admin.UserId))!;
        var row = Assert.Single(off.Attention, a => a.Key == "inactive");
        Assert.Equal("2 inactive accounts with no sign-in for 335+ days (1 over a year); automatic deletion is off", row.Text);
        Assert.Empty(db.SystemLogs);                                                       // nothing stored

        var on = (await new AdminDashboardService(db, new FixedClock(), new FixedOptions { CurrentValue = new() { DeleteEnabled = true } }).GetAsync(admin.UserId))!;
        Assert.DoesNotContain(on.Attention, a => a.Key == "inactive");                    // the daily job handles them
    }
}
