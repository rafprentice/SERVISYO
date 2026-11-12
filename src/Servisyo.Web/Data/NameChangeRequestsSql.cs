namespace Servisyo.Web.Data;

/// Database v2.8 (EF migration "NameChangeRequests"): an approved provider asks an administrator to change the name on
/// their account. EF makes the new table PROVIDER_NAME_CHANGE_REQUESTS by itself; tools\add-name-change-requests.ps1
/// adds these SQL steps to the generated migration:
///   Up:   usp_GetNotifications with two more parts (the provider sees the answer, the administrator sees the waiting requests);
///   Down: usp_GetNotifications as it was in v2.7.
/// The SQL is not copied by hand: it is the v2.7 text with two parts added, and each anchor line must be found exactly
/// once (otherwise the app does not even start, and a test fails).
public static class NameChangeRequestsSql
{
    /// ADMIN_ACTIONS.ACTION_TYPE of the answer (REASON = "old name to new name").
    public const string ApprovedActionType = "Name Change Approved";
    public const string RejectedActionType = "Name Change Rejected";

    // ------------------------------------------------------------------ usp_GetNotifications (from v2.7)

    public static readonly string GetNotifications27 = DayOffRequestsSql.GetNotifications27;

    /// The requested name: "Juan Santos" or "Juan Reyes Santos" (with the middle name when there is one).
    private const string NewName =
        "CONCAT(n.FIRST_NAME, CASE WHEN n.MIDDLE_NAME IS NULL THEN N'''' ELSE N'' '' + n.MIDDLE_NAME END, N'' '', n.LAST_NAME)";

    /// The provider's news: the answer to their request. The v2.7 anchor (the application decision) is still there once.
    private const string ProviderAnchor =
        "        -- provider: the administrator''s decision on the application (only the latest one is shown)";

    private const string ProviderPart =
        "        -- provider: the answer to a name change request (v2.8)\n" +
        "        SELECT CONCAT(N''N'', n.REQUEST_ID), 1, N''name-change'',\n" +
        "               CASE n.STATUS\n" +
        "                    WHEN N''Approved'' THEN CONCAT(N''Your name change request was approved. Your name is now '', " + NewName + ", N''.'')\n" +
        "                    ELSE CONCAT(N''Your name change request (to '', " + NewName + ", N'') was not approved, so your name stays the same.'')\n" +
        "               END,\n" +
        "               a.CREATED_AT, NULL\n" +
        "        FROM dbo.PROVIDER_NAME_CHANGE_REQUESTS n\n" +
        "        JOIN dbo.ADMIN_ACTIONS a ON a.ACTION_ID = n.DECIDED_BY_ACTION_ID\n" +
        "        WHERE @Role = N''Service Provider'' AND n.PROVIDER_ID = @ProviderId AND n.STATUS <> N''Pending''\n" +
        "        UNION ALL\n";

    /// The administrator's news: every request that is still waiting. Anchor: the last line of the v2.7 day-off part.
    private const string AdminAnchor =
        "        WHERE @Role = N''Administrator'' AND r.STATUS = N''Pending''";

    private const string AdminPart =
        "\n" +
        "        UNION ALL\n" +
        "        -- administrator: every name change request that is still Pending (v2.8)\n" +
        "        SELECT CONCAT(N''M'', n.REQUEST_ID), 1, N''name-change-new'',\n" +
        "               CONCAT(pu.FIRST_NAME, N'' '', pu.LAST_NAME, N'' ('', sp.BUSINESS_NAME, N'') asks to change their name to '',\n" +
        "                      " + NewName + ", N''. Open Name Changes to approve or reject it.''),\n" +
        "               n.REQUESTED_AT, NULL\n" +
        "        FROM dbo.PROVIDER_NAME_CHANGE_REQUESTS n\n" +
        "        JOIN dbo.SERVICE_PROVIDERS sp ON sp.PROVIDER_ID = n.PROVIDER_ID\n" +
        "        JOIN dbo.USERS pu ON pu.USER_ID = sp.USER_ID\n" +
        "        WHERE @Role = N''Administrator'' AND n.STATUS = N''Pending''";

    public static readonly string GetNotifications28 =
        ReplaceOnce(ReplaceOnce(GetNotifications27, ProviderAnchor, ProviderPart + ProviderAnchor), AdminAnchor, AdminAnchor + AdminPart);

    // ------------------------------------------------------------------ the migration steps

    /// Up: the v2.8 version (after EF made the table it reads).
    public static readonly string[] CreateAll = { GetNotifications28 };

    /// Down: the v2.7 version (before EF removes the table).
    public static readonly string[] RestoreOld = { GetNotifications27 };

    private static string ReplaceOnce(string sql, string oldText, string newText)
    {
        var at = sql.IndexOf(oldText, StringComparison.Ordinal);
        if (at < 0 || sql.IndexOf(oldText, at + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Database v2.8: a line to change was not found exactly once: " + oldText.Trim());
        return string.Concat(sql.AsSpan(0, at), newText, sql.AsSpan(at + oldText.Length));
    }
}
