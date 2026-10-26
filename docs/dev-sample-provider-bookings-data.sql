/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: sample booking requests for ONE provider, so every tab of
   Provider > Booking Requests has something to show:

     All 9 | Pending 3 | Accepted 1 | In Progress 1 | Completed 2 | Rejected 1 | Cancelled 1

   WHICH PROVIDER? Set @ProviderEmail below to the e-mail of a provider you can SIGN IN as
   (an approved one - approve a new provider in Admin > Providers first). The sample providers
   from dev-sample-providers-data.sql (dev.marco@...) cannot sign in.

   RUN THESE FIRST:
     docs\dev-sample-users-data.sql      (customers Ana, Brent, Carla, Dennis, Elena)
     docs\dev-sample-services-data.sql   (the provider's four services: Pipe Repair, Fixture Install,
                                          Drain Unclogging, Water Heater Install)
   If something is missing, this script stops with a clear message and changes nothing.

   The dates are relative to TODAY, so the requests always look current:
     Pending      Fixture Install   in 8 days 2:00 PM   (Brent)
     Pending      Drain Unclogging  in 5 days 10:00 AM  (Carla)
     Pending      Fixture Install   in 6 days 9:30 AM   (Dennis)  <- overlaps the accepted Pipe Repair below:
                                                                    pressing Accept shows the overlap message
     Accepted     Pipe Repair       in 6 days 9:00 AM   (Ana)     2 hours -> ends 11:00 AM
     In Progress  Pipe Repair       today 8:00 AM       (Dennis)
     Completed    Drain Unclogging  9 days ago 11:00 AM (Elena)
     Completed    Pipe Repair       20 days ago 1:00 PM (Ana)
     Rejected     Fixture Install   14 days ago 9:00 AM (Brent)
     Cancelled    Water Heater Install 6 days ago 3:00 PM (Carla)

   - Every sample booking's notes start with "[DEV SAMPLE (provider bookings) #n]" so they can be found.
   - Safe to re-run: if the sample bookings already exist, nothing more is added.
   - @Refresh = 1 deletes ONLY those marked bookings (and their booking lines) and creates them again
     - use it to put every status back after you have clicked through the buttons.
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -I -i docs\dev-sample-provider-bookings-data.sql
   (-I = quoted identifiers ON, required when a script changes BOOKINGS)
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;   -- needed to change BOOKINGS (it has a filtered index); plain sqlcmd starts with this OFF

DECLARE @ProviderEmail NVARCHAR(155) = N'dev.marco@servisyo.test';   -- <-- change this to your own provider's e-mail
DECLARE @Refresh BIT = 0;      -- 1 = rebuild the "DEV SAMPLE (provider bookings)" bookings

DECLARE @ProviderId INT = (SELECT p.PROVIDER_ID
                           FROM SERVICE_PROVIDERS p JOIN USERS u ON u.USER_ID = p.USER_ID
                           WHERE u.EMAIL = @ProviderEmail AND p.APPROVAL_STATUS = N'Approved');

IF @ProviderId IS NULL
BEGIN
    DECLARE @NoProvider NVARCHAR(500) = N'No APPROVED provider has the e-mail ' + @ProviderEmail
        + N'. Change @ProviderEmail at the top of this script (and approve the provider first if needed).';
    THROW 50001, @NoProvider, 1;
END

DECLARE @Today DATE = CAST(SYSDATETIME() AS DATE);

-- ---------- The plan ----------
DECLARE @Plan TABLE (Slot INT, CustomerEmail NVARCHAR(155), ServiceName NVARCHAR(100), DayOffset INT, BTime TIME(0),
                     Status NVARCHAR(50), Payment NVARCHAR(50), CreatedDaysAgo INT, Location NVARCHAR(255), Notes NVARCHAR(180));
INSERT INTO @Plan VALUES
 (1, N'dev.ana@servisyo.test',    N'Pipe Repair',          6,   '09:00', N'Accepted',    N'Unpaid', 4,  N'Lahug, Cebu City',        N'Kitchen sink pipe is leaking under the cabinet. Please bring replacement fittings.'),
 (2, N'dev.brent@servisyo.test',  N'Fixture Install',      8,   '14:00', N'Pending',     N'Unpaid', 1,  N'Banilad, Cebu City',      N'Need a new faucet and shower head installed in the master bathroom.'),
 (3, N'dev.carla@servisyo.test',  N'Drain Unclogging',     5,   '10:00', N'Pending',     N'Unpaid', 2,  N'Mandaue City, Cebu',      N'Bathroom drain is slow and smells bad.'),
 (4, N'dev.dennis@servisyo.test', N'Pipe Repair',          0,   '08:00', N'In Progress', N'Unpaid', 3,  N'Talamban, Cebu City',     N'Burst pipe near the water meter.'),
 (5, N'dev.elena@servisyo.test',  N'Drain Unclogging',     -9,  '11:00', N'Completed',   N'Paid',   12, N'Guadalupe, Cebu City',    N'Kitchen sink completely blocked.'),
 (6, N'dev.ana@servisyo.test',    N'Pipe Repair',          -20, '13:00', N'Completed',   N'Paid',   24, N'Lahug, Cebu City',        N'Leaking pipe in the laundry area.'),
 (7, N'dev.brent@servisyo.test',  N'Fixture Install',      -14, '09:00', N'Rejected',    N'Unpaid', 16, N'Banilad, Cebu City',      N'Install two wall-mounted faucets.'),
 (8, N'dev.carla@servisyo.test',  N'Water Heater Install', -6,  '15:00', N'Cancelled',   N'Unpaid', 9,  N'Mandaue City, Cebu',      N'Electric water heater, 30 L.'),
 (9, N'dev.dennis@servisyo.test', N'Fixture Install',      6,   '09:30', N'Pending',     N'Unpaid', 1,  N'Talamban, Cebu City',     N'Replace the bathroom mixer tap (this one overlaps the accepted Pipe Repair - a test for the overlap rule).');

