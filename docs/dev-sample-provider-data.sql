/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: fill YOUR provider account with sample data
   so you can SEE the Provider Dashboard before the real booking pages exist.

   What it does (for the provider account whose email you type below):
     1. Marks that provider as Approved       (shortcut - the real approval
                                               screen comes in Stage 5)
     2. Adds 4 sample services if missing     (Drain Unclogging, Pipe Repair,
                                               Waterproofing, Faucet Replacement)
     3. Adds a Mon-Sat 08:00-17:00 schedule   (only for days that have none)
     4. Adds 3 sample customers that CANNOT sign in (dev.*@servisyo.test)
     5. Adds 8 sample bookings: 2 Pending, 2 Accepted (one starts in ~30 min),
        1 In Progress, 2 Completed (with Visible reviews), 1 Rejected
     6. Recalculates your provider's AVG_RATING and REVIEW_COUNT

   Every sample booking is marked "DEV SAMPLE" in SERVICE_DESCRIPTION.
   - Safe to re-run: if sample bookings already exist it adds nothing.
   - To rebuild them with fresh dates, set @Refresh = 1. That deletes ONLY the
     bookings (and their reviews) marked "DEV SAMPLE" for YOUR provider.
     It never touches any other booking.
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-provider-data.sql
   (First edit the two DECLARE lines below.)
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- >>> EDIT THESE TWO LINES <<<
DECLARE @ProviderEmail NVARCHAR(155) = N'provider@email.com';  -- the PROVIDER account you registered
DECLARE @Refresh       BIT           = 0;                       -- 1 = rebuild the DEV SAMPLE bookings
-- >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>

DECLARE @ProviderId INT = (SELECT p.PROVIDER_ID
                           FROM SERVICE_PROVIDERS p JOIN USERS u ON u.USER_ID = p.USER_ID
                           WHERE u.EMAIL = LOWER(@ProviderEmail) AND u.[ROLE] = N'Service Provider');
IF @ProviderId IS NULL THROW 50001, 'No service-provider account found with that email. Register it on the website first (Register as a provider).', 1;

DECLARE @NowManila DATETIME2(0) = DATEADD(HOUR, 8, SYSUTCDATETIME());   -- Manila/Cebu wall-clock time
DECLARE @Today     DATE         = CAST(@NowManila AS DATE);

BEGIN TRAN;

-- 1. Approve (development shortcut)
UPDATE SERVICE_PROVIDERS
SET APPROVAL_STATUS = N'Approved', DATE_APPROVED = COALESCE(DATE_APPROVED, SYSUTCDATETIME())
WHERE PROVIDER_ID = @ProviderId AND APPROVAL_STATUS <> N'Approved';

-- 2. Category + services
DECLARE @CategoryId INT = (SELECT TOP 1 CATEGORY_ID FROM SERVICE_CATEGORIES WHERE CATEGORY_NAME = N'Plumbing');
IF @CategoryId IS NULL
BEGIN
    INSERT INTO SERVICE_CATEGORIES (CATEGORY_NAME, DESCRIPTION, IS_ACTIVE)
    VALUES (N'Plumbing', N'Leaks, pipes, fixtures and installation.', 1);
    SET @CategoryId = SCOPE_IDENTITY();
END

DECLARE @Svc TABLE (Name NVARCHAR(100), Price DECIMAL(10,2), Minutes INT);
INSERT INTO @Svc VALUES (N'Drain Unclogging', 600, 60), (N'Pipe Repair', 850, 120),
                        (N'Waterproofing', 700, 90),    (N'Faucet Replacement', 500, 60);

INSERT INTO SERVICES (PROVIDER_ID, CATEGORY_ID, SERVICE_NAME, DESCRIPTION, PRICE, DURATION_MINUTES, IS_ACTIVE)
SELECT @ProviderId, @CategoryId, s.Name, N'Sample service.', s.Price, s.Minutes, 1
FROM @Svc s
WHERE NOT EXISTS (SELECT 1 FROM SERVICES x WHERE x.PROVIDER_ID = @ProviderId AND x.SERVICE_NAME = s.Name);

