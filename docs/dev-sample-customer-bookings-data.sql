/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: sample bookings for ONE customer, so every tab of
   Customer > My Bookings has something to show:

     All 7 | Pending 1 | Accepted 1 | In Progress 1 | Completed 2 | Rejected 1 | Cancelled 1

   WHICH CUSTOMER? Set @CustomerEmail below to the e-mail of a customer you can SIGN IN as
   (register one on the website first). The sample customers from dev-sample-users-data.sql
   (dev.ana@...) cannot sign in.

   RUN THIS FIRST:
     docs\dev-sample-providers-data.sql   (the sample providers Marco, Liza, Danilo and Rhea with their services)
   If something is missing, this script stops with a clear message and changes nothing.

   The dates are relative to TODAY, so the bookings always look current:
     Pending      Marco   Fixture Install    in 9 days 3:00 PM      <- press "Cancel Request" to try the cancel rule
     Accepted     Marco   Pipe Repair        in 7 days 1:00 PM
     In Progress  Liza    Window Cleaning    today 8:00 AM
     Completed    Rhea    Laundry            9 days ago 10:00 AM    <- no review yet: shows "Leave a Review"
     Completed    Danilo  Aircon Cleaning    20 days ago 1:00 PM    <- already reviewed
     Rejected     Marco   Leak Detection     14 days ago 9:00 AM
     Cancelled    Liza    Deep Cleaning      6 days ago 3:00 PM
   (Rhea's account is Suspended in the sample data: the customer's history still shows her booking.)

   - Every sample booking's notes start with "[DEV SAMPLE (customer bookings) #n]" so they can be found.
   - Safe to re-run: if the sample bookings already exist, nothing more is added.
   - @Refresh = 1 deletes ONLY those marked bookings of this customer (with their booking lines and reviews)
     and creates them again - use it to put every status back after you have cancelled the pending one.
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -I -i docs\dev-sample-customer-bookings-data.sql
   (-I = quoted identifiers ON, required when a script changes BOOKINGS)
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;   -- needed to change BOOKINGS (it has a filtered index); plain sqlcmd starts with this OFF

DECLARE @CustomerEmail NVARCHAR(155) = N'you@email.com';   -- <-- change this to your own customer's e-mail
DECLARE @Refresh BIT = 0;      -- 1 = rebuild the "DEV SAMPLE (customer bookings)" bookings

DECLARE @CustomerId INT = (SELECT c.CUSTOMER_ID
                           FROM CUSTOMERS c JOIN USERS u ON u.USER_ID = c.USER_ID
                           WHERE u.EMAIL = LOWER(@CustomerEmail));

IF @CustomerId IS NULL
BEGIN
    DECLARE @NoCustomer NVARCHAR(500) = N'No customer has the e-mail ' + @CustomerEmail
        + N'. Change @CustomerEmail at the top of this script (register the customer on the website first).';
    THROW 50001, @NoCustomer, 1;
END

DECLARE @Today DATE = CAST(SYSDATETIME() AS DATE);

-- ---------- The plan ----------
DECLARE @Plan TABLE (Slot INT, ProviderEmail NVARCHAR(155), ServiceName NVARCHAR(100), DayOffset INT, BTime TIME(0),
                     Status NVARCHAR(50), Payment NVARCHAR(50), CreatedDaysAgo INT, Location NVARCHAR(255), Notes NVARCHAR(180),
                     Rating TINYINT NULL);
INSERT INTO @Plan VALUES
 (1, N'dev.marco@servisyo.test',  N'Fixture Install',  9,   '15:00', N'Pending',     N'Unpaid', 1,  N'45 Mabini St, Quezon City', N'Need a new faucet and shower head installed in the main bathroom.', NULL),
 (2, N'dev.marco@servisyo.test',  N'Pipe Repair',      7,   '13:00', N'Accepted',    N'Unpaid', 4,  N'45 Mabini St, Quezon City', N'Kitchen sink pipe is leaking underneath the cabinet.', NULL),
 (3, N'dev.liza@servisyo.test',   N'Window Cleaning',  0,   '08:00', N'In Progress', N'Unpaid', 3,  N'45 Mabini St, Quezon City', N'All windows on the second floor, please bring a ladder.', NULL),
 (4, N'dev.rhea@servisyo.test',   N'Laundry',          -9,  '10:00', N'Completed',   N'Paid',   11, N'45 Mabini St, Quezon City', N'Weekly laundry service for a family of 4.', NULL),
 (5, N'dev.danilo@servisyo.test', N'Aircon Cleaning',  -20, '13:00', N'Completed',   N'Paid',   22, N'45 Mabini St, Quezon City', N'Two split-type aircon units, living room and bedroom.', 5),
 (6, N'dev.marco@servisyo.test',  N'Leak Detection',   -14, '09:00', N'Rejected',    N'Unpaid', 16, N'45 Mabini St, Quezon City', N'Water meter keeps running even when every tap is closed.', NULL),
 (7, N'dev.liza@servisyo.test',   N'Deep Cleaning',    -6,  '15:00', N'Cancelled',   N'Unpaid', 9,  N'45 Mabini St, Quezon City', N'Deep cleaning before the in-laws visit.', NULL);

-- ---------- Check that the sample providers and their services exist (nothing has been changed yet) ----------
DECLARE @Missing NVARCHAR(MAX) = (
    SELECT STRING_AGG(m.Item, N'; ')
    FROM (SELECT DISTINCT pl.ProviderEmail + N' / ' + pl.ServiceName AS Item
          FROM @Plan pl
          WHERE NOT EXISTS (SELECT 1
                            FROM SERVICES s
                            JOIN SERVICE_PROVIDERS p ON p.PROVIDER_ID = s.PROVIDER_ID
                            JOIN USERS u ON u.USER_ID = p.USER_ID
                            WHERE u.EMAIL = pl.ProviderEmail AND s.SERVICE_NAME = pl.ServiceName)) m);

