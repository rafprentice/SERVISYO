namespace Servisyo.Web.Data;

/// Database v2.9 (EF migration "InactiveAccounts"): USERS.LAST_ACTIVITY_AT and USERS.INACTIVITY_WARNED_AT.
/// EF adds the two columns (LAST_ACTIVITY_AT with the default SYSUTCDATETIME(), so every existing row first gets
/// "now"); tools\add-inactive-accounts.ps1 then adds this step to the migration, so each existing account starts from
/// its real last sign-in instead. Nothing to undo in Down: EF removes the columns.
public static class InactiveAccountsSql
{
    /// Each account's last successful sign-in (the LOGIN lines the app writes to SYSTEM_LOGS), or its registration
    /// when it never signed in. Wrapped in EXEC so the idempotent deploy script compiles before the column exists.
    public const string InitLastActivity = @"EXEC(N'UPDATE u
SET LAST_ACTIVITY_AT = COALESCE(
        (SELECT MAX(l.CREATED_AT) FROM dbo.SYSTEM_LOGS l WHERE l.USER_ID = u.USER_ID AND l.ACTION_TYPE = N''LOGIN''),
        u.CREATED_AT)
FROM dbo.USERS u;')";
}