DECLARE @SvcDrain  INT = (SELECT TOP 1 SERVICE_ID FROM SERVICES WHERE PROVIDER_ID = @ProviderId AND SERVICE_NAME = N'Drain Unclogging'  ORDER BY SERVICE_ID);
DECLARE @SvcPipe   INT = (SELECT TOP 1 SERVICE_ID FROM SERVICES WHERE PROVIDER_ID = @ProviderId AND SERVICE_NAME = N'Pipe Repair'       ORDER BY SERVICE_ID);
DECLARE @SvcWater  INT = (SELECT TOP 1 SERVICE_ID FROM SERVICES WHERE PROVIDER_ID = @ProviderId AND SERVICE_NAME = N'Waterproofing'    ORDER BY SERVICE_ID);
DECLARE @SvcFaucet INT = (SELECT TOP 1 SERVICE_ID FROM SERVICES WHERE PROVIDER_ID = @ProviderId AND SERVICE_NAME = N'Faucet Replacement' ORDER BY SERVICE_ID);

-- 3. Weekly schedule Mon-Sat 08:00-17:00 (only days that do not exist yet)
INSERT INTO PROVIDER_AVAILABILITY (PROVIDER_ID, DAY_OF_WEEK, IS_AVAILABLE, START_TIME, END_TIME)
SELECT @ProviderId, d.N, 1, '08:00', '17:00'
FROM (VALUES (1),(2),(3),(4),(5),(6)) AS d(N)
WHERE NOT EXISTS (SELECT 1 FROM PROVIDER_AVAILABILITY a WHERE a.PROVIDER_ID = @ProviderId AND a.DAY_OF_WEEK = d.N);

-- 4. Sample customers (cannot sign in: 'AAAA' is not a valid password hash)
DECLARE @DevCustomers TABLE (Email NVARCHAR(155), FirstName NVARCHAR(155), LastName NVARCHAR(155), City NVARCHAR(50));
INSERT INTO @DevCustomers VALUES
    (N'dev.elena@servisyo.test', N'Elena', N'Cruz',   N'Cebu City'),
    (N'dev.ana@servisyo.test',   N'Ana',   N'Reyes',  N'Mandaue City'),
    (N'dev.ria@servisyo.test',   N'Ria',   N'Santos', N'Lapu-Lapu City');

INSERT INTO USERS (FIRST_NAME, LAST_NAME, EMAIL, PASSWORD_HASH, [ROLE], ACCOUNT_STATUS)
SELECT d.FirstName, d.LastName, d.Email, N'AAAA', N'Customer', N'Active'
FROM @DevCustomers d
WHERE NOT EXISTS (SELECT 1 FROM USERS u WHERE u.EMAIL = d.Email);

INSERT INTO CUSTOMERS (USER_ID, CITY)
SELECT u.USER_ID, d.City
FROM @DevCustomers d JOIN USERS u ON u.EMAIL = d.Email
WHERE NOT EXISTS (SELECT 1 FROM CUSTOMERS c WHERE c.USER_ID = u.USER_ID);

DECLARE @Elena INT = (SELECT c.CUSTOMER_ID FROM CUSTOMERS c JOIN USERS u ON u.USER_ID = c.USER_ID WHERE u.EMAIL = N'dev.elena@servisyo.test');
DECLARE @Ana   INT = (SELECT c.CUSTOMER_ID FROM CUSTOMERS c JOIN USERS u ON u.USER_ID = c.USER_ID WHERE u.EMAIL = N'dev.ana@servisyo.test');
DECLARE @Ria   INT = (SELECT c.CUSTOMER_ID FROM CUSTOMERS c JOIN USERS u ON u.USER_ID = c.USER_ID WHERE u.EMAIL = N'dev.ria@servisyo.test');

