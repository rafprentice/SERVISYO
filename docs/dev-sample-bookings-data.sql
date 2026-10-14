/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: 8 sample bookings so the admin "All Bookings" page
   shows what the design shows (Pending 2, Accepted 1, In Progress 1, Completed 2,
   Rejected 1, Cancelled 1).

   RUN THESE TWO SCRIPTS FIRST (this script needs their sample people):
     docs\dev-sample-users-data.sql        (customers Ana, Brent, Carla, Dennis, Elena)
     docs\dev-sample-providers-data.sql    (providers Marco, Liza, Danilo, Rhea)
   If they are missing, this script stops with a clear message and changes nothing.

   The 8 bookings (dates are fixed, as in the design):
     #1 Ana    -> Marco   Pipe Repair        Sep 20, 2026  9:00 AM  Accepted     Paid
     #2 Brent  -> Liza    Deep Cleaning      Sep 22, 2026  8:00 AM  Pending      Unpaid
     #3 Dennis -> Danilo  AC Service         Sep 18, 2026  2:00 PM  In Progress  Paid
     #4 Ana    -> Rhea    Laundry            Sep 10, 2026 10:00 AM  Completed    Paid
     #5 Elena  -> Marco   Drain Unclogging   Sep 12, 2026 11:00 AM  Completed    Paid
     #6 Carla  -> Danilo  Appliance Repair   Sep  5, 2026  1:00 PM  Cancelled    Refunded
     #7 Dennis -> Liza    Office Cleaning    Sep 17, 2026  6:00 PM  Pending      Unpaid
     #8 Brent  -> Marco   Fixture Install    Sep  8, 2026  9:00 AM  Rejected     Unpaid
   (The "#" above is the sample number. The real booking numbers come from the database.)

   - Any sample service that does not exist yet is added to the provider first.
   - Safe to re-run: if these sample bookings already exist, nothing more is added.
   - @Refresh = 1 deletes ONLY the bookings marked "DEV SAMPLE (bookings)" (and their
     booking lines) and creates them again.
   - The page counts ALL bookings in the database, so bookings from the other sample
     scripts (or ones you made yourself) are included in the numbers.
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-bookings-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Refresh BIT = 0;      -- 1 = rebuild the "DEV SAMPLE (bookings)" bookings

-- ---------- The plan ----------
DECLARE @Plan TABLE (Slot INT, CustomerEmail NVARCHAR(155), ProviderEmail NVARCHAR(155), ServiceName NVARCHAR(100),
                     ServiceCategory NVARCHAR(100), ServicePrice DECIMAL(10,2), ServiceMinutes INT,
                     BDate DATE, BTime TIME(0), Status NVARCHAR(50), Payment NVARCHAR(50));
INSERT INTO @Plan VALUES
 (1, N'dev.ana@servisyo.test',    N'dev.marco@servisyo.test',  N'Pipe Repair',      N'Plumbing', 850,  120, '2026-09-20', '09:00', N'Accepted',    N'Paid'),
 (2, N'dev.brent@servisyo.test',  N'dev.liza@servisyo.test',   N'Deep Cleaning',    N'Cleaning', 1200, 180, '2026-09-22', '08:00', N'Pending',     N'Unpaid'),
 (3, N'dev.dennis@servisyo.test', N'dev.danilo@servisyo.test', N'AC Service',       N'Repair',   950,  90,  '2026-09-18', '14:00', N'In Progress', N'Paid'),
 (4, N'dev.ana@servisyo.test',    N'dev.rhea@servisyo.test',   N'Laundry',          N'Cleaning', 500,  120, '2026-09-10', '10:00', N'Completed',   N'Paid'),
 (5, N'dev.elena@servisyo.test',  N'dev.marco@servisyo.test',  N'Drain Unclogging', N'Plumbing', 650,  90,  '2026-09-12', '11:00', N'Completed',   N'Paid'),
 (6, N'dev.carla@servisyo.test',  N'dev.danilo@servisyo.test', N'Appliance Repair', N'Repair',   900,  90,  '2026-09-05', '13:00', N'Cancelled',   N'Refunded'),
 (7, N'dev.dennis@servisyo.test', N'dev.liza@servisyo.test',   N'Office Cleaning',  N'Cleaning', 2000, 240, '2026-09-17', '18:00', N'Pending',     N'Unpaid'),
 (8, N'dev.brent@servisyo.test',  N'dev.marco@servisyo.test',  N'Fixture Install',  N'Plumbing', 700,  90,  '2026-09-08', '09:00', N'Rejected',    N'Unpaid');

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

-- ---------- Check that no OTHER booking already holds one of the same provider/date/time slots ----------
-- (the database allows only one Pending/Accepted/In Progress booking per provider, date and start time)
IF EXISTS (
    SELECT 1
    FROM @Plan pl
    JOIN USERS pu ON pu.EMAIL = pl.ProviderEmail
    JOIN SERVICE_PROVIDERS p ON p.USER_ID = pu.USER_ID
    JOIN BOOKINGS b ON b.PROVIDER_ID = p.PROVIDER_ID AND b.BOOKING_DATE = pl.BDate AND b.BOOKING_TIME = pl.BTime
    WHERE pl.Status IN (N'Pending', N'Accepted', N'In Progress')
      AND b.STATUS IN (N'Pending', N'Accepted', N'In Progress')
      AND ISNULL(b.SERVICE_DESCRIPTION, N'') NOT LIKE N'DEV SAMPLE (bookings)%')
BEGIN
    THROW 50002, N'Another active booking already uses one of the sample provider/date/time slots, so the sample bookings were not added.', 1;
END

BEGIN TRAN;

-- ---------- Make sure every sample service exists (existing services are never changed) ----------
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

-- ---------- Bookings ----------
IF @Refresh = 1
BEGIN
    DELETE bs FROM BOOKING_SERVICES bs JOIN BOOKINGS b ON b.BOOKING_ID = bs.BOOKING_ID
    WHERE b.SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (bookings)%';
    DELETE FROM BOOKINGS WHERE SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (bookings)%';
END

IF EXISTS (SELECT 1 FROM BOOKINGS WHERE SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (bookings)%')
BEGIN
    PRINT 'Sample bookings already exist - nothing more added. Set @Refresh = 1 to rebuild them.';
END
ELSE
BEGIN
    -- one row per booking, in sample order, so the real booking numbers grow in the same order
    DECLARE @Slot INT = 1;
    WHILE @Slot <= 8
    BEGIN
        INSERT INTO BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES,
                              SERVICE_LOCATION, SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT)
        SELECT c.CUSTOMER_ID, p.PROVIDER_ID, pl.BDate, pl.BTime, s.DURATION_MINUTES,
               N'Sample address, Cebu City', N'DEV SAMPLE (bookings) #' + CAST(pl.Slot AS NVARCHAR(5)), s.PRICE,
               pl.Status, pl.Payment, DATEADD(DAY, pl.Slot - 9, SYSUTCDATETIME())
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
    JOIN BOOKINGS b ON b.SERVICE_DESCRIPTION = N'DEV SAMPLE (bookings) #' + CAST(pl.Slot AS NVARCHAR(5))
    JOIN SERVICES s ON s.PROVIDER_ID = b.PROVIDER_ID AND s.SERVICE_NAME = pl.ServiceName;
END

COMMIT;

-- What the All Bookings page will show (every booking in the database is counted)
SELECT STATUS, COUNT(*) AS Bookings FROM BOOKINGS GROUP BY STATUS ORDER BY STATUS;
SELECT COUNT(*) AS TotalBookings FROM BOOKINGS;
