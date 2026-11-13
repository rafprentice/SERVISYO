/* SERVISYO - the audit SQL of database v2.10 ("AuditStandard"), readable (no EXEC wrapping).
   The migration installs it from src/Servisyo.Web/Data/AuditStandardSql.cs; docs/sql/DatabaseDesign-v2.sql has the
   same text (PART 2 and PART 5). Format: see docs/audit-standard-notes.md.
   The original v1 triggers (AddSystemLogs migration) are in src/Servisyo.Web/Data/AuditTriggersSql.cs. */

-- fn_AuditValue
CREATE OR ALTER FUNCTION dbo.fn_AuditValue (@Value NVARCHAR(MAX))
RETURNS NVARCHAR(160)
WITH SCHEMABINDING
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10)
       One value as a SYSTEM_LOGS line shows it: '(none)' when it is empty, long text cut to 150 characters. */
    RETURN CASE WHEN NULLIF(LTRIM(RTRIM(@Value)), N'') IS NULL THEN N'(none)'
                WHEN LEN(@Value) > 150 THEN LEFT(@Value, 147) + N'...'
                ELSE @Value
           END;
END
GO

-- fn_AuditActor
CREATE OR ALTER FUNCTION dbo.fn_AuditActor (@UserId INT, @Role NVARCHAR(50))
RETURNS NVARCHAR(560)
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10)
       Who made a change, as every SYSTEM_LOGS line starts: the role and the full name ("Customer Ana Reyes",
       "Administrator Ada Lim"), or only the role when nobody is signed in ("Guest" = someone registering,
       "System" = the app itself, a scheduled job or SSMS). */
    RETURN CONCAT(ISNULL(NULLIF(LTRIM(RTRIM(@Role)), N''), N'System'), N' ' + dbo.fn_GetFullName(@UserId));
END
GO

