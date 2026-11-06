using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Servisyo.Web.Data;
using Servisyo.Web.Models;
using Xunit;

namespace Servisyo.Tests;

/// Database v2.2: ADMIN_ACTIONS points to its target through one FOREIGN KEY column per kind of target
/// (no polymorphic TARGET_ID, no "USER" type). The real CHECK / FOREIGN KEY behaviour is tested by the
/// self-test in docs/sql/DatabaseDesign-v2.sql (test 29); these tests make sure the model and the SQL agree.
public class AdminTargetsTests
{
    private static IEntityType AdminActionModel()
    {
        var db = new ServisyoDbContext(new DbContextOptionsBuilder<ServisyoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AdminAction))!;
    }

    [Theory]
    [InlineData("TargetCustomerId", "TARGET_CUSTOMER_ID", typeof(Customer))]
    [InlineData("TargetProviderId", "TARGET_PROVIDER_ID", typeof(Provider))]
    [InlineData("TargetReviewId", "TARGET_REVIEW_ID", typeof(Review))]
    [InlineData("TargetCategoryId", "TARGET_CATEGORY_ID", typeof(ServiceCategory))]
    public void EachTargetColumn_IsAForeignKey_ToItsOwnTable_AndNeverCascades(string property, string column, Type principal)
    {
        var entity = AdminActionModel();
        var prop = entity.FindProperty(property)!;
        Assert.Equal(column, prop.GetColumnName());
        Assert.True(prop.IsNullable);

        var fk = Assert.Single(entity.GetForeignKeys(), f => f.Properties.Single().Name == property);
        Assert.Equal(principal, fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);      // the history is never deleted with its target
    }

    [Fact]
    public void ThePolymorphicTargetId_IsGone()
    {
        var entity = AdminActionModel();
        Assert.Null(entity.FindProperty("TargetId"));
        Assert.DoesNotContain(entity.GetProperties(), p => p.GetColumnName() == "TARGET_ID");
    }

    [Fact]
    public void TheChecks_AllowOnlyTheFourTypes_AndExactlyOneTarget()
    {
        var checks = AdminActionModel().GetCheckConstraints().ToDictionary(c => c.ModelName, c => c.Sql);

        Assert.Equal("[TARGET_TYPE] IN ('CUSTOMER','SERVICE_PROVIDER','REVIEW','CATEGORY')", checks["CK_ADMIN_ACTIONS_TARGET_TYPE"]);
        Assert.Equal(ServisyoDbContext.AdminActionTargetCheck, checks["CK_ADMIN_ACTIONS_TARGET"]);

        // every type: its own column IS NOT NULL and the other three IS NULL
        var columns = new Dictionary<string, string>
        {
            ["CUSTOMER"] = "TARGET_CUSTOMER_ID", ["SERVICE_PROVIDER"] = "TARGET_PROVIDER_ID",
            ["REVIEW"] = "TARGET_REVIEW_ID", ["CATEGORY"] = "TARGET_CATEGORY_ID"
        };
        var branches = Regex.Matches(ServisyoDbContext.AdminActionTargetCheck, @"\(\[TARGET_TYPE\] = '(\w+)'(.*?)\)").ToList();
        Assert.Equal(4, branches.Count);
        foreach (Match b in branches)
        {
            var type = b.Groups[1].Value;
            foreach (var (t, col) in columns)
                Assert.Contains($"[{col}] IS {(t == type ? "NOT NULL" : "NULL")}", b.Groups[2].Value);
        }
        Assert.Equal(AdminTargetTypes.All, branches.Select(b => b.Groups[1].Value));
    }

    [Fact]
    public void TheFactories_FillOnlyTheirOwnColumn()
    {
        var c = AdminAction.ForCustomer(1, "Account Suspended", 7, "why");
        Assert.Equal(("CUSTOMER", (int?)7, (int?)null, (int?)null, (int?)null), (c.TargetType, c.TargetCustomerId, c.TargetProviderId, c.TargetReviewId, c.TargetCategoryId));
        Assert.Equal("why", c.Reason);

        var p = AdminAction.ForProvider(1, "Approved", 3, null);
        Assert.Equal(("SERVICE_PROVIDER", (int?)null, (int?)3, (int?)null, (int?)null), (p.TargetType, p.TargetCustomerId, p.TargetProviderId, p.TargetReviewId, p.TargetCategoryId));

        var r = AdminAction.ForReview(1, "Review Removed", 9, null);
        Assert.Equal(("REVIEW", (int?)null, (int?)null, (int?)9, (int?)null), (r.TargetType, r.TargetCustomerId, r.TargetProviderId, r.TargetReviewId, r.TargetCategoryId));

        var k = AdminAction.ForCategory(1, "Category created", 2, "Plumbing");
        Assert.Equal(("CATEGORY", (int?)null, (int?)null, (int?)null, (int?)2), (k.TargetType, k.TargetCustomerId, k.TargetProviderId, k.TargetReviewId, k.TargetCategoryId));
        Assert.Equal(1, k.AdminUserId);
    }

    // ------------------------------------------------------------------ the migration SQL (AdminTargetsSql)

    [Fact]
    public void TheNewProcedures_WriteTheNewColumns_AndNeverTargetId()
    {
        Assert.Equal(new[] { "usp_ApproveProvider", "usp_UpdateAccountStatus", "usp_ModerateReview" }, AdminTargetsSql.ChangedProcedures);
        Assert.Equal(3, AdminTargetsSql.CreateAll.Length);
        Assert.Equal(3, AdminTargetsSql.RestoreV21.Length);

        for (var i = 0; i < 3; i++)
        {
            var sql = AdminTargetsSql.CreateAll[i];
            Assert.StartsWith("EXEC(N'", sql);
            Assert.EndsWith("')", sql.TrimEnd());
            Assert.Contains("CREATE OR ALTER PROCEDURE dbo." + AdminTargetsSql.ChangedProcedures[i], sql);
            Assert.Contains(DatabaseV2Sql.Marker, sql);
            Assert.DoesNotMatch(@"\bTARGET_ID\b", sql);
            Assert.Matches(@"\bTARGET_ID\b", AdminTargetsSql.RestoreV21[i]);     // Down puts the v2.1 texts back
        }
        Assert.Contains("TARGET_PROVIDER_ID", AdminTargetsSql.CreateAll[0]);
        Assert.Contains("TARGET_CUSTOMER_ID", AdminTargetsSql.CreateAll[1]);
        Assert.Contains("N''CUSTOMER''", AdminTargetsSql.CreateAll[1]);
        Assert.Contains("TARGET_REVIEW_ID", AdminTargetsSql.CreateAll[2]);
        Assert.DoesNotContain("N''USER''", string.Concat(AdminTargetsSql.CreateAll));
    }

    [Fact]
    public void Up_SavesTheTargetsFirst_ConvertsUser_AndStopsOnRowsItCannotConvert()
    {
        var stage = AdminTargetsSql.StageTargets;
        Assert.Contains("INTO dbo." + AdminTargetsSql.StagingTable, stage);
        Assert.Contains("c.USER_ID = a.TARGET_ID", stage);         // USER -> the CUSTOMER of that user ...
        Assert.Contains("p.USER_ID = a.TARGET_ID", stage);         // ... or the SERVICE_PROVIDER of that user
        Assert.Contains("THROW 50197", stage);

        var fill = AdminTargetsSql.FillTargets;
        foreach (var col in new[] { "TARGET_TYPE", "TARGET_CUSTOMER_ID", "TARGET_PROVIDER_ID", "TARGET_REVIEW_ID", "TARGET_CATEGORY_ID" })
            Assert.Contains($"a.{col}", fill);
        Assert.Contains("DROP TABLE dbo." + AdminTargetsSql.StagingTable, fill);
    }

    [Fact]
    public void Down_PutsTheV21TargetsBack()
    {
        Assert.Contains("INTO dbo." + AdminTargetsSql.DownStagingTable, AdminTargetsSql.StageOld);
        Assert.Contains("N''Account %''", AdminTargetsSql.StageOld);        // account changes were USER + USER_ID in v2.1
        Assert.Contains("a.TARGET_ID   = s.OLD_ID", AdminTargetsSql.FillOld);
        Assert.Contains("DROP TABLE dbo." + AdminTargetsSql.DownStagingTable, AdminTargetsSql.FillOld);
    }

    [Fact]
    public void TheDesignFile_HasTheSameCheck_AsTheModel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "sql", "DatabaseDesign-v2.sql"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var design = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "sql", "DatabaseDesign-v2.sql"));
        Assert.Contains("CONSTRAINT CK_ADMIN_ACTIONS_TARGET CHECK (" + ServisyoDbContext.AdminActionTargetCheck + ")", design);
        Assert.Contains("WHAT CHANGED IN v2.2 (EF migration \"AdminTargets\")", design);     // the design file is v2.3 now
    }
}
