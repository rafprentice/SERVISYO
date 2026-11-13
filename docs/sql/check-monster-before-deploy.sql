/* SERVISYO - READ-ONLY check of an online (MonsterASP) database BEFORE importing deploy-latest.sql.
   It changes nothing in your tables (it only fills a list in memory, @out).

   Run it the same way as deploy-latest.sql (Databases > your database > Import), or in SSMS (see the Messages tab),
   then send the messages to Claude.
     FIX  ...  stops the update: fix it first
     INFO ...  only for you to know: what the update will change
   The last line says READY or NOT READY.

   It checks the same things the update steps check, for every step the database does not have yet:
     v2.1 ProfileCleanup  every phone number must become 09XXXXXXXXX, names at most 100 characters
     v2.2 AdminTargets    every admin log row must point to something that still exists
     v2.3 DerivedData     every payment must be convertible
     v2.5 OneDayOffPerDate only one provider may be off on any date
     v2.6 MaxJobsPerDay20 (INFO) providers above 20 jobs a day become 20
     v2.10 AuditStandard  SYSTEM_LOGS may only have INSERT / UPDATE / DELETE / LOGIN lines (v2.9 REPORT lines are removed),
                          and no object made by someone else may have one of the new audit names
   v2.7 DayOffRequests and v2.8 NameChangeRequests only add new (empty) tables, and v2.9 InactiveAccounts only adds
   two USERS columns (filled from the sign-in log): there is nothing to check for them. */
SET NOCOUNT ON;
DECLARE @out TABLE (SEQ INT IDENTITY(1, 1) PRIMARY KEY, KIND NVARCHAR(4) NOT NULL, LINE NVARCHAR(1000) NOT NULL);

DECLARE @last NVARCHAR(150) = (SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC);
INSERT @out (KIND, LINE) VALUES (N'INFO', N'Database ' + DB_NAME() + N'. Last update step it has: ' + ISNULL(@last, N'(none)') + N'.');

-- ------------------------------------------------------------------ v2.1 ProfileCleanup
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]ProfileCleanup')
BEGIN
    INSERT @out (KIND, LINE)
    SELECT N'FIX', CONCAT(N'v2.1 phone: USER_ID ', u.USER_ID, N' (', u.[ROLE], N', ', u.FIRST_NAME, N' ', u.LAST_NAME,
                          N') has the phone number "', u.PHONE_NUMBER, N'", which is not a mobile number 09XXXXXXXXX.')
    FROM dbo.USERS u
    CROSS APPLY (SELECT NULLIF(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(u.PHONE_NUMBER)), N' ', N''), N'-', N''), N'.', N''), N'(', N''), N')', N''), N'') AS S) a
    CROSS APPLY (SELECT CASE WHEN LEFT(a.S, 3) = N'+63' THEN N'0' + SUBSTRING(a.S, 4, 50)
                             WHEN LEFT(a.S, 2) = N'63' AND LEN(a.S) = 12 THEN N'0' + SUBSTRING(a.S, 3, 50)
                             WHEN LEFT(a.S, 1) = N'9' AND LEN(a.S) = 10 THEN N'0' + a.S
                             ELSE a.S END AS P) x
    WHERE x.P IS NOT NULL AND (LEN(x.P) <> 11 OR x.P NOT LIKE N'09[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]')
    ORDER BY u.USER_ID;

    INSERT @out (KIND, LINE)
    SELECT N'FIX', CONCAT(N'v2.1 name: USER_ID ', USER_ID, N' has a first, middle or last name longer than 100 characters.')
    FROM dbo.USERS
    WHERE LEN(FIRST_NAME) > 100 OR LEN(LAST_NAME) > 100 OR LEN(ISNULL(MIDDLE_NAME, N'')) > 100
    ORDER BY USER_ID;
END