IF @Missing IS NOT NULL
BEGIN
    DECLARE @Msg NVARCHAR(2000) = N'Not ready. Run docs\dev-sample-providers-data.sql first. Missing provider / service: ' + @Missing + N'.';
    THROW 50002, @Msg, 1;
END

-- ---------- No OTHER active booking may already hold one of the sample provider/date/time slots ----------
-- (the database allows only one Pending/Accepted/In Progress booking per provider, date and start time)
IF EXISTS (
    SELECT 1
    FROM @Plan pl
    JOIN USERS pu ON pu.EMAIL = pl.ProviderEmail
    JOIN SERVICE_PROVIDERS p ON p.USER_ID = pu.USER_ID
    JOIN BOOKINGS b ON b.PROVIDER_ID = p.PROVIDER_ID
                   AND b.BOOKING_DATE = DATEADD(DAY, pl.DayOffset, @Today)
                   AND b.BOOKING_TIME = pl.BTime
    WHERE pl.Status IN (N'Pending', N'Accepted', N'In Progress')
      AND b.STATUS IN (N'Pending', N'Accepted', N'In Progress')
      AND ISNULL(b.SERVICE_DESCRIPTION, N'') NOT LIKE N'[[]DEV SAMPLE (customer bookings)%')
BEGIN
    THROW 50003, N'Another active booking of one of the sample providers already uses one of the sample date/time slots, so the sample bookings were not added.', 1;
END

BEGIN TRAN;

IF @Refresh = 1
BEGIN
    DELETE r FROM REVIEWS r JOIN BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
    WHERE b.CUSTOMER_ID = @CustomerId AND b.SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (customer bookings)%';
    DELETE bs FROM BOOKING_SERVICES bs JOIN BOOKINGS b ON b.BOOKING_ID = bs.BOOKING_ID
    WHERE b.CUSTOMER_ID = @CustomerId AND b.SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (customer bookings)%';
    DELETE FROM BOOKINGS
    WHERE CUSTOMER_ID = @CustomerId AND SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (customer bookings)%';
END

IF EXISTS (SELECT 1 FROM BOOKINGS WHERE CUSTOMER_ID = @CustomerId AND SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (customer bookings)%')
BEGIN
    PRINT 'Sample bookings already exist for this customer - nothing more added. Set @Refresh = 1 to rebuild them.';
END
ELSE
BEGIN
    -- one row per booking, in plan order, so the booking numbers grow in the same order
    DECLARE @Slot INT = 1;
    WHILE @Slot <= 7
    BEGIN
        INSERT INTO BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES,
                              SERVICE_LOCATION, SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT)
        SELECT @CustomerId, p.PROVIDER_ID, DATEADD(DAY, pl.DayOffset, @Today), pl.BTime, s.DURATION_MINUTES,
               pl.Location,
               N'[DEV SAMPLE (customer bookings) #' + CAST(pl.Slot AS NVARCHAR(5)) + N'] ' + pl.Notes,
               s.PRICE, pl.Status, pl.Payment,
               DATEADD(MINUTE, pl.Slot, DATEADD(DAY, -pl.CreatedDaysAgo, SYSUTCDATETIME()))
        FROM @Plan pl
        JOIN USERS pu ON pu.EMAIL = pl.ProviderEmail
        JOIN SERVICE_PROVIDERS p ON p.USER_ID = pu.USER_ID
        JOIN SERVICES s ON s.PROVIDER_ID = p.PROVIDER_ID AND s.SERVICE_NAME = pl.ServiceName
        WHERE pl.Slot = @Slot;
        SET @Slot += 1;
    END

    INSERT INTO BOOKING_SERVICES (BOOKING_ID, SERVICE_ID, PRICE_SNAPSHOT, DURATION_MINUTES_SNAPSHOT)
    SELECT b.BOOKING_ID, s.SERVICE_ID, s.PRICE, s.DURATION_MINUTES
    FROM @Plan pl
    JOIN USERS pu ON pu.EMAIL = pl.ProviderEmail
    JOIN SERVICE_PROVIDERS p ON p.USER_ID = pu.USER_ID
    JOIN BOOKINGS b ON b.CUSTOMER_ID = @CustomerId
                   AND b.PROVIDER_ID = p.PROVIDER_ID
                   AND b.SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (customer bookings) #' + CAST(pl.Slot AS NVARCHAR(5)) + N']%'
    JOIN SERVICES s ON s.PROVIDER_ID = p.PROVIDER_ID AND s.SERVICE_NAME = pl.ServiceName;

    -- the one Completed booking that is already reviewed
    INSERT INTO REVIEWS (BOOKING_ID, RATING, [COMMENT], STATUS, CREATED_AT)
    SELECT b.BOOKING_ID, pl.Rating, N'Great service, thank you!', N'Visible', SYSUTCDATETIME()
    FROM @Plan pl
    JOIN BOOKINGS b ON b.CUSTOMER_ID = @CustomerId
                   AND b.SERVICE_DESCRIPTION LIKE N'[[]DEV SAMPLE (customer bookings) #' + CAST(pl.Slot AS NVARCHAR(5)) + N']%'
    WHERE pl.Rating IS NOT NULL;
END

COMMIT;

-- What the My Bookings tabs will show for this customer
SELECT STATUS, COUNT(*) AS Bookings FROM BOOKINGS WHERE CUSTOMER_ID = @CustomerId GROUP BY STATUS ORDER BY STATUS;
SELECT COUNT(*) AS TotalBookings FROM BOOKINGS WHERE CUSTOMER_ID = @CustomerId;
