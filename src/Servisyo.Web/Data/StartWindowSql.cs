namespace Servisyo.Web.Data;

/// Database v2.12 (EF migration "StartWindow"): "Mark In Progress" only inside the start window, a reason for a late
/// start, and the automatic cancellation of a provider no-show. EF adds the BOOKINGS columns STARTED_AT, LATE_REASON,
/// the calculated MINUTES_LATE / IS_LATE and CHECK CK_BOOKINGS_START by itself; tools\add-start-window.ps1 adds these
/// SQL steps to the generated migration:
///   Up, LAST:    trg_Bookings_StatusFlow, trg_Bookings_Audit, usp_UpdateBookingStatus, usp_GetBookingsNeedingAttention
///                and usp_GetNotifications, v2.12 (they read the new columns, so they come after them)
///   Down, FIRST: the same five objects as they were before (v2.3, v2.11, v2.3, v2.3, v2.8)
/// The plain texts are the SAME as docs/sql/DatabaseDesign-v2.sql (a test compares them); usp_GetNotifications is the
/// v2.8 text with three parts added and two lines changed, each anchor found exactly once (otherwise the app does not
/// even start, and a test fails).
public static class StartWindowSql
{
    // ------------------------------------------------------------------ trg_Bookings_StatusFlow (v2.12)

    public const string StatusFlow = @"CREATE OR ALTER TRIGGER dbo.trg_Bookings_StatusFlow ON dbo.BOOKINGS
AFTER UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2  (v2.12: the start window and the provider no-show)
       The booking life cycle from the project document, enforced by the database itself:
         Pending -> Accepted | Rejected | Cancelled,  Accepted -> In Progress,  In Progress -> Completed,
         and (v2.12) Accepted -> Cancelled, only for a provider no-show.
       A booking can only start (In Progress) once the customer has paid or chosen cash on completion.
       v2.12: starting writes STARTED_AT = now (UTC). CHECK CK_BOOKINGS_START allows a start only from the scheduled
       time to 2 hours after it, with a LATE_REASON when it is more than 15 minutes late. STARTED_AT and LATE_REASON
       are written once, when the job starts. A booking that was paid (or cash promised) but not started within
       2 hours of its scheduled time (Cebu time) is cancelled as a provider no-show; a test e-wallet payment is
       refunded in the same change (PAYMENT_STATUS Refunded).
       Any other status change is refused and the whole statement is rolled back.
       Development scripts that must jump a sample booking to any state can first run:
         EXEC sp_set_session_context @key = N'servisyo_dev_override', @value = 1;   (that connection only) */
    SET NOCOUNT ON;

    IF TRY_CAST(SESSION_CONTEXT(N'servisyo_dev_override') AS INT) = 1 RETURN;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
               WHERE i.STATUS <> d.STATUS
                 AND NOT (   (d.STATUS = N'Pending'     AND i.STATUS IN (N'Accepted', N'Rejected', N'Cancelled'))
                          OR (d.STATUS = N'Accepted'    AND i.STATUS IN (N'In Progress', N'Cancelled'))
                          OR (d.STATUS = N'In Progress' AND i.STATUS = N'Completed')))
        THROW 50160, N'That booking status change is not allowed. Allowed: Pending -> Accepted / Rejected / Cancelled, Accepted -> In Progress (or Cancelled for a provider no-show), In Progress -> Completed.', 1;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
               WHERE d.STATUS = N'Accepted' AND i.STATUS = N'In Progress'
                 AND i.PAYMENT_STATUS <> N'Paid'
                 AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL p
                                 WHERE p.BOOKING_ID = i.BOOKING_ID AND p.PAYMENT_STATUS = N'PendingCollection'))
        THROW 50161, N'A booking can start only after the customer has paid or chosen cash on completion.', 1;

    -- v2.12: an accepted booking is cancelled only as a provider no-show: never started, the customer had paid or
    -- chosen cash, more than 2 hours after its scheduled time (Cebu time), a test e-wallet payment refunded
    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
               WHERE d.STATUS = N'Accepted' AND i.STATUS = N'Cancelled'
                 AND (   i.STARTED_AT IS NOT NULL
                      OR i.PAYMENT_STATUS = N'Paid'
                      OR NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL p WHERE p.BOOKING_ID = i.BOOKING_ID)
                      OR DATEDIFF(MINUTE, CAST(i.BOOKING_DATE AS DATETIME) + CAST(i.BOOKING_TIME AS DATETIME), dbo.fn_ManilaNow()) <= 120))
        THROW 50162, N'An accepted booking can only be cancelled as a provider no-show: not started within 2 hours of its scheduled time, with any test e-wallet payment refunded.', 1;

    -- v2.12: a job starts NOW: STARTED_AT is the current time (5 minutes allowed between the app's and this clock)
    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
               WHERE d.STATUS = N'Accepted' AND i.STATUS = N'In Progress'
                 AND (i.STARTED_AT IS NULL OR ABS(DATEDIFF(SECOND, i.STARTED_AT, SYSUTCDATETIME())) > 300))
        THROW 50163, N'A booking starts now: STARTED_AT must be the current time (UTC).', 1;

    -- v2.12: the start time and the late reason are written once, when the job starts
    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
               WHERE NOT (d.STATUS = N'Accepted' AND i.STATUS = N'In Progress')
                 AND (   ISNULL(i.STARTED_AT, '19000101') <> ISNULL(d.STARTED_AT, '19000101')
                      OR ISNULL(i.LATE_REASON, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(d.LATE_REASON, N'') COLLATE Latin1_General_100_BIN2))
        THROW 50164, N'The start time and the late reason are saved once, when the job starts, and cannot be changed.', 1;