-- ------------------------------------------------------------------ v2.2 AdminTargets (old columns: read with EXEC)
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]AdminTargets')
    INSERT @out (KIND, LINE) EXEC (N'
    SELECT N''FIX'', CONCAT(N''v2.2 admin log: ACTION_ID '', a.ACTION_ID, N'' points to '', a.TARGET_TYPE, N'' '', a.TARGET_ID,
                          N'', which no longer exists (or is both a customer and a provider).'')
    FROM dbo.ADMIN_ACTIONS a
    LEFT JOIN dbo.CUSTOMERS c           ON a.TARGET_TYPE = N''USER''             AND c.USER_ID = a.TARGET_ID
    LEFT JOIN dbo.SERVICE_PROVIDERS p   ON a.TARGET_TYPE = N''USER''             AND p.USER_ID = a.TARGET_ID
    LEFT JOIN dbo.SERVICE_PROVIDERS sp  ON a.TARGET_TYPE = N''SERVICE_PROVIDER'' AND sp.PROVIDER_ID = a.TARGET_ID
    LEFT JOIN dbo.REVIEWS r             ON a.TARGET_TYPE = N''REVIEW''           AND r.REVIEW_ID = a.TARGET_ID
    LEFT JOIN dbo.SERVICE_CATEGORIES sc ON a.TARGET_TYPE = N''CATEGORY''         AND sc.CATEGORY_ID = a.TARGET_ID
    WHERE NOT (   (a.TARGET_TYPE = N''USER'' AND c.CUSTOMER_ID IS NOT NULL AND p.PROVIDER_ID IS NULL)
               OR (a.TARGET_TYPE = N''USER'' AND p.PROVIDER_ID IS NOT NULL AND c.CUSTOMER_ID IS NULL)
               OR (a.TARGET_TYPE = N''SERVICE_PROVIDER'' AND sp.PROVIDER_ID IS NOT NULL)
               OR (a.TARGET_TYPE = N''REVIEW'' AND r.REVIEW_ID IS NOT NULL)
               OR (a.TARGET_TYPE = N''CATEGORY'' AND sc.CATEGORY_ID IS NOT NULL))
    ORDER BY a.ACTION_ID;');

-- ------------------------------------------------------------------ v2.3 DerivedData (old columns and tables: read with EXEC)
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]DerivedData')
    INSERT @out (KIND, LINE) EXEC (N'
    SELECT N''FIX'', CONCAT(N''v2.3 payment: PAYMENT_ID '', x.PAYMENT_ID, N'' (booking '', x.BOOKING_ID, N''): '', x.PROBLEM, N''.'')
    FROM (
        SELECT p.PAYMENT_ID, p.BOOKING_ID,
               CASE
                   WHEN p.PAYMENT_METHOD NOT IN (N''CashOnCompletion'', N''MockEWallet'')
                     OR (p.PAYMENT_METHOD = N''MockEWallet'' AND p.TRANSACTION_REFERENCE NOT LIKE N''GCASH-%'' AND p.TRANSACTION_REFERENCE NOT LIKE N''MAYA-%'')
                       THEN N''the payment method is unknown''
                   WHEN p.AMOUNT <> b.TOTAL_FEE
                       THEN N''the amount is not the booking''''s TOTAL_FEE''
                   WHEN p.PAYMENT_METHOD = N''CashOnCompletion'' AND cash.PAYMENT_ID IS NULL
                       THEN N''a cash payment without its CASH_PAYMENT_DETAILS row''
                   WHEN p.PAYMENT_METHOD = N''CashOnCompletion''
                        AND cash.IS_COLLECTED_BY_PROVIDER <> CASE WHEN p.PAYMENT_STATUS = N''Paid'' THEN 1 ELSE 0 END
                       THEN N''"collected" does not match the payment status''
               END AS PROBLEM
        FROM dbo.PAYMENTS p
        JOIN dbo.BOOKINGS b ON b.BOOKING_ID = p.BOOKING_ID
        LEFT JOIN dbo.CASH_PAYMENT_DETAILS cash ON cash.PAYMENT_ID = p.PAYMENT_ID
    ) x
    WHERE x.PROBLEM IS NOT NULL
    ORDER BY x.PAYMENT_ID;');
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]DerivedData')
    INSERT @out (KIND, LINE) EXEC (N'
    SELECT N''INFO'', CONCAT(N''v2.3 converts '', (SELECT COUNT(*) FROM dbo.PAYMENTS), N'' payment(s) into PAYMENTS_DTL and removes the tables ''
        + N''NOTIFICATIONS ('', (SELECT COUNT(*) FROM dbo.NOTIFICATIONS), N'' rows, now calculated), ''
        + N''EMAIL_OTPS ('', (SELECT COUNT(*) FROM dbo.EMAIL_OTPS), N'' rows, temporary codes), ''
        + N''BOOKING_REPORTS ('', (SELECT COUNT(*) FROM dbo.BOOKING_REPORTS), N'' rows, never filled by the app) and ''
        + N''CASH_PAYMENT_DETAILS ('', (SELECT COUNT(*) FROM dbo.CASH_PAYMENT_DETAILS), N'' rows, now calculated). Keep your BAK backup.'');');

-- ------------------------------------------------------------------ v2.5 OneDayOffPerDate
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]OneDayOffPerDate')
    INSERT @out (KIND, LINE)
    SELECT N'FIX', CONCAT(N'v2.5 day off: ', CONVERT(CHAR(10), d.UNAVAILABLE_DATE, 120), N' is off for PROVIDER_ID ', d.PROVIDER_ID,
                          N' (', u.FIRST_NAME, N' ', u.LAST_NAME, N'), UNAVAILABLE_ID ', d.UNAVAILABLE_ID,
                          N'. Only one provider may keep this date: remove it for the others.')
    FROM dbo.PROVIDER_UNAVAILABLE_DATES d
    JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = d.PROVIDER_ID
    JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
    WHERE d.UNAVAILABLE_DATE IN (SELECT UNAVAILABLE_DATE FROM dbo.PROVIDER_UNAVAILABLE_DATES GROUP BY UNAVAILABLE_DATE HAVING COUNT(*) > 1)
    ORDER BY d.UNAVAILABLE_DATE, d.UNAVAILABLE_ID;

-- ------------------------------------------------------------------ v2.6 MaxJobsPerDay20 (information only)
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]MaxJobsPerDay20')
    INSERT @out (KIND, LINE)
    SELECT N'INFO', CONCAT(N'v2.6: PROVIDER_ID ', p.PROVIDER_ID, N' (', u.FIRST_NAME, N' ', u.LAST_NAME, N') accepts ', p.MAX_JOBS_PER_DAY,
                           N' jobs a day; the update sets it to 20.')
    FROM dbo.SERVICE_PROVIDERS p
    JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
    WHERE p.MAX_JOBS_PER_DAY > 20
    ORDER BY p.PROVIDER_ID;

-- ------------------------------------------------------------------ v2.10 AuditStandard
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]AuditStandard')
BEGIN
    INSERT @out (KIND, LINE)
    SELECT N'FIX', CONCAT(N'v2.10 system logs: ', COUNT(*), N' line(s) have ACTION_TYPE "', ACTION_TYPE,
                          N'", but only INSERT, UPDATE, DELETE and LOGIN are allowed. Send this line to Claude.')
    FROM dbo.SYSTEM_LOGS
    WHERE ACTION_TYPE NOT IN (N'INSERT', N'UPDATE', N'DELETE', N'LOGIN', N'REPORT')
    GROUP BY ACTION_TYPE;

    INSERT @out (KIND, LINE)
    SELECT N'INFO', CONCAT(N'v2.10 removes ', COUNT(*), N' REPORT line(s) of the v2.9 inactive-account report; the admin dashboard shows that number live now.')
    FROM dbo.SYSTEM_LOGS
    WHERE ACTION_TYPE = N'REPORT'
    HAVING COUNT(*) > 0;

    INSERT @out (KIND, LINE)
    SELECT N'FIX', CONCAT(N'v2.10 name taken: ', o.name, N' (', o.type_desc, N') already exists and was not made by SERVISYO. Rename or drop it first.')
    FROM sys.objects o
    WHERE o.schema_id = SCHEMA_ID(N'dbo')
      AND o.name IN (N'fn_AuditValue', N'fn_AuditActor', N'trg_Users_Audit', N'trg_Customers_Audit', N'trg_Providers_Audit', N'trg_Services_Audit',
                     N'trg_Bookings_Audit', N'trg_PaymentsDtl_Audit', N'trg_Reviews_Audit', N'trg_UnavailableDates_Audit', N'trg_DayOffRequests_Audit',
                     N'trg_NameChangeRequests_Audit')
      AND ISNULL(OBJECT_DEFINITION(o.object_id), N'') NOT LIKE N'%SERVISYO-DB-V2%'
      AND NOT (o.name IN (N'trg_Users_Audit', N'trg_Bookings_Audit', N'trg_Services_Audit')
               AND ISNULL(OBJECT_DEFINITION(o.object_id), N'') LIKE N'%servisyo[_]user[_]id%')
    ORDER BY o.name;
END

-- ------------------------------------------------------------------ the report
DECLARE @fix INT = (SELECT COUNT(*) FROM @out WHERE KIND = N'FIX');
INSERT @out (KIND, LINE)
SELECT N'', CASE WHEN @fix = 0 THEN N'READY: nothing blocks the update. Import deploy-latest.sql (Stop the website first).'
                 ELSE CONCAT(N'NOT READY: ', @fix, N' thing(s) to fix first (the FIX lines). Nothing was changed.') END;

DECLARE @seq INT = (SELECT MIN(SEQ) FROM @out), @line NVARCHAR(1010);
WHILE @seq IS NOT NULL
BEGIN
    SELECT @line = CASE WHEN KIND = N'' THEN LINE ELSE KIND + N'  ' + LINE END FROM @out WHERE SEQ = @seq;
    PRINT @line;
    SET @seq = (SELECT MIN(SEQ) FROM @out WHERE SEQ > @seq);
END
