/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: 3 sample reviews so the admin Reviews page shows what the
   design shows: two Visible reviews and one Flagged review.

   RUN THESE FIRST (this script needs their sample people):
     docs\dev-sample-users-data.sql        (customers Elena, Carla)
     docs\dev-sample-providers-data.sql    (providers Rhea, Marco, Danilo)
   If they are missing, this script stops with a clear message and changes nothing.

   Each review needs a COMPLETED booking, so this script also adds 3 completed bookings
   (marked "DEV SAMPLE (reviews)"):
     Elena -> Rhea   Laundry            5 stars  Visible  (submitted Sep 11, 2026)
     Elena -> Marco  Drain Unclogging   4 stars  Visible  (submitted Sep 13, 2026)
     Carla -> Danilo Appliance Repair   2 stars  FLAGGED  (submitted Sep 6, 2026)

   - Safe to re-run: if these sample reviews exist, nothing more is added.
   - @Refresh = 1 deletes ONLY the sample bookings marked "DEV SAMPLE (reviews)" (with their
     reviews and booking lines) and creates them again.
   - The provider ratings are NOT changed by this script. They are recalculated from the real
     Visible reviews the first time you press Keep / Hide / Restore on the page. (The sample
     providers script typed in made-up ratings such as 4.9 with 112 reviews, so you will see
     them change to the real numbers.)
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-reviews-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Refresh BIT = 0;      -- 1 = rebuild the "DEV SAMPLE (reviews)" bookings and reviews

DECLARE @Plan TABLE (Slot INT, CustomerEmail NVARCHAR(155), ProviderEmail NVARCHAR(155), ServiceName NVARCHAR(100),
                     ServiceCategory NVARCHAR(100), ServicePrice DECIMAL(10,2), ServiceMinutes INT,
                     BDate DATE, BTime TIME(0), Rating TINYINT, ReviewText NVARCHAR(255), ReviewStatus NVARCHAR(50),
                     ReviewedAt DATETIME2);
INSERT INTO @Plan VALUES
 (1, N'dev.elena@servisyo.test', N'dev.rhea@servisyo.test',   N'Laundry',          N'Cleaning', 500, 120, '2026-09-10', '10:00', 5,
     N'Rhea is amazing! Everything came back perfectly clean and folded. Will definitely book again.', N'Visible', '2026-09-11T04:00:00'),
 (2, N'dev.elena@servisyo.test', N'dev.marco@servisyo.test',  N'Drain Unclogging', N'Plumbing', 650, 90,  '2026-09-12', '11:00', 4,
     N'Very professional and fast. Fixed the drain in under an hour. Minor issue with cleanup afterward.', N'Visible', '2026-09-13T04:00:00'),
 (3, N'dev.carla@servisyo.test', N'dev.danilo@servisyo.test', N'Appliance Repair', N'Repair',   900, 90,  '2026-09-05', '13:00', 2,
     N'Very professional and fast. Fixed the drain in under an hour. Minor issue with cleanup afterward.', N'Flagged', '2026-09-06T04:00:00');

-- ---------- Check that the sample people exist (nothing has been changed yet) ----------
DECLARE @MissingCustomers NVARCHAR(MAX) = (
    SELECT STRING_AGG(e.Email, N', ')
    FROM (SELECT DISTINCT CustomerEmail AS Email FROM @Plan) e
    WHERE NOT EXISTS (SELECT 1 FROM USERS u JOIN CUSTOMERS c ON c.USER_ID = u.USER_ID WHERE u.EMAIL = e.Email));
DECLARE @MissingProviders NVARCHAR(MAX) = (
    SELECT STRING_AGG(e.Email, N', ')
    FROM (SELECT DISTINCT ProviderEmail AS Email FROM @Plan) e
    WHERE NOT EXISTS (SELECT 1 FROM USERS u JOIN SERVICE_PROVIDERS p ON p.USER_ID = u.USER_ID WHERE u.EMAIL = e.Email));

IF @MissingCustomers IS NOT NULL OR @MissingProviders IS NOT NULL
BEGIN
    DECLARE @Msg NVARCHAR(2000) = N'Sample people are missing. Run docs\dev-sample-users-data.sql and docs\dev-sample-providers-data.sql first. '
        + N'Missing customers: ' + COALESCE(@MissingCustomers, N'none') + N'. Missing providers: ' + COALESCE(@MissingProviders, N'none') + N'.';
    THROW 50001, @Msg, 1;
END

BEGIN TRAN;

