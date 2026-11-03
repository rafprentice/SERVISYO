using Servisyo.Web.Data;
using Xunit;

namespace Servisyo.Tests;

/// The SQL of the ProfileCleanup migration (database v2.1) cannot run in the in-memory test database; the real check
/// is the self-test in docs/sql/DatabaseDesign-v2.sql (PART 6, tests 22-28). These tests make sure the SQL is complete.
public class ProfileCleanupSqlTests
{
    [Fact]
    public void CreateAll_HasTheChangedObjects_ThenTheThreeNewTriggers_AllMarked()
    {
        var expected = ProfileCleanupSql.ChangedObjects.Concat(ProfileCleanupSql.NewTriggers).ToList();
        Assert.Equal(expected.Count, ProfileCleanupSql.CreateAll.Length);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Contains("CREATE OR ALTER", ProfileCleanupSql.CreateAll[i]);
            Assert.Contains("dbo." + expected[i] + " ", ProfileCleanupSql.CreateAll[i].Replace("\n", " ").Replace("\r", " "));
            Assert.Contains(DatabaseV2Sql.Marker, ProfileCleanupSql.CreateAll[i]);
            Assert.StartsWith("EXEC(N'", ProfileCleanupSql.CreateAll[i]);
            Assert.EndsWith("')", ProfileCleanupSql.CreateAll[i].TrimEnd());
        }
        Assert.Equal(new[] { "trg_Users_StatusSync", "trg_Customers_StatusCopy", "trg_Providers_StatusCopy" }, ProfileCleanupSql.NewTriggers);
    }

    [Fact]
    public void TheNewObjects_NeverUseCity_ButTheRestoredV2Objects_Do()
    {
        Assert.DoesNotContain(ProfileCleanupSql.CreateAll, s => s.Contains("CITY"));
        Assert.Contains(ProfileCleanupSql.RestoreV2, s => s.Contains("CITY"));          // Down puts the v2 texts back
        Assert.Equal(ProfileCleanupSql.ChangedObjects.Count, ProfileCleanupSql.RestoreV2.Length);
    }

    [Fact]
    public void Up_ChecksTheDataFirst_AndCopiesTheRealStatus()
    {
        Assert.Contains("THROW 50198", ProfileCleanupSql.PrepareData);                 // stops on phones / names that do not fit
        Assert.Contains("09[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]", ProfileCleanupSql.PrepareData);
        Assert.Contains("LEN(FIRST_NAME) > 100", ProfileCleanupSql.PrepareData);
        Assert.Contains("c.ACCOUNT_STATUS = u.ACCOUNT_STATUS", ProfileCleanupSql.CopyAccountStatus);
        Assert.Contains("p.ACCOUNT_STATUS = u.ACCOUNT_STATUS", ProfileCleanupSql.CopyAccountStatus);
        foreach (var t in ProfileCleanupSql.NewTriggers)
        {
            Assert.Contains("N''" + t + "''", ProfileCleanupSql.CheckNoClash);
            Assert.Contains($"DROP TRIGGER IF EXISTS dbo.{t};", ProfileCleanupSql.DropNew);
        }
    }

    [Fact]
    public void SuspendingAProvider_InTheProcedure_CancelsOnlyPendingBookings()
    {
        var proc = ProfileCleanupSql.CreateAll[ProfileCleanupSql.ChangedObjects.ToList().IndexOf("usp_UpdateAccountStatus")];
        Assert.Contains("b.STATUS = N''Pending''", proc);
        Assert.Contains("SET b.STATUS = N''Cancelled''", proc);
        Assert.Contains("b.STATUS IN (N''Accepted'', N''In Progress'')", proc);   // only listed, never changed
    }

    [Fact]
    public void TheAuditTriggers_AreNeverTouched()
    {
        foreach (var audit in new[] { "trg_Users_Audit", "trg_Bookings_Audit", "trg_Services_Audit" })
        {
            Assert.DoesNotContain(audit, ProfileCleanupSql.DropNew);
            Assert.DoesNotContain(ProfileCleanupSql.CreateAll, s => s.Contains("TRIGGER dbo." + audit));
        }
    }
}
