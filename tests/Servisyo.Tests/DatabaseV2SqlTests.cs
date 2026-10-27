using Servisyo.Web.Data;
using Xunit;

namespace Servisyo.Tests;

/// The SQL of database v2 cannot run in the in-memory test database (the real check is the self-test in
/// docs/sql/DatabaseDesign-v2.sql, PART 6). These tests make sure the migration's SQL is complete and safe.
public class DatabaseV2SqlTests
{
    [Fact]
    public void Every_object_is_created_once_in_order_and_carries_the_marker()
    {
        Assert.Equal(DatabaseV2Sql.Objects.Count, DatabaseV2Sql.CreateAll.Length);
        for (var i = 0; i < DatabaseV2Sql.Objects.Count; i++)
        {
            var (type, name) = DatabaseV2Sql.Objects[i];
            var sql = DatabaseV2Sql.CreateAll[i];
            Assert.StartsWith("EXEC(N'CREATE OR ALTER " + type + " dbo." + name, sql);
            Assert.Contains(DatabaseV2Sql.Marker, sql);
            Assert.EndsWith("')", sql.TrimEnd());
        }
        Assert.Equal(DatabaseV2Sql.Objects.Count, DatabaseV2Sql.Objects.Select(o => o.Name).Distinct().Count());
    }

    [Fact]
    public void Functions_come_before_views_procedures_and_triggers()
    {
        var order = DatabaseV2Sql.Objects.Select(o => o.Type).ToList();
        Assert.True(order.LastIndexOf("FUNCTION") < order.IndexOf("VIEW"));
        Assert.True(order.LastIndexOf("VIEW") < order.IndexOf("PROCEDURE"));
        Assert.True(order.LastIndexOf("PROCEDURE") < order.IndexOf("TRIGGER"));
    }

    [Fact]
    public void The_clash_check_and_the_drop_cover_every_object_and_never_touch_the_audit_triggers()
    {
        foreach (var (type, name) in DatabaseV2Sql.Objects)
        {
            Assert.Contains("N''" + name + "''", DatabaseV2Sql.CheckNoClash);
            Assert.Contains($"DROP {type} IF EXISTS dbo.{name};", DatabaseV2Sql.DropAll);
        }
        foreach (var audit in new[] { "trg_Users_Audit", "trg_Bookings_Audit", "trg_Services_Audit" })
        {
            Assert.DoesNotContain(audit, DatabaseV2Sql.DropAll);
            Assert.DoesNotContain(DatabaseV2Sql.CreateAll, s => s.Contains("TRIGGER dbo." + audit));
        }
    }

    [Theory]
    [InlineData("usp_RegisterCustomer")]
    [InlineData("usp_RegisterProvider")]
    [InlineData("usp_CreateBooking")]
    [InlineData("usp_ApproveAndPay")]
    [InlineData("trg_Reviews_RatingSync")]
    [InlineData("trg_Bookings_StatusFlow")]
    [InlineData("trg_Payments_Audit")]
    public void The_main_objects_are_there(string name) =>
        Assert.Contains(DatabaseV2Sql.Objects, o => o.Name == name);

    [Fact]
    public void Write_procedures_run_in_a_transaction_with_xact_abort()
    {
        foreach (var (type, name) in DatabaseV2Sql.Objects.Where(o => o.Type == "PROCEDURE"))
        {
            var sql = DatabaseV2Sql.CreateAll[DatabaseV2Sql.Objects.ToList().IndexOf((type, name))];
            var writes = sql.Contains("INSERT INTO dbo.") || sql.Contains("UPDATE dbo.") || sql.Contains("DELETE FROM dbo.");
            if (!writes) continue;
            Assert.Contains("SET XACT_ABORT ON;", sql);
            Assert.Contains("BEGIN TRANSACTION;", sql);
            Assert.Contains("ROLLBACK TRANSACTION;", sql);
        }
    }
}