-- ---------- Make sure the sample services exist (existing services are never changed) ----------
INSERT INTO SERVICE_CATEGORIES (CATEGORY_NAME, DESCRIPTION, IS_ACTIVE)
SELECT v.Name, v.Descr, 1
FROM (VALUES (N'Plumbing', N'Leaks, pipes, fixtures and installation.'),
             (N'Cleaning', N'Residential and commercial cleaning for homes and offices.'),
             (N'Repair',   N'Appliance, furniture and general home repairs.')) AS v(Name, Descr)
WHERE NOT EXISTS (SELECT 1 FROM SERVICE_CATEGORIES c WHERE c.CATEGORY_NAME = v.Name);

INSERT INTO SERVICES (PROVIDER_ID, CATEGORY_ID, SERVICE_NAME, DESCRIPTION, PRICE, DURATION_MINUTES, IS_ACTIVE)
SELECT DISTINCT p.PROVIDER_ID, c.CATEGORY_ID, pl.ServiceName, N'Sample service.', pl.ServicePrice, pl.ServiceMinutes, 1
FROM @Plan pl
JOIN USERS pu ON pu.EMAIL = pl.ProviderEmail
JOIN SERVICE_PROVIDERS p ON p.USER_ID = pu.USER_ID
JOIN SERVICE_CATEGORIES c ON c.CATEGORY_NAME = pl.ServiceCategory
WHERE NOT EXISTS (SELECT 1 FROM SERVICES x WHERE x.PROVIDER_ID = p.PROVIDER_ID AND x.SERVICE_NAME = pl.ServiceName);

-- ---------- Bookings + reviews ----------
IF @Refresh = 1
BEGIN
    DELETE r FROM REVIEWS r JOIN BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
    WHERE b.SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (reviews)%';
    DELETE bs FROM BOOKING_SERVICES bs JOIN BOOKINGS b ON b.BOOKING_ID = bs.BOOKING_ID
    WHERE b.SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (reviews)%';
    DELETE FROM BOOKINGS WHERE SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (reviews)%';
END

IF EXISTS (SELECT 1 FROM BOOKINGS WHERE SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (reviews)%')
BEGIN
    PRINT 'Sample reviews already exist - nothing more added. Set @Refresh = 1 to rebuild them.';
END
ELSE
BEGIN
    DECLARE @Slot INT = 1;
    WHILE @Slot <= 3
    BEGIN
        INSERT INTO BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES,
                              SERVICE_LOCATION, SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT)
        SELECT c.CUSTOMER_ID, p.PROVIDER_ID, pl.BDate, pl.BTime, s.DURATION_MINUTES,
               N'Sample address, Cebu City', N'DEV SAMPLE (reviews) #' + CAST(pl.Slot AS NVARCHAR(5)), s.PRICE,
               N'Completed', N'Paid', DATEADD(DAY, -1, pl.ReviewedAt)
        FROM @Plan pl
        JOIN USERS cu ON cu.EMAIL = pl.CustomerEmail
        JOIN CUSTOMERS c ON c.USER_ID = cu.USER_ID
        JOIN USERS pu ON pu.EMAIL = pl.ProviderEmail
        JOIN SERVICE_PROVIDERS p ON p.USER_ID = pu.USER_ID
        JOIN SERVICES s ON s.PROVIDER_ID = p.PROVIDER_ID AND s.SERVICE_NAME = pl.ServiceName
        WHERE pl.Slot = @Slot;
        SET @Slot += 1;
    END

    INSERT INTO BOOKING_SERVICES (BOOKING_ID, SERVICE_ID, PRICE_SNAPSHOT, DURATION_MINUTES_SNAPSHOT)
    SELECT b.BOOKING_ID, s.SERVICE_ID, s.PRICE, s.DURATION_MINUTES
    FROM @Plan pl
    JOIN BOOKINGS b ON b.SERVICE_DESCRIPTION = N'DEV SAMPLE (reviews) #' + CAST(pl.Slot AS NVARCHAR(5))
    JOIN SERVICES s ON s.PROVIDER_ID = b.PROVIDER_ID AND s.SERVICE_NAME = pl.ServiceName;

    INSERT INTO REVIEWS (BOOKING_ID, RATING, [COMMENT], STATUS, CREATED_AT)
    SELECT b.BOOKING_ID, pl.Rating, pl.ReviewText, pl.ReviewStatus, pl.ReviewedAt
    FROM @Plan pl
    JOIN BOOKINGS b ON b.SERVICE_DESCRIPTION = N'DEV SAMPLE (reviews) #' + CAST(pl.Slot AS NVARCHAR(5));
END

COMMIT;

-- What the Reviews page will show (every review in the database is counted)
SELECT STATUS, COUNT(*) AS Reviews FROM REVIEWS GROUP BY STATUS ORDER BY STATUS;
SELECT COUNT(*) AS TotalReviews FROM REVIEWS;
