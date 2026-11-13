namespace Servisyo.Web.Data;

/// Database v2.11 (EF migration "BookingCompletionProof"): a provider completes a job WITH proof. EF adds the three
/// BOOKINGS columns and CK_BOOKINGS_COMPLETION by itself; tools\add-booking-completion.ps1 adds these SQL steps:
///   Up:   trg_Bookings_Audit, v2.11 (the v2.10 trigger + the two proof columns; a completion with its proof is ONE line)
///   Down: trg_Bookings_Audit as it was in v2.10 (first, before EF removes the columns it reads)
/// The text is the SAME as docs/sql/DatabaseDesign-v2.sql (a test compares them).
public static class BookingCompletionSql
{
    /// The words added to the status line when a booking is completed together with its proof.
    public const string ProofNote = " (proof photo and completion message saved)";

    public const string BookingsAudit = @"CREATE OR ALTER TRIGGER dbo.trg_Bookings_Audit ON dbo.BOOKINGS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.11: the job's proof; v2.10: the standard SYSTEM_LOGS format, one line per changed field)
       New booking requests; every change of status, payment status, date, time, duration, fee, location or request
       description (old -> new); deleted bookings.
       v2.11: completing a job WITH its proof (COMPLETION_MESSAGE, COMPLETION_PHOTO_URL) writes ONE line:
         ""... updated Booking #31: status 'In Progress' -> 'Completed' (proof photo and completion message saved).""
       The message itself is not copied into SYSTEM_LOGS (one fact, one place: it is in BOOKINGS). A later change of
       the proof is logged old -> new like any other field. COMPLETED_AT is the time of that line, so it is not logged. */
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
        -- v2.11: completed together with its proof (the photo was empty before)
        CROSS APPLY (SELECT CASE WHEN d.STATUS <> N'Completed' AND i.STATUS = N'Completed'
                                      AND d.COMPLETION_PHOTO_URL IS NULL AND i.COMPLETION_PHOTO_URL IS NOT NULL
                                 THEN 1 ELSE 0 END AS WITH_PROOF) x
        CROSS APPLY (VALUES
            (1, N'status', d.STATUS, i.STATUS, CASE WHEN x.WITH_PROOF = 1 THEN N' (proof photo and completion message saved)' ELSE N'' END),
            (2, N'payment status', d.PAYMENT_STATUS, i.PAYMENT_STATUS, N''),
            (3, N'date', CONVERT(NVARCHAR(10), d.BOOKING_DATE, 23), CONVERT(NVARCHAR(10), i.BOOKING_DATE, 23), N''),
            (4, N'time', CONVERT(NVARCHAR(5), d.BOOKING_TIME, 108), CONVERT(NVARCHAR(5), i.BOOKING_TIME, 108), N''),
            (5, N'duration', CONCAT(d.TOTAL_DURATION_MINUTES, N' min'), CONCAT(i.TOTAL_DURATION_MINUTES, N' min'), N''),
            (6, N'total fee', N'PHP ' + CONVERT(NVARCHAR(30), CAST(d.TOTAL_FEE AS MONEY), 1), N'PHP ' + CONVERT(NVARCHAR(30), CAST(i.TOTAL_FEE AS MONEY), 1), N''),
            (7, N'location', d.SERVICE_LOCATION, i.SERVICE_LOCATION, N''),
            (8, N'request description', d.SERVICE_DESCRIPTION, i.SERVICE_DESCRIPTION, N''),
            (9, N'completion message', d.COMPLETION_MESSAGE, i.COMPLETION_MESSAGE, N''),
            (10, N'proof photo', d.COMPLETION_PHOTO_URL, i.COMPLETION_PHOTO_URL, N'')
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE, NOTE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
          AND NOT (x.WITH_PROOF = 1 AND f.SEQ IN (9, 10))          -- said once, on the status line
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

    /// Up, LAST: the v2.11 trigger.
    public static readonly string[] CreateAll = { AuditStandardSql.Exec(BookingsAudit) };

    /// Down, FIRST: the v2.10 trigger (it does not read the proof columns).
    public static readonly string[] RestoreOld = { AuditStandardSql.Exec(AuditStandardSql.BookingsAudit) };
}
