/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: create/update ONE sample booking so you can
   SEE each dashboard state in the browser before the real booking pages exist.

   - Safe to re-run. Run it again with a different @Stage to move the same
     sample booking to the next state. It does not delete anything.
   - It adds a clearly named sample provider ("Dev Plumbing (sample)") that
     CANNOT sign in (its password hash is not a real hash).
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-booking.sql
   (First edit the two DECLARE lines below.)
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- >>> EDIT THESE TWO LINES <<<
DECLARE @CustomerEmail NVARCHAR(255) = N'you@email.com';   -- the customer you registered
DECLARE @Stage         NVARCHAR(30)  = N'Waiting';         -- Waiting | QuotationReceived | Confirmed | InProgress | Completed
-- >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>

DECLARE @Status  NVARCHAR(50), @Payment NVARCHAR(50);
SELECT @Status = CASE @Stage
                   WHEN N'Waiting'           THEN N'Pending'
                   WHEN N'QuotationReceived' THEN N'Accepted'
                   WHEN N'Confirmed'         THEN N'Accepted'
                   WHEN N'InProgress'        THEN N'In Progress'
                   WHEN N'Completed'         THEN N'Completed' END,
       @Payment = CASE WHEN @Stage IN (N'Waiting', N'QuotationReceived') THEN N'Unpaid' ELSE N'Paid' END;
IF @Status IS NULL THROW 50001, 'Unknown @Stage value.', 1;

DECLARE @CustomerId INT = (SELECT c.CUSTOMER_ID FROM CUSTOMERS c JOIN USERS u ON u.USER_ID = c.USER_ID
                           WHERE u.EMAIL = LOWER(@CustomerEmail));
IF @CustomerId IS NULL THROW 50002, 'No customer found with that email. Register it in the website first.', 1;

BEGIN TRAN;

-- Category
DECLARE @CategoryId INT = (SELECT TOP 1 CATEGORY_ID FROM SERVICE_CATEGORIES WHERE CATEGORY_NAME = N'Plumbing');
IF @CategoryId IS NULL
BEGIN
    INSERT INTO SERVICE_CATEGORIES (CATEGORY_NAME, DESCRIPTION, IS_ACTIVE) VALUES (N'Plumbing', N'Leaks, pipes, fixtures and installation.', 1);
    SET @CategoryId = SCOPE_IDENTITY();
END

-- Sample provider user (cannot sign in: 'AAAA' is not a valid password hash)
DECLARE @ProviderEmail NVARCHAR(255) = N'dev.provider@servisyo.test';
DECLARE @ProviderUserId INT = (SELECT USER_ID FROM USERS WHERE EMAIL = @ProviderEmail);
IF @ProviderUserId IS NULL
BEGIN
    INSERT INTO USERS (FIRST_NAME, LAST_NAME, EMAIL, PASSWORD_HASH, ROLE, ACCOUNT_STATUS)
    VALUES (N'Dev', N'Provider', @ProviderEmail, N'AAAA', N'Service Provider', N'Active');
    SET @ProviderUserId = SCOPE_IDENTITY();
END

-- Sample provider (approved)
DECLARE @ProviderId INT = (SELECT PROVIDER_ID FROM SERVICE_PROVIDERS WHERE USER_ID = @ProviderUserId);
IF @ProviderId IS NULL
BEGIN
    INSERT INTO SERVICE_PROVIDERS (USER_ID, BUSINESS_NAME, DESCRIPTION, CITY, REVIEW_COUNT, MIN_NOTICE_DAYS,
                                   MAX_JOBS_PER_DAY, APPROVAL_STATUS, DATE_APPLIED, DATE_APPROVED)
    VALUES (@ProviderUserId, N'Dev Plumbing (sample)', N'Sample provider for development.', N'Cebu City', 0, 0,
            5, N'Approved', SYSUTCDATETIME(), SYSUTCDATETIME());
    SET @ProviderId = SCOPE_IDENTITY();
END

-- Weekly schedule Mon-Sat 08:00-17:00 (only days that do not exist yet)
INSERT INTO PROVIDER_AVAILABILITY (PROVIDER_ID, DAY_OF_WEEK, IS_AVAILABLE, START_TIME, END_TIME)
SELECT @ProviderId, d.N, 1, '08:00', '17:00'
FROM (VALUES (1),(2),(3),(4),(5),(6)) AS d(N)
WHERE NOT EXISTS (SELECT 1 FROM PROVIDER_AVAILABILITY a WHERE a.PROVIDER_ID = @ProviderId AND a.DAY_OF_WEEK = d.N);

-- Sample service
DECLARE @ServiceId INT = (SELECT TOP 1 SERVICE_ID FROM SERVICES WHERE PROVIDER_ID = @ProviderId AND SERVICE_NAME = N'Pipe Repair');
IF @ServiceId IS NULL
BEGIN
    INSERT INTO SERVICES (PROVIDER_ID, CATEGORY_ID, SERVICE_NAME, DESCRIPTION, PRICE, DURATION_MINUTES, IS_ACTIVE)
    VALUES (@ProviderId, @CategoryId, N'Pipe Repair', N'Sample service.', 850, 120, 1);
    SET @ServiceId = SCOPE_IDENTITY();
END

-- The sample booking: tomorrow (Manila time) at 14:00. Created once, then updated on re-runs.
DECLARE @Tomorrow DATE = DATEADD(DAY, 1, CAST(DATEADD(HOUR, 8, SYSUTCDATETIME()) AS DATE));
DECLARE @BookingId INT = (SELECT TOP 1 BOOKING_ID FROM BOOKINGS
                          WHERE CUSTOMER_ID = @CustomerId AND PROVIDER_ID = @ProviderId ORDER BY BOOKING_ID DESC);
IF @BookingId IS NULL
BEGIN
    INSERT INTO BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES,
                          SERVICE_LOCATION, SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS)
    VALUES (@CustomerId, @ProviderId, @Tomorrow, '14:00', 120,
            N'Sample address, Cebu City', N'Sample booking for development.', 850, @Status, @Payment);
    SET @BookingId = SCOPE_IDENTITY();

    INSERT INTO BOOKING_SERVICES (BOOKING_ID, SERVICE_ID, PRICE_SNAPSHOT, DURATION_MINUTES_SNAPSHOT)
    VALUES (@BookingId, @ServiceId, 850, 120);
END
ELSE
BEGIN
    UPDATE BOOKINGS SET STATUS = @Status, PAYMENT_STATUS = @Payment, UPDATED_AT = SYSUTCDATETIME()
    WHERE BOOKING_ID = @BookingId;
END

COMMIT;

SELECT @BookingId AS BookingId, @Stage AS Stage, @Status AS Status, @Payment AS PaymentStatus;
