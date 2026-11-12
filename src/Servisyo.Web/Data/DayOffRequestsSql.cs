namespace Servisyo.Web.Data;

/// Database v2.7 (EF migration "DayOffRequests"): providers REQUEST 1 to 7 days off and an administrator approves or
/// rejects. EF makes the new table PROVIDER_DAY_OFF_REQUESTS and the column PROVIDER_UNAVAILABLE_DATES.REQUEST_ID by
/// itself; tools\add-day-off-requests.ps1 adds these SQL steps to the generated migration:
///   Up:   the three objects below, changed so that a day of a PENDING request does not stop bookings yet;
///   Down: the same three objects as they were (v2.0 / v2.3).
/// The SQL is not copied by hand: it is the current text of each object with only the lines shown here changed,
/// and each change must find its line exactly once (otherwise the app does not even start, and a test fails).
public static class DayOffRequestsSql
{
    /// The day blocks bookings only when it has no request (an older day off, or an administrator's extra day) or its
    /// request is Approved - the same rule as DayOffQueries.BlockingBookings in the app.
    private const string ApprovedOnly =
        "(u.REQUEST_ID IS NULL OR EXISTS (SELECT 1 FROM dbo.PROVIDER_DAY_OFF_REQUESTS r WHERE r.REQUEST_ID = u.REQUEST_ID AND r.STATUS = N''Approved''))";

    // ------------------------------------------------------------------ fn_IsProviderAvailable (from v2.0)

    public static readonly string IsProviderAvailableV20 =
        DatabaseV2Sql.CreateAll.Single(s => s.Contains("CREATE OR ALTER FUNCTION dbo.fn_IsProviderAvailable"));

    private const string FnOld =
        "               WHERE u.PROVIDER_ID = @ProviderId AND u.UNAVAILABLE_DATE = @BookingDate)";

    private const string FnNew =
        "               WHERE u.PROVIDER_ID = @ProviderId AND u.UNAVAILABLE_DATE = @BookingDate\n" +
        "                 AND " + ApprovedOnly + ")   -- v2.7: a Pending day-off request does not block yet";

    public static readonly string IsProviderAvailable27 = ReplaceOnce(IsProviderAvailableV20, FnOld, FnNew);

    // ------------------------------------------------------------------ usp_CreateBooking (from v2.3)

    public static readonly string CreateBookingV23 =
        DerivedDataSql.CreateAll.Single(s => s.Contains("CREATE OR ALTER PROCEDURE dbo.usp_CreateBooking"));

    private const string BookingOld =
        "        IF EXISTS (SELECT 1 FROM dbo.PROVIDER_UNAVAILABLE_DATES WHERE PROVIDER_ID = @ProviderId AND UNAVAILABLE_DATE = @BookingDate)";

    private const string BookingNew =
        "        IF EXISTS (SELECT 1 FROM dbo.PROVIDER_UNAVAILABLE_DATES u WHERE u.PROVIDER_ID = @ProviderId AND u.UNAVAILABLE_DATE = @BookingDate\n" +
        "                   AND " + ApprovedOnly + ")   -- v2.7: a Pending day-off request does not block yet";

    public static readonly string CreateBooking27 = ReplaceOnce(CreateBookingV23, BookingOld, BookingNew);

    // ------------------------------------------------------------------ usp_GetNotifications (from v2.3)

    public static readonly string GetNotificationsV23 =
        DerivedDataSql.CreateAll.Single(s => s.Contains("CREATE OR ALTER PROCEDURE dbo.usp_GetNotifications"));

    /// The provider's news: the answer to their request, or a day off an administrator gave them.
    private const string ProviderAnchor =
        "        -- provider: the administrator''s decision on the application (only the latest one is shown)";