END";

    // ------------------------------------------------------------------ trg_Bookings_Audit (v2.12)

    /// The words added to the status line when a job starts or is cancelled as a no-show (also in the C# tests).
    public const string OnTimeNote = " (started on time)";
    public const string NoShowNoteSql = "N' (Provider No-Show ' + NCHAR(8212) + N' missed scheduled start window)'";

    public const string BookingsAudit = @"CREATE OR ALTER TRIGGER dbo.trg_Bookings_Audit ON dbo.BOOKINGS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.12: the start of the job; v2.11: the job's proof; v2.10: one line per changed field)
       New booking requests; every change of status, payment status, date, time, duration, fee, location or request
       description (old -> new); deleted bookings.
       v2.11: completing a job WITH its proof (COMPLETION_MESSAGE, COMPLETION_PHOTO_URL) writes ONE line:
         ""... updated Booking #31: status 'In Progress' -> 'Completed' (proof photo and completion message saved).""
       v2.12: starting a job says on the status line whether it was on time, or how late and why:
         ""... updated Booking #33: status 'Accepted' -> 'In Progress' (late start: 25 minutes late, reason 'Flat tire').""
       and a booking cancelled because it was not started in time says so (the actor is System, the no-show job):
         ""System updated Booking #33: status 'Accepted' -> 'Cancelled' (Provider No-Show - missed scheduled start window).""
       The message and the reason are not copied elsewhere (one fact, one place: they are in BOOKINGS). COMPLETED_AT
       and STARTED_AT are the time of that line, so they are not logged as fields when the status changes with them. */
    SET NOCOUNT ON;
    IF TRY_CAST(SESSION_CONTEXT(N'servisyo_audit_off') AS INT) = 1 RETURN;
    DECLARE @ins BIT = CASE WHEN EXISTS (SELECT 1 FROM inserted) THEN 1 ELSE 0 END;
    DECLARE @del BIT = CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 1 ELSE 0 END;
    IF @ins = 0 AND @del = 0 RETURN;
    DECLARE @uid INT = TRY_CAST(SESSION_CONTEXT(N'servisyo_user_id') AS INT);
    DECLARE @role NVARCHAR(50) = COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System');
    DECLARE @actor NVARCHAR(560) = dbo.fn_AuditActor(@uid, @role);
    DECLARE @now DATETIME2 = SYSUTCDATETIME();

    IF @ins = 1 AND @del = 0
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'INSERT', N'Booking', i.BOOKING_ID,
               LEFT(CONCAT(@actor, N' created Booking #', i.BOOKING_ID, N': ', i.STATUS, N' request with ',
                           COALESCE(p.BUSINESS_NAME, CONCAT(N'provider #', i.PROVIDER_ID)),
                           N' on ', CONVERT(NCHAR(10), i.BOOKING_DATE, 23), N' at ', CONVERT(NCHAR(5), i.BOOKING_TIME, 108),
                           N', PHP ', CONVERT(NVARCHAR(30), CAST(i.TOTAL_FEE AS MONEY), 1), N'.'), 500), @now
        FROM inserted i
        LEFT JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = i.PROVIDER_ID
        ORDER BY i.BOOKING_ID;

    IF @ins = 1 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'Booking', i.BOOKING_ID,
               LEFT(CONCAT(@actor, N' updated Booking #', i.BOOKING_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''', f.NOTE, N'.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
        -- v2.11: completed together with its proof (the photo was empty before); v2.12: started (STARTED_AT was
        -- empty before), or cancelled from Accepted (only a provider no-show may do that)
        CROSS APPLY (SELECT CASE WHEN d.STATUS <> N'Completed' AND i.STATUS = N'Completed'
                                      AND d.COMPLETION_PHOTO_URL IS NULL AND i.COMPLETION_PHOTO_URL IS NOT NULL
                                 THEN 1 ELSE 0 END AS WITH_PROOF,
                            CASE WHEN d.STATUS = N'Accepted' AND i.STATUS = N'In Progress'
                                      AND d.STARTED_AT IS NULL AND i.STARTED_AT IS NOT NULL
                                 THEN 1 ELSE 0 END AS STARTED,
                            CASE WHEN d.STATUS = N'Accepted' AND i.STATUS = N'Cancelled' THEN 1 ELSE 0 END AS NO_SHOW) x
        CROSS APPLY (VALUES
            (1, N'status', d.STATUS, i.STATUS,
                CASE WHEN x.WITH_PROOF = 1 THEN N' (proof photo and completion message saved)'
                     WHEN x.STARTED = 1 AND i.LATE_REASON IS NOT NULL
                          THEN CONCAT(N' (late start: ', i.MINUTES_LATE, N' minutes late, reason ''', dbo.fn_AuditValue(i.LATE_REASON), N''')')
                     WHEN x.STARTED = 1 THEN N' (started on time)'
                     WHEN x.NO_SHOW = 1 THEN N' (Provider No-Show ' + NCHAR(8212) + N' missed scheduled start window)'
                     ELSE N'' END),
            (2, N'payment status', d.PAYMENT_STATUS, i.PAYMENT_STATUS, N''),
            (3, N'date', CONVERT(NVARCHAR(10), d.BOOKING_DATE, 23), CONVERT(NVARCHAR(10), i.BOOKING_DATE, 23), N''),
            (4, N'time', CONVERT(NVARCHAR(5), d.BOOKING_TIME, 108), CONVERT(NVARCHAR(5), i.BOOKING_TIME, 108), N''),
            (5, N'duration', CONCAT(d.TOTAL_DURATION_MINUTES, N' min'), CONCAT(i.TOTAL_DURATION_MINUTES, N' min'), N''),
            (6, N'total fee', N'PHP ' + CONVERT(NVARCHAR(30), CAST(d.TOTAL_FEE AS MONEY), 1), N'PHP ' + CONVERT(NVARCHAR(30), CAST(i.TOTAL_FEE AS MONEY), 1), N''),
            (7, N'location', d.SERVICE_LOCATION, i.SERVICE_LOCATION, N''),
            (8, N'request description', d.SERVICE_DESCRIPTION, i.SERVICE_DESCRIPTION, N''),
            (9, N'completion message', d.COMPLETION_MESSAGE, i.COMPLETION_MESSAGE, N''),
            (10, N'proof photo', d.COMPLETION_PHOTO_URL, i.COMPLETION_PHOTO_URL, N''),
            (11, N'start time (Cebu)', CONVERT(NVARCHAR(16), DATEADD(HOUR, 8, d.STARTED_AT), 120), CONVERT(NVARCHAR(16), DATEADD(HOUR, 8, i.STARTED_AT), 120), N''),
            (12, N'late reason', d.LATE_REASON, i.LATE_REASON, N'')
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE, NOTE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
          AND NOT (x.WITH_PROOF = 1 AND f.SEQ IN (9, 10))          -- said once, on the status line
          AND NOT (x.STARTED = 1 AND f.SEQ IN (11, 12))            -- v2.12: the same for the start
        ORDER BY i.BOOKING_ID, f.SEQ;

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'Booking', d.BOOKING_ID,
               LEFT(CONCAT(@actor, N' deleted Booking #', d.BOOKING_ID, N': ', d.STATUS, N' booking on ',
                           CONVERT(NCHAR(10), d.BOOKING_DATE, 23), N' at ', CONVERT(NCHAR(5), d.BOOKING_TIME, 108),
                           N', PHP ', CONVERT(NVARCHAR(30), CAST(d.TOTAL_FEE AS MONEY), 1), N'.'), 500), @now
        FROM deleted d
        ORDER BY d.BOOKING_ID;
END";

    // ------------------------------------------------------------------ usp_UpdateBookingStatus (v2.12)

    public const string UpdateBookingStatus = @"CREATE OR ALTER PROCEDURE dbo.usp_UpdateBookingStatus
    @ProviderUserId INT,
    @BookingId      INT,
    @NewStatus      NVARCHAR(50),
    @LateReason     NVARCHAR(600) = NULL       -- v2.12: required when starting more than 15 minutes late
AS
BEGIN
    /* SERVISYO-DB-V2
       The ASSIGNED provider (found from their USER_ID) moves one of their bookings:
       Pending -> Accepted | Rejected, Accepted -> In Progress (only after the customer paid or chose cash on
       completion), In Progress -> Completed. Accepting must not overlap another Accepted / In Progress job.
       v2.12: starting is allowed from the scheduled date and time (Cebu) up to 2 hours after it; more than 15 minutes
       late needs @LateReason (saved in LATE_REASON). STARTED_AT = now. Later than 2 hours: the window has expired.
       The customer's bell shows the PDF's ""Your booking #N is now <status>."" (usp_GetNotifications). Customers cancel with usp_CancelBooking. */
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @NewStatus = LTRIM(RTRIM(@NewStatus));
    SET @LateReason = NULLIF(LTRIM(RTRIM(REPLACE(@LateReason, NCHAR(13) + NCHAR(10), NCHAR(10)))), N'');

    DECLARE @ProviderId INT = (SELECT p.PROVIDER_ID
                               FROM dbo.SERVICE_PROVIDERS p
                               JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
                               WHERE p.USER_ID = @ProviderUserId
                                 AND u.ROLE = N'Service Provider'
                                 AND u.ACCOUNT_STATUS = N'Active'
                                 AND p.APPROVAL_STATUS = N'Approved');
    IF @ProviderId IS NULL
        THROW 50121, N'Only an active, approved service provider can update bookings.', 1;

    DECLARE @tc INT = @@TRANCOUNT;
    BEGIN TRY
        IF @tc = 0 BEGIN TRANSACTION;

        -- accepting: one provider's accepts are checked one at a time
        IF @NewStatus = N'Accepted'
            SELECT @ProviderId = PROVIDER_ID FROM dbo.SERVICE_PROVIDERS WITH (UPDLOCK, HOLDLOCK) WHERE PROVIDER_ID = @ProviderId;

        DECLARE @Current NVARCHAR(50), @Payment NVARCHAR(50), @CustomerUserId INT,
                @Date DATE, @Time TIME(0), @Minutes INT;
        SELECT @Current = b.STATUS, @Payment = b.PAYMENT_STATUS, @CustomerUserId = c.USER_ID,
               @Date = b.BOOKING_DATE, @Time = b.BOOKING_TIME, @Minutes = b.TOTAL_DURATION_MINUTES
        FROM dbo.BOOKINGS b WITH (UPDLOCK)
        JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
        WHERE b.BOOKING_ID = @BookingId
          AND b.PROVIDER_ID = @ProviderId;

        IF @Current IS NULL
            THROW 50139, N'That booking could not be found.', 1;

        IF NOT (   (@Current = N'Pending'     AND @NewStatus IN (N'Accepted', N'Rejected'))
                OR (@Current = N'Accepted'    AND @NewStatus = N'In Progress')
                OR (@Current = N'In Progress' AND @NewStatus = N'Completed'))
            THROW 50143, N'Invalid status change for the provider.', 1;

        IF @NewStatus = N'In Progress'
           AND @Payment <> N'Paid'
           AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL WHERE BOOKING_ID = @BookingId AND PAYMENT_STATUS = N'PendingCollection')
            THROW 50144, N'The booking is waiting for the customer to approve and pay the quotation, so it can''t start yet.', 1;

        -- v2.12: the start window (Philippine time; minutes counted like the CHECK CK_BOOKINGS_START)
        DECLARE @StartedAt DATETIME2 = SYSUTCDATETIME();
        DECLARE @MinutesAfter INT = DATEDIFF(MINUTE, CAST(@Date AS DATETIME) + CAST(@Time AS DATETIME), DATEADD(HOUR, 8, @StartedAt));
        IF @NewStatus = N'In Progress' AND @MinutesAfter < 0
            THROW 50156, N'Cannot start a booking before its scheduled date and time.', 1;
        IF @NewStatus = N'In Progress' AND @MinutesAfter > 120
            THROW 50157, N'Booking start window has expired.', 1;
        IF @NewStatus = N'In Progress' AND @MinutesAfter > 15 AND @LateReason IS NULL
            THROW 50158, N'Please enter the reason for the late start before marking the booking In Progress.', 1;
        IF @NewStatus = N'In Progress' AND @MinutesAfter > 15 AND LEN(@LateReason) > 500
            THROW 50159, N'Please keep the late reason to 500 characters or fewer.', 1;

        IF @NewStatus = N'Accepted'
           AND dbo.fn_HasBookingConflict(@ProviderId, @Date, @Time, @Minutes, @BookingId, 0) = 1
            THROW 50145, N'This booking overlaps another job that is already on your schedule.', 1;

        UPDATE dbo.BOOKINGS
        SET STATUS = @NewStatus,
            STARTED_AT = CASE WHEN @NewStatus = N'In Progress' THEN @StartedAt ELSE STARTED_AT END,
            LATE_REASON = CASE WHEN @NewStatus = N'In Progress' AND @MinutesAfter > 15 THEN @LateReason ELSE LATE_REASON END,
            UPDATED_AT = SYSUTCDATETIME()
        WHERE BOOKING_ID = @BookingId;

        -- v2.3: the customer's bell shows the new status from BOOKINGS (usp_GetNotifications)

        IF @tc = 0 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @tc = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END";

    // ------------------------------------------------------------------ usp_GetBookingsNeedingAttention (v2.12)

    public const string NeedingAttention = @"CREATE OR ALTER PROCEDURE dbo.usp_GetBookingsNeedingAttention
    @Top INT = 25
AS
BEGIN
    /* SERVISYO-DB-V2  (v2.3: replaces the BOOKING_REPORTS table, which no screen ever filled; v2.12: late starts, no-shows)
       The administrator's list of bookings that need a look, worked out from the data, newest first, at most 25:
         - its review was flagged, or gave 1 or 2 stars (an unhappy customer);
         - its date has passed (Cebu time) but it is still Pending or Accepted (nobody moved it on);
         - v2.12, booked for the last 7 days: it was started more than 15 minutes late (MINUTES_LATE), or it was
           cancelled because the provider did not start it within 2 hours (a provider no-show: a cancelled booking
           with a payment, see trg_Bookings_StatusFlow).
       TotalCount = how many bookings need attention in all (it can be more than the 25 rows shown); LateStartCount and
       NoShowCount = how many of them are late starts / no-shows (the admin dashboard shows those on their own row).
       The column names match the app's C# class. */
    SET NOCOUNT ON;

    IF @Top IS NULL OR @Top < 1 OR @Top > 25 SET @Top = 25;
    DECLARE @Today DATE = CAST(dbo.fn_ManilaNow() AS DATE);
    DECLARE @WeekAgo DATE = DATEADD(DAY, -7, @Today);

    SELECT TOP (@Top)
           b.BOOKING_ID AS BookingId,
           CAST(b.STATUS AS NVARCHAR(50)) AS Status,
           b.BOOKING_DATE AS BookingDate,
           CAST(CASE WHEN r.STATUS = N'Flagged' THEN CONCAT(N'Review flagged (', r.RATING, N' of 5 stars)')
                     WHEN r.RATING <= 2 THEN CONCAT(N'Low rating (', r.RATING, N' of 5 stars)')
                     WHEN k.KIND = 2 THEN N'Provider no-show (not started within 2 hours)'
                     WHEN k.KIND = 1 THEN CONCAT(N'Late start (', b.MINUTES_LATE, N' min late)')
                     ELSE CONCAT(N'Date passed, still ', b.STATUS) END AS NVARCHAR(100)) AS Reason,
           CAST(cu.FIRST_NAME + N' ' + cu.LAST_NAME AS NVARCHAR(255)) AS CustomerName,
           CAST(sp.BUSINESS_NAME AS NVARCHAR(255)) AS ProviderName,
           COUNT(*) OVER () AS TotalCount,
           SUM(CASE WHEN k.KIND = 1 THEN 1 ELSE 0 END) OVER () AS LateStartCount,
           SUM(CASE WHEN k.KIND = 2 THEN 1 ELSE 0 END) OVER () AS NoShowCount
    FROM dbo.BOOKINGS b
    JOIN dbo.CUSTOMERS c ON c.CUSTOMER_ID = b.CUSTOMER_ID
    JOIN dbo.USERS cu ON cu.USER_ID = c.USER_ID
    JOIN dbo.SERVICE_PROVIDERS sp ON sp.PROVIDER_ID = b.PROVIDER_ID
    LEFT JOIN dbo.REVIEWS r ON r.BOOKING_ID = b.BOOKING_ID AND r.STATUS <> N'Removed'
    -- what the row is about: 0 = a review or a passed date, 1 = a late start, 2 = a provider no-show
    CROSS APPLY (SELECT CASE WHEN r.REVIEW_ID IS NOT NULL AND (r.STATUS = N'Flagged' OR r.RATING <= 2) THEN 0
                             WHEN b.STATUS = N'Cancelled' THEN 2
                             WHEN b.MINUTES_LATE IS NOT NULL THEN 1
                             ELSE 0 END AS KIND) k
    WHERE (r.REVIEW_ID IS NOT NULL AND (r.STATUS = N'Flagged' OR r.RATING <= 2))
       OR (b.STATUS IN (N'Pending', N'Accepted') AND b.BOOKING_DATE < @Today)
       OR (b.BOOKING_DATE >= @WeekAgo AND b.MINUTES_LATE IS NOT NULL)
       OR (b.BOOKING_DATE >= @WeekAgo AND b.STATUS = N'Cancelled'
           AND EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL p WHERE p.BOOKING_ID = b.BOOKING_ID))
    ORDER BY b.BOOKING_DATE DESC, b.BOOKING_ID DESC;
END";

    // ------------------------------------------------------------------ usp_GetNotifications (from v2.8)

    /// Customers never see a booking cancelled by a suspension and by a no-show at once: a suspension only cancels
    /// Pending requests, which have no payment. (Every anchor is ONE line: a Windows checkout may have \r\n.)
    private const string SuspensionAnchor =
        "          AND EXISTS (SELECT 1 FROM dbo.ADMIN_ACTIONS a";

    private const string SuspensionPart =
        "          AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL d WHERE d.BOOKING_ID = b.BOOKING_ID)   -- v2.12: not a provider no-show\n";

    /// The customer's news about a no-show, placed before the cash-received part.
    private const string CustomerAnchor =
        "        -- customer: the provider confirmed receiving the cash";

    private const string CustomerPart =
        "        -- customer: the provider did not start the job within 2 hours of its time, so it was cancelled (v2.12)\n" +
        "        SELECT CONCAT(N''B'', b.BOOKING_ID), 1, N''booking-cancelled'',\n" +
        "               CONCAT(N''Your booking #'', b.BOOKING_ID, N'' on '', FORMAT(b.BOOKING_DATE, ''MMM d, yyyy'', ''en-US''),\n" +
        "                      N'' was cancelled because the provider did not start it within 2 hours of the scheduled time.'',\n" +
        "                      CASE WHEN pay.PAYMENT_STATUS = N''Refunded''\n" +
        "                           THEN CONCAT(N'' Your '', @Peso, FORMAT(b.TOTAL_FEE, ''#,##0.##'', ''en-US''), N'' test payment was refunded.'')\n" +
        "                           ELSE N'' Nothing was charged.'' END,\n" +
        "                      N'' Please book another provider.''),\n" +
        "               b.UPDATED_AT, b.BOOKING_ID\n" +
        "        FROM dbo.BOOKINGS b\n" +
        "        JOIN Pay pay ON pay.BOOKING_ID = b.BOOKING_ID\n" +
        "        WHERE @Role = N''Customer'' AND b.CUSTOMER_ID = @CustomerId AND b.STATUS = N''Cancelled''\n" +
        "        UNION ALL\n";

    /// The provider's "cancelled by the customer" is no longer said about a no-show; the no-show part follows it.
    private const string ProviderAnchor =
        "          AND NOT EXISTS (SELECT 1 FROM SuspensionCancel sc WHERE sc.BOOKING_ID = b.BOOKING_ID)";

    private const string ProviderPart =
        "\n" +
        "          AND NOT EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL d WHERE d.BOOKING_ID = b.BOOKING_ID)   -- v2.12: a no-show is the next part\n" +
        "        UNION ALL\n" +
        "        -- provider: not started within 2 hours of its time, so it was cancelled automatically (v2.12)\n" +
        "        SELECT CONCAT(N''B'', b.BOOKING_ID), 1, N''booking-cancelled'',\n" +
        "               CONCAT(N''Booking #'', b.BOOKING_ID, N'' was cancelled automatically (Provider No-Show): it was not started within 2 hours of its scheduled time.'',\n" +
        "                      CASE WHEN b.PAYMENT_STATUS = N''Refunded'' THEN N'' The test payment was refunded to the customer.'' ELSE N'''' END),\n" +
        "               b.UPDATED_AT, b.BOOKING_ID\n" +
        "        FROM dbo.BOOKINGS b\n" +
        "        WHERE @Role = N''Service Provider'' AND b.PROVIDER_ID = @ProviderId AND b.STATUS = N''Cancelled''\n" +
        "          AND EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL d WHERE d.BOOKING_ID = b.BOOKING_ID)";

    public static readonly string GetNotifications212 =
        ReplaceOnce(ReplaceOnce(ReplaceOnce(NameChangeRequestsSql.GetNotifications28,
            SuspensionAnchor, SuspensionPart + SuspensionAnchor),
            CustomerAnchor, CustomerPart + CustomerAnchor),
            ProviderAnchor, ProviderAnchor + ProviderPart);

    // ------------------------------------------------------------------ the migration steps

    /// The five objects in the order they are created (Up, LAST: after EF added the columns and the CHECK).
    public static readonly string[] CreateAll =
    {
        AuditStandardSql.Exec(StatusFlow),
        AuditStandardSql.Exec(BookingsAudit),
        AuditStandardSql.Exec(UpdateBookingStatus),
        AuditStandardSql.Exec(NeedingAttention),
        GetNotifications212
    };

    /// Down, FIRST (before EF removes the columns these read): the same five objects as they were before v2.12.
    public static readonly string[] RestoreOld =
    {
        Find(DerivedDataSql.CreateAll, "CREATE OR ALTER TRIGGER dbo.trg_Bookings_StatusFlow ON dbo.BOOKINGS"),   // v2.3
        AuditStandardSql.Exec(BookingCompletionSql.BookingsAudit),                                                 // v2.11
        Find(DerivedDataSql.CreateAll, "CREATE OR ALTER PROCEDURE dbo.usp_UpdateBookingStatus"),                   // v2.3
        Find(DerivedDataSql.CreateAll, "CREATE OR ALTER PROCEDURE dbo.usp_GetBookingsNeedingAttention"),           // v2.3
        NameChangeRequestsSql.GetNotifications28                                                                   // v2.8
    };

    private static string Find(IEnumerable<string> list, string start)
    {
        var found = list.Where(s => s.StartsWith("EXEC(N'" + start, StringComparison.Ordinal)).ToList();
        if (found.Count != 1)
            throw new InvalidOperationException("Database v2.12: the old version was not found exactly once: " + start);
        return found[0];
    }

    private static string ReplaceOnce(string sql, string oldText, string newText)
    {
        var at = sql.IndexOf(oldText, StringComparison.Ordinal);
        if (at < 0 || sql.IndexOf(oldText, at + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Database v2.12: a line to change was not found exactly once: " + oldText.Trim());
        return string.Concat(sql.AsSpan(0, at), newText, sql.AsSpan(at + oldText.Length));
    }
}
