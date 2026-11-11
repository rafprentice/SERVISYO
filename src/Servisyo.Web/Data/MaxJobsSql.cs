namespace Servisyo.Web.Data;

/// Database v2.6 (EF migration "MaxJobsPerDay20"): a provider accepts at most 20 jobs a day (ProviderLimits).
/// EF changes the CHECK CK_PROVIDERS_LIMITS by itself; tools\set-max-jobs-20.ps1 adds these three SQL steps to the
/// generated migration:
///   Up:   CapExisting first (a provider above 20 becomes 20, otherwise the new CHECK cannot be added),
///         then RegisterProvider20 (usp_RegisterProvider says "1 to 20");
///   Down: RegisterProviderV23 (usp_RegisterProvider as it was in v2.3: "1 to 50"). Down cannot bring back
///         numbers above 20 that were lowered.
/// The procedure text is not copied by hand: it is the v2.3 text (DerivedDataSql) with only the limit line changed.
public static class MaxJobsSql
{
    public const string CapExisting = "UPDATE dbo.SERVICE_PROVIDERS SET MAX_JOBS_PER_DAY = 20 WHERE MAX_JOBS_PER_DAY > 20;";

    private const string OldLimitLine =
        "IF @MaxJobsPerDay IS NULL OR @MaxJobsPerDay < 1 OR @MaxJobsPerDay > 50 THROW 50110, N''Enter a whole number of jobs per day from 1 to 50.'', 1;";

    private const string NewLimitLine =
        "IF @MaxJobsPerDay IS NULL OR @MaxJobsPerDay < 1 OR @MaxJobsPerDay > 20 THROW 50110, N''Enter a whole number of jobs per day from 1 to 20.'', 1;";

    /// usp_RegisterProvider as created by the DerivedData migration (v2.3).
    public static readonly string RegisterProviderV23 =
        DerivedDataSql.CreateAll.Single(s => s.Contains("CREATE OR ALTER PROCEDURE dbo.usp_RegisterProvider"));

    /// The same procedure with the 20-job limit (the only difference).
    public static readonly string RegisterProvider20 = WithNewLimit(RegisterProviderV23);

    private static string WithNewLimit(string sql)
    {
        var at = sql.IndexOf(OldLimitLine, StringComparison.Ordinal);
        if (at < 0 || sql.IndexOf(OldLimitLine, at + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("usp_RegisterProvider (v2.3) does not have exactly one 50-job limit line.");
        return sql.Replace(OldLimitLine, NewLimitLine, StringComparison.Ordinal)
                  .Replace("SERVISYO-DB-V2", "SERVISYO-DB-V2 (v2.6: at most 20 jobs a day)", StringComparison.Ordinal);
    }
}