-- 5. Sample bookings
IF @Refresh = 1
BEGIN
    DELETE r FROM REVIEWS r JOIN BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
    WHERE b.PROVIDER_ID = @ProviderId AND b.SERVICE_DESCRIPTION LIKE N'DEV SAMPLE%';

    DELETE bs FROM BOOKING_SERVICES bs JOIN BOOKINGS b ON b.BOOKING_ID = bs.BOOKING_ID
    WHERE b.PROVIDER_ID = @ProviderId AND b.SERVICE_DESCRIPTION LIKE N'DEV SAMPLE%';

    DELETE FROM BOOKINGS
    WHERE PROVIDER_ID = @ProviderId AND SERVICE_DESCRIPTION LIKE N'DEV SAMPLE%';
END

IF EXISTS (SELECT 1 FROM BOOKINGS WHERE PROVIDER_ID = @ProviderId AND SERVICE_DESCRIPTION LIKE N'DEV SAMPLE%')
BEGIN
    PRINT 'Sample bookings already exist - nothing added. Set @Refresh = 1 to rebuild them with fresh dates.';
END
ELSE
BEGIN
    -- "Starts in about 30 minutes" (never on top of another sample slot)
    DECLARE @Soon DATETIME2(0) = DATEADD(MINUTE, 30, @NowManila);
    DECLARE @SoonDate DATE = CAST(@Soon AS DATE);
    DECLARE @SoonTime TIME(0) = CAST(DATEADD(SECOND, -DATEPART(SECOND, @Soon), @Soon) AS TIME(0));
    WHILE @SoonTime IN ('08:07', '15:07') AND @SoonDate = @Today
        SET @SoonTime = DATEADD(MINUTE, 1, @SoonTime);

    DECLARE @Plan TABLE (
        Slot INT, CustomerId INT, Svc1 INT, Svc2 INT,
        BookingDate DATE, BookingTime TIME(0),
        Status NVARCHAR(50), Payment NVARCHAR(50),
        Location NVARCHAR(150), Note NVARCHAR(100),
        CreatedMinutesAgo INT, Rating TINYINT, Comment NVARCHAR(255));

    INSERT INTO @Plan VALUES
     (1, @Ana,   @SvcPipe,   @SvcWater, DATEADD(DAY,  1, @Today), '10:00', N'Pending',     N'Unpaid', N'Mandaue City',   N'new request',        5,     NULL, NULL),
     (2, @Ria,   @SvcFaucet, NULL,      DATEADD(DAY,  1, @Today), '14:00', N'Pending',     N'Unpaid', N'Lapu-Lapu City', N'new request',        20,    NULL, NULL),
     (3, @Elena, @SvcDrain,  NULL,      @SoonDate,                @SoonTime, N'Accepted',   N'Unpaid', N'12 Mango Ave, Cebu City', N'starts soon', 1440,  NULL, NULL),
     (4, @Elena, @SvcWater,  NULL,      @Today,                   '15:07', N'Accepted',     N'Unpaid', N'12 Mango Ave, Cebu City', N'later today', 2880,  NULL, NULL),
     (5, @Ria,   @SvcPipe,   NULL,      @Today,                   '08:07', N'In Progress',  N'Paid',   N'45 Osmena Blvd, Lapu-Lapu City', N'in progress', 4320, NULL, NULL),
     (6, @Elena, @SvcDrain,  NULL,      DATEADD(DAY, -5, @Today), '09:00', N'Completed',    N'Paid',   N'12 Mango Ave, Cebu City', N'completed',  8000,  4,
         N'Very professional and fast. Fixed the drain in under an hour. Minor issue with cleanup.'),
     (7, @Ria,   @SvcFaucet, NULL,      DATEADD(DAY, -8, @Today), '14:00', N'Completed',    N'Paid',   N'45 Osmena Blvd, Lapu-Lapu City', N'completed', 12000, 5,
         N'Neat work and a fair price. Will book again.'),
     (8, @Ana,   @SvcPipe,   NULL,      DATEADD(DAY,  3, @Today), '16:00', N'Rejected',     N'Unpaid', N'Mandaue City',   N'declined',           9000,  NULL, NULL);

    DECLARE @Slot INT, @CustId INT, @S1 INT, @S2 INT, @Date DATE, @Time TIME(0),
            @St NVARCHAR(50), @Pay NVARCHAR(50), @Loc NVARCHAR(150), @Note NVARCHAR(100),
            @Ago INT, @Rating TINYINT, @Comment NVARCHAR(255),
            @Dur INT, @Fee DECIMAL(10,2), @BookingId INT;

    DECLARE plan_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT Slot, CustomerId, Svc1, Svc2, BookingDate, BookingTime, Status, Payment,
               Location, Note, CreatedMinutesAgo, Rating, Comment
        FROM @Plan ORDER BY Slot;
    OPEN plan_cur;
    FETCH NEXT FROM plan_cur INTO @Slot, @CustId, @S1, @S2, @Date, @Time, @St, @Pay, @Loc, @Note, @Ago, @Rating, @Comment;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SELECT @Dur = SUM(DURATION_MINUTES), @Fee = SUM(PRICE)
        FROM SERVICES WHERE SERVICE_ID IN (@S1, ISNULL(@S2, @S1));

        INSERT INTO BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES,
                              SERVICE_LOCATION, SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT)
        VALUES (@CustId, @ProviderId, @Date, @Time, @Dur,
                @Loc, N'DEV SAMPLE - ' + @Note, @Fee, @St, @Pay, DATEADD(MINUTE, -@Ago, SYSUTCDATETIME()));
        SET @BookingId = SCOPE_IDENTITY();

        INSERT INTO BOOKING_SERVICES (BOOKING_ID, SERVICE_ID, PRICE_SNAPSHOT, DURATION_MINUTES_SNAPSHOT)
        SELECT @BookingId, SERVICE_ID, PRICE, DURATION_MINUTES
        FROM SERVICES WHERE SERVICE_ID IN (@S1, ISNULL(@S2, @S1));

        IF @Rating IS NOT NULL
            INSERT INTO REVIEWS (BOOKING_ID, RATING, [COMMENT], CREATED_AT, STATUS)
            VALUES (@BookingId, @Rating, @Comment, DATEADD(DAY, 1, CAST(@Date AS DATETIME2(0))), N'Visible');

        FETCH NEXT FROM plan_cur INTO @Slot, @CustId, @S1, @S2, @Date, @Time, @St, @Pay, @Loc, @Note, @Ago, @Rating, @Comment;
    END

    CLOSE plan_cur;
    DEALLOCATE plan_cur;
END

-- 6. Recalculate the provider's rating from VISIBLE reviews only
UPDATE SERVICE_PROVIDERS
SET REVIEW_COUNT = (SELECT COUNT(*) FROM REVIEWS r JOIN BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
                    WHERE b.PROVIDER_ID = @ProviderId AND r.STATUS = N'Visible'),
    AVG_RATING   = (SELECT CAST(ROUND(AVG(CAST(r.RATING AS DECIMAL(4,2))), 1) AS DECIMAL(2,1))
                    FROM REVIEWS r JOIN BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
                    WHERE b.PROVIDER_ID = @ProviderId AND r.STATUS = N'Visible')
WHERE PROVIDER_ID = @ProviderId;

COMMIT;

-- Summary
SELECT STATUS, COUNT(*) AS Bookings FROM BOOKINGS WHERE PROVIDER_ID = @ProviderId GROUP BY STATUS;
SELECT BUSINESS_NAME, APPROVAL_STATUS, AVG_RATING, REVIEW_COUNT FROM SERVICE_PROVIDERS WHERE PROVIDER_ID = @ProviderId;