-- trg_Users_Audit
CREATE OR ALTER TRIGGER dbo.trg_Users_Audit ON dbo.USERS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10: the standard SYSTEM_LOGS format, one line per changed field)
       New accounts; every change of name, email, phone, account status or role (old -> new); password changes
       (never the password itself); removed accounts. Sign-ins are logged by the app (LOGIN). LAST_ACTIVITY_AT,
       INACTIVITY_WARNED_AT and NOTIFICATIONS_SEEN_AT change at every sign-in or visit and are not logged. */
    SET NOCOUNT ON;
    -- the inactive-account clean-up turns the audit off for its own changes (it writes one DELETE line itself)
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
        SELECT @uid, @role, N'INSERT', N'User', i.USER_ID,
               LEFT(CONCAT(@actor, N' created User #', i.USER_ID, N': ', i.ROLE, N' account ', i.EMAIL, N'.'), 500), @now
        FROM inserted i
        ORDER BY i.USER_ID;

    IF @ins = 1 AND @del = 1
    BEGIN
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'User', i.USER_ID,
               LEFT(CONCAT(@actor, N' updated User #', i.USER_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.USER_ID = i.USER_ID
        CROSS APPLY (VALUES
            (1, N'first name', d.FIRST_NAME, i.FIRST_NAME),
            (2, N'middle name', d.MIDDLE_NAME, i.MIDDLE_NAME),
            (3, N'last name', d.LAST_NAME, i.LAST_NAME),
            (4, N'email', d.EMAIL, i.EMAIL),
            (5, N'phone', d.PHONE_NUMBER, i.PHONE_NUMBER),
            (6, N'account status', d.ACCOUNT_STATUS, i.ACCOUNT_STATUS),
            (7, N'role', d.ROLE, i.ROLE)
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
        ORDER BY i.USER_ID, f.SEQ;

        -- the password is never written anywhere: only that it changed
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'User', i.USER_ID,
               LEFT(CONCAT(@actor, N' updated User #', i.USER_ID, N': password changed.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.USER_ID = i.USER_ID
        WHERE i.PASSWORD_HASH COLLATE Latin1_General_100_BIN2 <> d.PASSWORD_HASH COLLATE Latin1_General_100_BIN2
        ORDER BY i.USER_ID;
    END

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'User', d.USER_ID,
               LEFT(CONCAT(@actor, N' deleted User #', d.USER_ID, N': ', d.ROLE, N' account ', d.EMAIL, N'.'), 500), @now
        FROM deleted d
        ORDER BY d.USER_ID;
END
GO

-- trg_Customers_Audit
CREATE OR ALTER TRIGGER dbo.trg_Customers_Audit ON dbo.CUSTOMERS
AFTER UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10)
       A customer's address and profile photo (old -> new). The account itself (name, email, phone, status) is logged by
       trg_Users_Audit; ACCOUNT_STATUS here is only a copy of USERS and is not logged twice. New customers are the
       "created User" line of their account. */
    SET NOCOUNT ON;
    IF TRY_CAST(SESSION_CONTEXT(N'servisyo_audit_off') AS INT) = 1 RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;
    DECLARE @uid INT = TRY_CAST(SESSION_CONTEXT(N'servisyo_user_id') AS INT);
    DECLARE @role NVARCHAR(50) = COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System');
    DECLARE @actor NVARCHAR(560) = dbo.fn_AuditActor(@uid, @role);
    DECLARE @now DATETIME2 = SYSUTCDATETIME();

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Customer', i.CUSTOMER_ID,
           LEFT(CONCAT(@actor, N' updated Customer #', i.CUSTOMER_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                       N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
    FROM inserted i
    JOIN deleted d ON d.CUSTOMER_ID = i.CUSTOMER_ID
    CROSS APPLY (VALUES
        (1, N'address', d.ADDRESS, i.ADDRESS),
        (2, N'profile photo', d.PROFILE_PHOTO, i.PROFILE_PHOTO)
    ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
    WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
    ORDER BY i.CUSTOMER_ID, f.SEQ;
END
GO

-- trg_Providers_Audit
CREATE OR ALTER TRIGGER dbo.trg_Providers_Audit ON dbo.SERVICE_PROVIDERS
AFTER UPDATE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10)
       A provider's business details (old -> new): business name, description, address, base fee, minimum notice,
       jobs per day, approval status (Pending -> Approved / Rejected) and profile photo. Not logged: AVG_RATING and
       REVIEW_COUNT (calculated by trg_Reviews_RatingSync), ACCOUNT_STATUS (a copy of USERS, logged there),
       DATE_APPLIED / DATE_APPROVED (they come with the approval status). */
    SET NOCOUNT ON;
    IF TRY_CAST(SESSION_CONTEXT(N'servisyo_audit_off') AS INT) = 1 RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;
    DECLARE @uid INT = TRY_CAST(SESSION_CONTEXT(N'servisyo_user_id') AS INT);
    DECLARE @role NVARCHAR(50) = COALESCE(TRY_CAST(SESSION_CONTEXT(N'servisyo_user_role') AS NVARCHAR(50)), N'System');
    DECLARE @actor NVARCHAR(560) = dbo.fn_AuditActor(@uid, @role);
    DECLARE @now DATETIME2 = SYSUTCDATETIME();

    INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
    SELECT @uid, @role, N'UPDATE', N'Provider', i.PROVIDER_ID,
           LEFT(CONCAT(@actor, N' updated Provider #', i.PROVIDER_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                       N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
    FROM inserted i
    JOIN deleted d ON d.PROVIDER_ID = i.PROVIDER_ID
    CROSS APPLY (VALUES
        (1, N'business name', d.BUSINESS_NAME, i.BUSINESS_NAME),
        (2, N'description', d.DESCRIPTION, i.DESCRIPTION),
        (3, N'address', d.ADDRESS, i.ADDRESS),
        (4, N'base fee', N'PHP ' + CONVERT(NVARCHAR(30), CAST(d.BASE_FEE AS MONEY), 1), N'PHP ' + CONVERT(NVARCHAR(30), CAST(i.BASE_FEE AS MONEY), 1)),
        (5, N'minimum notice days', CONVERT(NVARCHAR(11), d.MIN_NOTICE_DAYS), CONVERT(NVARCHAR(11), i.MIN_NOTICE_DAYS)),
        (6, N'max jobs per day', CONVERT(NVARCHAR(11), d.MAX_JOBS_PER_DAY), CONVERT(NVARCHAR(11), i.MAX_JOBS_PER_DAY)),
        (7, N'approval status', d.APPROVAL_STATUS, i.APPROVAL_STATUS),
        (8, N'profile photo', d.PROFILE_PHOTO, i.PROFILE_PHOTO)
    ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
    WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
    ORDER BY i.PROVIDER_ID, f.SEQ;
END
GO

-- trg_Services_Audit
CREATE OR ALTER TRIGGER dbo.trg_Services_Audit ON dbo.SERVICES
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10: the standard SYSTEM_LOGS format, one line per changed field)
       New services; every change of name, description, category, price, duration or active (old -> new); deleted services. */
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
        SELECT @uid, @role, N'INSERT', N'Service', i.SERVICE_ID,
               LEFT(CONCAT(@actor, N' created Service #', i.SERVICE_ID, N': ', i.SERVICE_NAME, N' (', c.CATEGORY_NAME, N'), PHP ',
                           CONVERT(NVARCHAR(30), CAST(i.PRICE AS MONEY), 1), N', ', i.DURATION_MINUTES, N' min.'), 500), @now
        FROM inserted i
        LEFT JOIN dbo.SERVICE_CATEGORIES c ON c.CATEGORY_ID = i.CATEGORY_ID
        ORDER BY i.SERVICE_ID;

    IF @ins = 1 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'Service', i.SERVICE_ID,
               LEFT(CONCAT(@actor, N' updated Service #', i.SERVICE_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.SERVICE_ID = i.SERVICE_ID
        LEFT JOIN dbo.SERVICE_CATEGORIES oc ON oc.CATEGORY_ID = d.CATEGORY_ID
        LEFT JOIN dbo.SERVICE_CATEGORIES nc ON nc.CATEGORY_ID = i.CATEGORY_ID
        CROSS APPLY (VALUES
            (1, N'name', d.SERVICE_NAME, i.SERVICE_NAME),
            (2, N'description', d.DESCRIPTION, i.DESCRIPTION),
            (3, N'category', oc.CATEGORY_NAME, nc.CATEGORY_NAME),
            (4, N'price', N'PHP ' + CONVERT(NVARCHAR(30), CAST(d.PRICE AS MONEY), 1), N'PHP ' + CONVERT(NVARCHAR(30), CAST(i.PRICE AS MONEY), 1)),
            (5, N'duration', CONCAT(d.DURATION_MINUTES, N' min'), CONCAT(i.DURATION_MINUTES, N' min')),
            (6, N'active', CASE WHEN d.IS_ACTIVE = 1 THEN N'Yes' ELSE N'No' END, CASE WHEN i.IS_ACTIVE = 1 THEN N'Yes' ELSE N'No' END)
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
        ORDER BY i.SERVICE_ID, f.SEQ;

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'Service', d.SERVICE_ID,
               LEFT(CONCAT(@actor, N' deleted Service #', d.SERVICE_ID, N': ', d.SERVICE_NAME, N' (PHP ',
                           CONVERT(NVARCHAR(30), CAST(d.PRICE AS MONEY), 1), N').'), 500), @now
        FROM deleted d
        ORDER BY d.SERVICE_ID;
END
GO

-- trg_Bookings_Audit
CREATE OR ALTER TRIGGER dbo.trg_Bookings_Audit ON dbo.BOOKINGS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10: the standard SYSTEM_LOGS format, one line per changed field)
       New booking requests; every change of status, payment status, date, time, duration, fee, location or request
       description (old -> new); deleted bookings. */
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
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.BOOKING_ID = i.BOOKING_ID
        CROSS APPLY (VALUES
            (1, N'status', d.STATUS, i.STATUS),
            (2, N'payment status', d.PAYMENT_STATUS, i.PAYMENT_STATUS),
            (3, N'date', CONVERT(NVARCHAR(10), d.BOOKING_DATE, 23), CONVERT(NVARCHAR(10), i.BOOKING_DATE, 23)),
            (4, N'time', CONVERT(NVARCHAR(5), d.BOOKING_TIME, 108), CONVERT(NVARCHAR(5), i.BOOKING_TIME, 108)),
            (5, N'duration', CONCAT(d.TOTAL_DURATION_MINUTES, N' min'), CONCAT(i.TOTAL_DURATION_MINUTES, N' min')),
            (6, N'total fee', N'PHP ' + CONVERT(NVARCHAR(30), CAST(d.TOTAL_FEE AS MONEY), 1), N'PHP ' + CONVERT(NVARCHAR(30), CAST(i.TOTAL_FEE AS MONEY), 1)),
            (7, N'location', d.SERVICE_LOCATION, i.SERVICE_LOCATION),
            (8, N'request description', d.SERVICE_DESCRIPTION, i.SERVICE_DESCRIPTION)
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
        ORDER BY i.BOOKING_ID, f.SEQ;

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'Booking', d.BOOKING_ID,
               LEFT(CONCAT(@actor, N' deleted Booking #', d.BOOKING_ID, N': ', d.STATUS, N' booking on ',
                           CONVERT(NCHAR(10), d.BOOKING_DATE, 23), N' at ', CONVERT(NCHAR(5), d.BOOKING_TIME, 108),
                           N', PHP ', CONVERT(NVARCHAR(30), CAST(d.TOTAL_FEE AS MONEY), 1), N'.'), 500), @now
        FROM deleted d
        ORDER BY d.BOOKING_ID;
END
GO

-- trg_PaymentsDtl_Audit
CREATE OR ALTER TRIGGER dbo.trg_PaymentsDtl_Audit ON dbo.PAYMENTS_DTL
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10: the standard SYSTEM_LOGS format; v2.3 made it from trg_Payments_Audit)
       Each booking's payment: the method chosen (cash on completion or test e-wallet), every change of status, method
       or reference (old -> new), deleted payments. The method name comes from PAYMENTS, the amount from BOOKINGS. */
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
        SELECT @uid, @role, N'INSERT', N'Payment', i.PAYMENT_DTL_ID,
               LEFT(CONCAT(@actor, N' created Payment #', i.PAYMENT_DTL_ID, N': booking #', i.BOOKING_ID, N', ', m.METHOD_NAME,
                           N', ', i.PAYMENT_STATUS, N', PHP ', CONVERT(NVARCHAR(30), CAST(b.TOTAL_FEE AS MONEY), 1),
                           N', ref ', i.TRANSACTION_REFERENCE, N'.'), 500), @now
        FROM inserted i
        LEFT JOIN dbo.PAYMENTS m ON m.PAYMENT_ID = i.PAYMENT_ID
        LEFT JOIN dbo.BOOKINGS b ON b.BOOKING_ID = i.BOOKING_ID
        ORDER BY i.PAYMENT_DTL_ID;

    IF @ins = 1 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'Payment', i.PAYMENT_DTL_ID,
               LEFT(CONCAT(@actor, N' updated Payment #', i.PAYMENT_DTL_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.PAYMENT_DTL_ID = i.PAYMENT_DTL_ID
        LEFT JOIN dbo.PAYMENTS om ON om.PAYMENT_ID = d.PAYMENT_ID
        LEFT JOIN dbo.PAYMENTS nm ON nm.PAYMENT_ID = i.PAYMENT_ID
        CROSS APPLY (VALUES
            (1, N'status', d.PAYMENT_STATUS, i.PAYMENT_STATUS),
            (2, N'method', om.METHOD_NAME, nm.METHOD_NAME),
            (3, N'reference', d.TRANSACTION_REFERENCE, i.TRANSACTION_REFERENCE)
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
        ORDER BY i.PAYMENT_DTL_ID, f.SEQ;

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'Payment', d.PAYMENT_DTL_ID,
               LEFT(CONCAT(@actor, N' deleted Payment #', d.PAYMENT_DTL_ID, N': booking #', d.BOOKING_ID, N', ',
                           d.PAYMENT_STATUS, N', ref ', d.TRANSACTION_REFERENCE, N'.'), 500), @now
        FROM deleted d
        ORDER BY d.PAYMENT_DTL_ID;
END
GO

-- trg_Reviews_Audit
CREATE OR ALTER TRIGGER dbo.trg_Reviews_Audit ON dbo.REVIEWS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10)
       New reviews; every change of status (Visible / Flagged / Removed), rating or comment (old -> new); deleted reviews.
       The provider's AVG_RATING / REVIEW_COUNT are kept right by trg_Reviews_RatingSync, not here. */
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
        SELECT @uid, @role, N'INSERT', N'Review', i.REVIEW_ID,
               LEFT(CONCAT(@actor, N' created Review #', i.REVIEW_ID, N': ', i.RATING, N'/5 for booking #', i.BOOKING_ID,
                           N', comment ''', dbo.fn_AuditValue(i.COMMENT), N'''.'), 500), @now
        FROM inserted i
        ORDER BY i.REVIEW_ID;

    IF @ins = 1 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'Review', i.REVIEW_ID,
               LEFT(CONCAT(@actor, N' updated Review #', i.REVIEW_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.REVIEW_ID = i.REVIEW_ID
        CROSS APPLY (VALUES
            (1, N'status', d.STATUS, i.STATUS),
            (2, N'rating', CONCAT(d.RATING, N'/5'), CONCAT(i.RATING, N'/5')),
            (3, N'comment', d.COMMENT, i.COMMENT)
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
        ORDER BY i.REVIEW_ID, f.SEQ;

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'Review', d.REVIEW_ID,
               LEFT(CONCAT(@actor, N' deleted Review #', d.REVIEW_ID, N': ', d.RATING, N'/5 for booking #', d.BOOKING_ID, N'.'), 500), @now
        FROM deleted d
        ORDER BY d.REVIEW_ID;
END
GO

-- trg_UnavailableDates_Audit
CREATE OR ALTER TRIGGER dbo.trg_UnavailableDates_Audit ON dbo.PROVIDER_UNAVAILABLE_DATES
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10)
       Days off that do NOT belong to a day-off request: an administrator's extra day (v2.4) or an older day off.
       The days of a request (REQUEST_ID filled) are logged once, through their request (trg_DayOffRequests_Audit),
       not one line per day. */
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
        SELECT @uid, @role, N'INSERT', N'Day Off', i.UNAVAILABLE_ID,
               LEFT(CONCAT(@actor, N' created Day Off #', i.UNAVAILABLE_ID, N': ', COALESCE(p.BUSINESS_NAME, CONCAT(N'provider #', i.PROVIDER_ID)),
                           N' is off on ', CONVERT(NCHAR(10), i.UNAVAILABLE_DATE, 23),
                           CASE WHEN i.APPROVED_BY_ACTION_ID IS NOT NULL THEN N' (extra day approved by an administrator)' ELSE N'' END,
                           N', reason ''', dbo.fn_AuditValue(i.REASON), N'''.'), 500), @now
        FROM inserted i
        LEFT JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = i.PROVIDER_ID
        WHERE i.REQUEST_ID IS NULL
        ORDER BY i.UNAVAILABLE_ID;

    IF @ins = 1 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'Day Off', i.UNAVAILABLE_ID,
               LEFT(CONCAT(@actor, N' updated Day Off #', i.UNAVAILABLE_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.UNAVAILABLE_ID = i.UNAVAILABLE_ID
        CROSS APPLY (VALUES
            (1, N'date', CONVERT(NVARCHAR(10), d.UNAVAILABLE_DATE, 23), CONVERT(NVARCHAR(10), i.UNAVAILABLE_DATE, 23)),
            (2, N'reason', d.REASON, i.REASON)
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
        WHERE i.REQUEST_ID IS NULL
          AND ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
        ORDER BY i.UNAVAILABLE_ID, f.SEQ;

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'Day Off', d.UNAVAILABLE_ID,
               LEFT(CONCAT(@actor, N' deleted Day Off #', d.UNAVAILABLE_ID, N': ', COALESCE(p.BUSINESS_NAME, CONCAT(N'provider #', d.PROVIDER_ID)),
                           N' off on ', CONVERT(NCHAR(10), d.UNAVAILABLE_DATE, 23), N'.'), 500), @now
        FROM deleted d
        LEFT JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = d.PROVIDER_ID
        WHERE d.REQUEST_ID IS NULL
        ORDER BY d.UNAVAILABLE_ID;
END
GO

-- trg_DayOffRequests_Audit
CREATE OR ALTER TRIGGER dbo.trg_DayOffRequests_Audit ON dbo.PROVIDER_DAY_OFF_REQUESTS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10)
       A provider's day-off request (v2.7): sent, answered (status Pending -> Approved / Rejected) or removed.
       Its dates are on Admin > Service Providers; who decided is in ADMIN_ACTIONS. */
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
        SELECT @uid, @role, N'INSERT', N'Day Off Request', i.REQUEST_ID,
               LEFT(CONCAT(@actor, N' created Day Off Request #', i.REQUEST_ID, N': ', COALESCE(p.BUSINESS_NAME, CONCAT(N'provider #', i.PROVIDER_ID)),
                           N', ', i.STATUS, N', reason ''', dbo.fn_AuditValue(i.REASON), N'''.'), 500), @now
        FROM inserted i
        LEFT JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = i.PROVIDER_ID
        ORDER BY i.REQUEST_ID;

    IF @ins = 1 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'Day Off Request', i.REQUEST_ID,
               LEFT(CONCAT(@actor, N' updated Day Off Request #', i.REQUEST_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.REQUEST_ID = i.REQUEST_ID
        CROSS APPLY (VALUES
            (1, N'status', d.STATUS, i.STATUS),
            (2, N'reason', d.REASON, i.REASON)
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
        ORDER BY i.REQUEST_ID, f.SEQ;

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'Day Off Request', d.REQUEST_ID,
               LEFT(CONCAT(@actor, N' deleted Day Off Request #', d.REQUEST_ID, N': ', d.STATUS, N' request of ',
                           COALESCE(p.BUSINESS_NAME, CONCAT(N'provider #', d.PROVIDER_ID)), N'.'), 500), @now
        FROM deleted d
        LEFT JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = d.PROVIDER_ID
        ORDER BY d.REQUEST_ID;
END
GO

-- trg_NameChangeRequests_Audit
CREATE OR ALTER TRIGGER dbo.trg_NameChangeRequests_Audit ON dbo.PROVIDER_NAME_CHANGE_REQUESTS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    /* SERVISYO-DB-V2 (v2.10)
       An approved provider's name change request (v2.8): sent, answered or removed. When it is approved, the new name
       in USERS is logged by trg_Users_Audit (first / middle / last name, old -> new). */
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
        SELECT @uid, @role, N'INSERT', N'Name Change Request', i.REQUEST_ID,
               LEFT(CONCAT(@actor, N' created Name Change Request #', i.REQUEST_ID, N': ', COALESCE(p.BUSINESS_NAME, CONCAT(N'provider #', i.PROVIDER_ID)),
                           N' asks for ''', CONCAT(i.FIRST_NAME, N' ' + NULLIF(i.MIDDLE_NAME, N''), N' ', i.LAST_NAME),
                           N''', reason ''', dbo.fn_AuditValue(i.REASON), N'''.'), 500), @now
        FROM inserted i
        LEFT JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = i.PROVIDER_ID
        ORDER BY i.REQUEST_ID;

    IF @ins = 1 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'UPDATE', N'Name Change Request', i.REQUEST_ID,
               LEFT(CONCAT(@actor, N' updated Name Change Request #', i.REQUEST_ID, N': ', f.FIELD_NAME, N' ''', dbo.fn_AuditValue(f.OLD_VALUE),
                           N''' -> ''', dbo.fn_AuditValue(f.NEW_VALUE), N'''.'), 500), @now
        FROM inserted i
        JOIN deleted d ON d.REQUEST_ID = i.REQUEST_ID
        CROSS APPLY (VALUES
            (1, N'status', d.STATUS, i.STATUS),
            (2, N'requested name', CONCAT(d.FIRST_NAME, N' ' + NULLIF(d.MIDDLE_NAME, N''), N' ', d.LAST_NAME),
                                   CONCAT(i.FIRST_NAME, N' ' + NULLIF(i.MIDDLE_NAME, N''), N' ', i.LAST_NAME)),
            (3, N'reason', d.REASON, i.REASON)
        ) f (SEQ, FIELD_NAME, OLD_VALUE, NEW_VALUE)
        WHERE ISNULL(f.OLD_VALUE, N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(f.NEW_VALUE, N'') COLLATE Latin1_General_100_BIN2
        ORDER BY i.REQUEST_ID, f.SEQ;

    IF @ins = 0 AND @del = 1
        INSERT INTO dbo.SYSTEM_LOGS (USER_ID, USER_ROLE, ACTION_TYPE, ENTITY_NAME, ENTITY_ID, DESCRIPTION, CREATED_AT)
        SELECT @uid, @role, N'DELETE', N'Name Change Request', d.REQUEST_ID,
               LEFT(CONCAT(@actor, N' deleted Name Change Request #', d.REQUEST_ID, N': ', d.STATUS, N' request of ',
                           COALESCE(p.BUSINESS_NAME, CONCAT(N'provider #', d.PROVIDER_ID)), N'.'), 500), @now
        FROM deleted d
        LEFT JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = d.PROVIDER_ID
        ORDER BY d.REQUEST_ID;
END
GO
