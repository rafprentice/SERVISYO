using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.3 ("DerivedData"): one fact in one place, nothing stored that can be calculated, derived tables become
/// TOP 25 queries. The real SQL behaviour is tested by the self-test in docs/sql/DatabaseDesign-v2.sql (tests 10, 12,
/// 28, 30-33); these tests make sure the EF model, the migration SQL and the design file agree.
public class DerivedDataTests
{
    private static IModel Model()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return db.GetService<IDesignTimeModel>().Model;
    }

    private static string DesignFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "sql", "DatabaseDesign-v2.sql"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "sql", "DatabaseDesign-v2.sql"));
    }

    // ------------------------------------------------------------------ the model

    [Theory]
    [InlineData("NOTIFICATIONS")]
    [InlineData("BOOKING_REPORTS")]
    [InlineData("EMAIL_OTPS")]
    [InlineData("CASH_PAYMENT_DETAILS")]
    public void TheDerivedTables_AreGone(string table) =>
        Assert.DoesNotContain(Model().GetEntityTypes(), e => e.GetTableName() == table);

    [Fact]
    public void Payments_IsTheListOfMethods_WithItsThreeFixedRows()
    {
        var payments = Model().FindEntityType(typeof(Payment))!;
        Assert.Equal("PAYMENTS", payments.GetTableName());
        Assert.Equal(new[] { "METHOD_NAME", "PAYMENT_ID" }, payments.GetProperties().Select(p => p.GetColumnName()!).OrderBy(x => x));
        Assert.True(Assert.Single(payments.GetIndexes()).IsUnique);         // a method name is listed once

        var seed = payments.GetSeedData().Select(r => ((int)r["PaymentId"]!, (string)r["MethodName"]!)).OrderBy(r => r.Item1);
        Assert.Equal(new[] { (1, "Cash on Completion"), (2, "GCash"), (3, "Maya") }, seed);
        Assert.Equal(PaymentIds.All.Select(m => (m.Id, m.Name)), seed);
    }

    [Fact]
    public void PaymentsDtl_IsTheBridge_OnePaymentPerBooking_AndStoresNoAmount()
    {
        var dtl = Model().FindEntityType(typeof(PaymentDetail))!;
        Assert.Equal("PAYMENTS_DTL", dtl.GetTableName());
        Assert.Equal(new[] { "BOOKING_ID", "CREATED_AT", "PAID_AT", "PAYMENT_DTL_ID", "PAYMENT_ID", "PAYMENT_STATUS", "TRANSACTION_REFERENCE" },
            dtl.GetProperties().Select(p => p.GetColumnName()!).OrderBy(x => x));

        var toBooking = Assert.Single(dtl.GetForeignKeys(), f => f.PrincipalEntityType.ClrType == typeof(Booking));
        var toPayment = Assert.Single(dtl.GetForeignKeys(), f => f.PrincipalEntityType.ClrType == typeof(Payment));
        Assert.True(toBooking.IsUnique);                                    // one payment per booking
        Assert.Equal(DeleteBehavior.Restrict, toBooking.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, toPayment.DeleteBehavior);    // a method in use cannot be deleted
        Assert.Contains(dtl.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(PaymentDetail.TransactionReference));

        var checks = dtl.GetCheckConstraints().ToDictionary(c => c.ModelName, c => c.Sql);
        Assert.Equal("[PAYMENT_STATUS] <> 'PendingCollection' OR [PAYMENT_ID] = 1", checks["CK_PAYMENTS_DTL_PENDING_CASH"]);
        Assert.Equal(3, checks.Count);
    }

    [Fact]
    public void Users_HasTheBellsSeenAtColumn()
    {
        var p = Model().FindEntityType(typeof(User))!.FindProperty(nameof(User.NotificationsSeenAt))!;
        Assert.Equal("NOTIFICATIONS_SEEN_AT", p.GetColumnName());
        Assert.True(p.IsNullable);
    }

    [Theory]
    [InlineData(PaymentMethods.CashOnCompletion, null, PaymentIds.Cash)]
    [InlineData(PaymentMethods.MockEWallet, MockWallets.GCash, PaymentIds.GCash)]
    [InlineData(PaymentMethods.MockEWallet, MockWallets.Maya, PaymentIds.Maya)]
    public void TheCustomersChoice_PointsToTheRightPaymentsRow(string method, string? wallet, int expected)
    {
        Assert.Equal(expected, PaymentIds.For(method, wallet));
        Assert.Equal(method, PaymentIds.MethodOf(expected));
        Assert.Equal(wallet, PaymentIds.WalletOf(expected));
    }

    // ------------------------------------------------------------------ the migration SQL (DerivedDataSql)

    private static string CodeOnly(string sql) =>
        Regex.Replace(Regex.Replace(sql.Replace("''", "'"), @"/\*.*?\*/", "", RegexOptions.Singleline), @"--[^\n]*", "");

    [Fact]
    public void Up_SavesThePaymentsFirst_StopsOnRowsItCannotConvert_AndDropsTheOldTrigger()
    {
        var stage = DerivedDataSql.StagePayments;
        Assert.Contains("INTO dbo." + DerivedDataSql.StagingTable, stage);
        Assert.Contains("THROW 50198", stage);                                  // a payment that cannot be converted: nothing changes
        Assert.Contains("p.AMOUNT <> b.TOTAL_FEE", stage.Replace("''", "'"));   // the amount must really be the booking fee
        Assert.Contains("DROP TRIGGER IF EXISTS dbo.trg_Payments_Audit;", stage);
        Assert.True(stage.IndexOf("DELETE FROM dbo.PAYMENTS;", StringComparison.Ordinal) > stage.IndexOf("THROW 50198", StringComparison.Ordinal));
    }

    [Fact]
    public void Up_CopiesEveryPayment_WithoutTheAmount()
    {
        var fill = DerivedDataSql.FillPaymentDetails;
        Assert.Contains("INSERT INTO dbo.PAYMENTS_DTL (BOOKING_ID, PAYMENT_ID, PAYMENT_STATUS, TRANSACTION_REFERENCE, CREATED_AT, PAID_AT)", fill);
        Assert.DoesNotContain("AMOUNT", fill);
        Assert.Contains("THROW 50193", fill);                                   // not every payment copied: everything rolls back
        Assert.Contains("DROP TABLE dbo." + DerivedDataSql.StagingTable, fill);
    }

    [Fact]
    public void TheNewObjects_NeverUseARemovedTableOrColumn_AndAreMarked()
    {
        Assert.Equal(DerivedDataSql.ChangedObjects.Count + DerivedDataSql.NewObjects.Count, DerivedDataSql.CreateAll.Length);
        Assert.Equal(new[] { "usp_GetNotifications", "usp_GetBookingsNeedingAttention", "trg_PaymentsDtl_Audit" }, DerivedDataSql.NewObjects);
        foreach (var sql in DerivedDataSql.CreateAll)
        {
            Assert.StartsWith("EXEC(N'", sql);
            Assert.Contains(DatabaseV2Sql.Marker, sql);
            var code = CodeOnly(sql);
            foreach (var removed in new[] { @"\bdbo\.NOTIFICATIONS\b", @"\bCASH_PAYMENT_DETAILS\b", @"\bEMAIL_OTPS\b", @"\bBOOKING_REPORTS\b", @"\.AMOUNT\b", @"\.PAYMENT_METHOD\b" })
                Assert.DoesNotMatch(removed, code);
        }
        foreach (var name in DerivedDataSql.ChangedObjects.Concat(DerivedDataSql.NewObjects))
            Assert.Single(DerivedDataSql.CreateAll, s => Regex.IsMatch(s, @"CREATE OR ALTER (PROCEDURE|VIEW|TRIGGER) dbo\." + name + @"\b"));
    }

    [Fact]
    public void Down_PutsThePaymentsBack_AndTheV22Objects()
    {
        Assert.Contains("INTO dbo." + DerivedDataSql.DownStagingTable, DerivedDataSql.StageOld);
        Assert.Contains("DROP PROCEDURE IF EXISTS dbo.usp_GetNotifications;", DerivedDataSql.StageOld);
        Assert.Contains("INSERT INTO dbo.PAYMENTS (BOOKING_ID, AMOUNT, PAYMENT_METHOD", DerivedDataSql.FillOld);
        Assert.Contains("INSERT INTO dbo.CASH_PAYMENT_DETAILS", DerivedDataSql.FillOld);
        Assert.Contains(DerivedDataSql.RestoreV22, s => s.Contains("CREATE OR ALTER TRIGGER dbo.trg_Payments_Audit ON dbo.PAYMENTS"));
        Assert.Equal(DerivedDataSql.ChangedObjects.Count + 1, DerivedDataSql.RestoreV22.Length);
    }

    [Fact]
    public void TheDesignFile_HasTheV23Objects()
    {
        var design = DesignFile();
        Assert.Contains("DATABASE DESIGN v2.10", design);                        // v2.4-v2.10: columns, an index, the 20-job limit, day-off and name change requests, the audit standard
        Assert.Contains("WHAT CHANGED IN v2.3 (EF migration \"DerivedData\")", design);
        Assert.Contains("CREATE OR ALTER PROCEDURE dbo.usp_GetNotifications", design);
        Assert.Contains("CREATE OR ALTER PROCEDURE dbo.usp_GetBookingsNeedingAttention", design);
        Assert.Contains("CREATE TABLE PAYMENTS_DTL", design);
        Assert.DoesNotContain("CREATE TABLE NOTIFICATIONS", design);
        Assert.Contains("MigrationId LIKE N'%[_]AuditStandard'", design);        // PART 0: the newest migration must be there first
        // the same SQL as the migration (the migration only wraps it in EXEC); v2.7 and v2.8 added parts to usp_GetNotifications
        var proc = NameChangeRequestsSql.GetNotifications28;
        var body = proc["EXEC(N'".Length..proc.LastIndexOf("')", StringComparison.Ordinal)].Replace("''", "'");
        Assert.Contains(body.Replace("\r\n", "\n").Trim(), design.Replace("\r\n", "\n"));
    }
}