    private const string ProviderPart =
        "        -- provider: the answer to a day-off request, or a day off an administrator gave (v2.7)\n" +
        "        SELECT CONCAT(N''D'', a.ACTION_ID), 1, N''day-off'',\n" +
        "               CASE a.ACTION_TYPE\n" +
        "                    WHEN N''Day Off Approved'' THEN CONCAT(N''Your day off request for '', a.REASON, N'' was approved.'')\n" +
        "                    WHEN N''Day Off Rejected'' THEN CONCAT(N''Your day off request for '', a.REASON, N'' was not approved, so you stay available on those days.'')\n" +
        "                    ELSE CONCAT(N''An administrator gave you a day off: '', a.REASON, N''.'')\n" +
        "               END,\n" +
        "               a.CREATED_AT, NULL\n" +
        "        FROM dbo.ADMIN_ACTIONS a\n" +
        "        WHERE @Role = N''Service Provider'' AND a.TARGET_PROVIDER_ID = @ProviderId\n" +
        "          AND a.ACTION_TYPE IN (N''Day Off Approved'', N''Day Off Rejected'', N''Extra Day Off Approved'')\n" +
        "        UNION ALL\n";

    /// The administrator's news: every request that is still waiting for an answer.
    private const string AdminAnchor =
        "        WHERE @Role = N''Administrator'' AND sp.APPROVAL_STATUS = N''Pending''";

    private const string AdminPart =
        "\n" +
        "        UNION ALL\n" +
        "        -- administrator: every day-off request that is still Pending (v2.7)\n" +
        "        SELECT CONCAT(N''Q'', r.REQUEST_ID), 1, N''day-off-new'',\n" +
        "               CONCAT(pu.FIRST_NAME, N'' '', pu.LAST_NAME, N'' ('', sp.BUSINESS_NAME, N'') asks for a day off: '',\n" +
        "                      FORMAT(d.FIRST_DAY, ''MMM d, yyyy'', ''en-US''),\n" +
        "                      CASE WHEN d.DAYS > 1 THEN CONCAT(N'' to '', FORMAT(d.LAST_DAY, ''MMM d, yyyy'', ''en-US''), N'' ('', d.DAYS, N'' days)'') ELSE N'''' END,\n" +
        "                      N''. Open Providers to approve or reject it.''),\n" +
        "               r.REQUESTED_AT, NULL\n" +
        "        FROM dbo.PROVIDER_DAY_OFF_REQUESTS r\n" +
        "        JOIN dbo.SERVICE_PROVIDERS sp ON sp.PROVIDER_ID = r.PROVIDER_ID\n" +
        "        JOIN dbo.USERS pu ON pu.USER_ID = sp.USER_ID\n" +
        "        CROSS APPLY (SELECT MIN(u.UNAVAILABLE_DATE) AS FIRST_DAY, MAX(u.UNAVAILABLE_DATE) AS LAST_DAY, COUNT(*) AS DAYS\n" +
        "                     FROM dbo.PROVIDER_UNAVAILABLE_DATES u WHERE u.REQUEST_ID = r.REQUEST_ID) d\n" +
        "        WHERE @Role = N''Administrator'' AND r.STATUS = N''Pending''";

    public static readonly string GetNotifications27 =
        ReplaceOnce(ReplaceOnce(GetNotificationsV23, ProviderAnchor, ProviderPart + ProviderAnchor), AdminAnchor, AdminAnchor + AdminPart);

    // ------------------------------------------------------------------ the migration steps

    /// Up: the v2.7 versions (after EF made the table and the column they read).
    public static readonly string[] CreateAll = { IsProviderAvailable27, CreateBooking27, GetNotifications27 };

    /// Down: the versions before v2.7 (before EF removes the table and the column).
    public static readonly string[] RestoreOld = { IsProviderAvailableV20, CreateBookingV23, GetNotificationsV23 };

    private static string ReplaceOnce(string sql, string oldText, string newText)
    {
        var at = sql.IndexOf(oldText, StringComparison.Ordinal);
        if (at < 0 || sql.IndexOf(oldText, at + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Database v2.7: a line to change was not found exactly once: " + oldText.Trim());
        return string.Concat(sql.AsSpan(0, at), newText, sql.AsSpan(at + oldText.Length));
    }
}