-- ---------- Check that the sample people and the provider's services exist (nothing has been changed yet) ----------
DECLARE @MissingCustomers NVARCHAR(MAX) = (
    SELECT STRING_AGG(e.Email, N', ')
    FROM (SELECT DISTINCT CustomerEmail AS Email FROM @Plan) e
    WHERE NOT EXISTS (SELECT 1 FROM USERS u JOIN CUSTOMERS c ON c.USER_ID = u.USER_ID WHERE u.EMAIL = e.Email));
DECLARE @MissingServices NVARCHAR(MAX) = (
    SELECT STRING_AGG(n.Name, N', ')
    FROM (SELECT DISTINCT ServiceName AS Name FROM @Plan) n
    WHERE NOT EXISTS (SELECT 1 FROM SERVICES s WHERE s.PROVIDER_ID = @ProviderId AND s.SERVICE_NAME = n.Name));

IF @MissingCustomers IS NOT NULL OR @MissingServices IS NOT NULL
BEGIN
    DECLARE @Msg NVARCHAR(2000) = N'Not ready. Run docs\dev-sample-users-data.sql and docs\dev-sample-services-data.sql first. '
        + N'Missing customers: ' + COALESCE(@MissingCustomers, N'none') + N'. Missing services for this provider: ' + COALESCE(@MissingServices, N'none') + N'.';
    THROW 50002, @Msg, 1;
END

-- ---------- No OTHER active booking may already hold one of the sample provider/date/time slots ----------
-- (the database allows only one Pending/Accepted/In Progress booking per provider, date and start time)
IF EXISTS (
    SELECT 1
    FROM @Plan pl
    JOIN BOOKINGS b ON b.PROVIDER_ID = @ProviderId
                   AND b.BOOKING_DATE = DATEADD(DAY, pl.DayOffset, @Today)
                   AND b.BOOKING_TIME = pl.BTime
    WHERE pl.Status IN (N'Pending', N'Accepted', N'In Progress')
      AND b.STATUS IN (N'Pending', N'Accepted', N'In Progress')
      AND ISNULL(b.SERVICE_DESCRIPTION, N'') NOT LIKE N'[[]DEV SAMPLE (provider bookings)%')
BEGIN
    THROW 50003, N'Another active booking of this provider already uses one of the sample date/time slots, so the sample bookings were not added.', 1;
END

BEGIN TRAN;

IF @Refresh = 1
BEGIN
    DELETE bs FROM BOOKING_SERVICES bs JOIN BOOKINGS b ON b.BOOKING_ID = bs.BOOKING_ID
    WHERE b.PROVIDER_ID = @ProviderId AND b.SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (provider bookings)%';
    DELETE FROM BOOKINGS
    WHERE PROVIDER_ID = @ProviderId AND SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (provider bookings)%';
END

IF EXISTS (SELECT 1 FROM BOOKINGS WHERE PROVIDER_ID = @ProviderId AND SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (provider bookings)%')
BEGIN
    PRINT 'Sample bookings already exist for this provider - nothing more added. Set @Refresh = 1 to rebuild them.';
END
ELSE
BEGIN
    -- one row per booking, in plan order, so the booking numbers grow in the same order
    DECLARE @Slot INT = 1;
    WHILE @Slot <= 9
    BEGIN
        INSERT INTO BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES,
                              SERVICE_LOCATION, SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT)
        SELECT c.CUSTOMER_ID, @ProviderId, DATEADD(DAY, pl.DayOffset, @Today), pl.BTime, s.DURATION_MINUTES,
               pl.Location,
               N'[DEV SAMPLE (provider bookings) #' + CAST(pl.Slot AS NVARCHAR(5)) + N'] ' + pl.Notes,
               s.PRICE, pl.Status, pl.Payment,
               DATEADD(MINUTE, pl.Slot, DATEADD(DAY, -pl.CreatedDaysAgo, SYSUTCDATETIME()))
        FROM @Plan pl
        JOIN USERS cu ON cu.EMAIL = pl.CustomerEmail
        JOIN CUSTOMERS c ON c.USER_ID = cu.USER_ID
        JOIN SERVICES s ON s.PROVIDER_ID = @ProviderId AND s.SERVICE_NAME = pl.ServiceName
        WHERE pl.Slot = @Slot;
        SET @Slot += 1;
    END

    INSERT INTO BOOKING_SERVICES (BOOKING_ID, SERVICE_ID, PRICE_SNAPSHOT, DURATION_MINUTES_SNAPSHOT)
    SELECT b.BOOKING_ID, s.SERVICE_ID, s.PRICE, s.DURATION_MINUTES
    FROM @Plan pl
    JOIN BOOKINGS b ON b.PROVIDER_ID = @ProviderId
                   AND b.SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (provider bookings) #' + CAST(pl.Slot AS NVARCHAR(5)) + N']%'
    JOIN SERVICES s ON s.PROVIDER_ID = @ProviderId AND s.SERVICE_NAME = pl.ServiceName;
END

COMMIT;

-- What the Booking Requests tabs will show for this provider
SELECT STATUS, COUNT(*) AS Bookings FROM BOOKINGS WHERE PROVIDER_ID = @ProviderId GROUP BY STATUS ORDER BY STATUS;
SELECT COUNT(*) AS TotalBookings FROM BOOKINGS WHERE PROVIDER_ID = @ProviderId;
