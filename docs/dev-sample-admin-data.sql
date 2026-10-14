/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: sample data so you can SEE the Admin Dashboard
   before the real admin screens (approvals, moderation, reports) exist.

   It adds (all clearly marked / using @servisyo.test emails):
     - 3 customers  (Ana Reyes, Brent Lim [Suspended], Dennis Go)
     - 3 approved providers (Marco Reyes, Liza Santos, Danilo Cruz) with 1-2 services each
     - 2 PENDING provider applications (Felix Torres - Plumbing - Mandaue,
                                        Maricel Bautista - Cleaning - Poblacion)
     - 4 bookings (Accepted, Pending, In Progress, Completed)
     - 1 FLAGGED review (on the Completed booking)
     - 1 OPEN booking report (on the In Progress booking)
   None of these accounts can sign in (their password hash is not a real hash).

   - Safe to re-run: if the sample bookings exist, nothing more is added.
   - @Refresh = 1 deletes ONLY the bookings marked "DEV SAMPLE (admin)" (plus their
     reviews, reports and booking lines) and creates them again with fresh dates.
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-admin-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Refresh BIT = 0;      -- 1 = rebuild the "DEV SAMPLE (admin)" bookings
DECLARE @Today DATE = CAST(DATEADD(HOUR, 8, SYSUTCDATETIME()) AS DATE);   -- Manila/Cebu date

BEGIN TRAN;

-- ---------- Categories (the homepage seeder normally creates these) ----------
INSERT INTO SERVICE_CATEGORIES (CATEGORY_NAME, DESCRIPTION, IS_ACTIVE)
SELECT v.Name, v.Descr, 1
FROM (VALUES (N'Plumbing', N'Leaks, pipes, fixtures and installation.'),
             (N'Cleaning', N'Residential and commercial cleaning for homes and offices.'),
             (N'Repair',   N'Appliance, furniture and general home repairs.')) AS v(Name, Descr)
WHERE NOT EXISTS (SELECT 1 FROM SERVICE_CATEGORIES c WHERE c.CATEGORY_NAME = v.Name);

-- ---------- Users (cannot sign in: 'AAAA' is not a valid password hash) ----------
DECLARE @DevUsers TABLE (Email NVARCHAR(155), FirstName NVARCHAR(155), LastName NVARCHAR(155),
                         UserRole NVARCHAR(50), AccountStatus NVARCHAR(50), City NVARCHAR(50));
INSERT INTO @DevUsers VALUES
    (N'dev.ana@servisyo.test',     N'Ana',     N'Reyes',    N'Customer',         N'Active',    N'Cebu City'),
    (N'dev.brent@servisyo.test',   N'Brent',   N'Lim',      N'Customer',         N'Suspended', N'Mandaue City'),
    (N'dev.dennis@servisyo.test',  N'Dennis',  N'Go',       N'Customer',         N'Active',    N'Lapu-Lapu City'),
    (N'dev.marco@servisyo.test',   N'Marco',   N'Reyes',    N'Service Provider', N'Active',    N'Cebu City'),
    (N'dev.liza@servisyo.test',    N'Liza',    N'Santos',   N'Service Provider', N'Active',    N'Cebu City'),
    (N'dev.danilo@servisyo.test',  N'Danilo',  N'Cruz',     N'Service Provider', N'Active',    N'Mandaue'),
    (N'dev.felix@servisyo.test',   N'Felix',   N'Torres',   N'Service Provider', N'Active',    N'Mandaue'),
    (N'dev.maricel@servisyo.test', N'Maricel', N'Bautista', N'Service Provider', N'Active',    N'Poblacion');

INSERT INTO USERS (FIRST_NAME, LAST_NAME, EMAIL, PASSWORD_HASH, [ROLE], ACCOUNT_STATUS)
SELECT d.FirstName, d.LastName, d.Email, N'AAAA', d.UserRole, d.AccountStatus
FROM @DevUsers d
WHERE NOT EXISTS (SELECT 1 FROM USERS u WHERE u.EMAIL = d.Email);

INSERT INTO CUSTOMERS (USER_ID, CITY)
SELECT u.USER_ID, d.City
FROM @DevUsers d JOIN USERS u ON u.EMAIL = d.Email
WHERE d.UserRole = N'Customer'
  AND NOT EXISTS (SELECT 1 FROM CUSTOMERS c WHERE c.USER_ID = u.USER_ID);

-- ---------- Providers (3 approved, 2 pending) ----------
DECLARE @DevProviders TABLE (Email NVARCHAR(155), Business NVARCHAR(155), City NVARCHAR(50),
                             Approval NVARCHAR(50), Category NVARCHAR(100), AppliedDaysAgo INT);
INSERT INTO @DevProviders VALUES
    (N'dev.marco@servisyo.test',   N'Marco''s Plumbing (sample)',  N'Cebu City', N'Approved', N'Plumbing', 30),
    (N'dev.liza@servisyo.test',    N'Liza''s Cleaning (sample)',   N'Cebu City', N'Approved', N'Cleaning', 25),
    (N'dev.danilo@servisyo.test',  N'Danilo Aircon (sample)',      N'Mandaue',   N'Approved', N'Repair',   20),
    (N'dev.felix@servisyo.test',   N'Felix Plumbing (sample)',     N'Mandaue',   N'Pending',  N'Plumbing', 2),
    (N'dev.maricel@servisyo.test', N'Maricel Cleaning (sample)',   N'Poblacion', N'Pending',  N'Cleaning', 1);

INSERT INTO SERVICE_PROVIDERS (USER_ID, BUSINESS_NAME, CITY, REVIEW_COUNT, MIN_NOTICE_DAYS, MAX_JOBS_PER_DAY,
                               APPROVAL_STATUS, DATE_APPLIED, DATE_APPROVED)
SELECT u.USER_ID, d.Business, d.City, 0, 1, 5, d.Approval,
       DATEADD(DAY, -d.AppliedDaysAgo, SYSUTCDATETIME()),
       CASE WHEN d.Approval = N'Approved' THEN SYSUTCDATETIME() END
FROM @DevProviders d JOIN USERS u ON u.EMAIL = d.Email
WHERE NOT EXISTS (SELECT 1 FROM SERVICE_PROVIDERS p WHERE p.USER_ID = u.USER_ID);

INSERT INTO PROVIDER_CATEGORIES (PROVIDER_ID, CATEGORY_ID)
SELECT p.PROVIDER_ID, c.CATEGORY_ID
FROM @DevProviders d
JOIN USERS u ON u.EMAIL = d.Email
JOIN SERVICE_PROVIDERS p ON p.USER_ID = u.USER_ID
JOIN SERVICE_CATEGORIES c ON c.CATEGORY_NAME = d.Category
WHERE NOT EXISTS (SELECT 1 FROM PROVIDER_CATEGORIES pc WHERE pc.PROVIDER_ID = p.PROVIDER_ID AND pc.CATEGORY_ID = c.CATEGORY_ID);

-- ---------- Services of the approved providers ----------
DECLARE @DevServices TABLE (ProviderEmail NVARCHAR(155), Category NVARCHAR(100), Name NVARCHAR(100),
                            Price DECIMAL(10,2), Minutes INT);
INSERT INTO @DevServices VALUES
    (N'dev.marco@servisyo.test',  N'Plumbing', N'Pipe Repair',        850,  120),
    (N'dev.marco@servisyo.test',  N'Plumbing', N'Faucet Replacement', 500,  60),
    (N'dev.liza@servisyo.test',   N'Cleaning', N'Deep Cleaning',      1200, 180),
    (N'dev.danilo@servisyo.test', N'Repair',   N'AC Service',         900,  90);

INSERT INTO SERVICES (PROVIDER_ID, CATEGORY_ID, SERVICE_NAME, DESCRIPTION, PRICE, DURATION_MINUTES, IS_ACTIVE)
SELECT p.PROVIDER_ID, c.CATEGORY_ID, s.Name, N'Sample service.', s.Price, s.Minutes, 1
FROM @DevServices s
JOIN USERS u ON u.EMAIL = s.ProviderEmail
JOIN SERVICE_PROVIDERS p ON p.USER_ID = u.USER_ID
JOIN SERVICE_CATEGORIES c ON c.CATEGORY_NAME = s.Category
WHERE NOT EXISTS (SELECT 1 FROM SERVICES x WHERE x.PROVIDER_ID = p.PROVIDER_ID AND x.SERVICE_NAME = s.Name);

-- ---------- Bookings, one flagged review, one open report ----------
IF @Refresh = 1
BEGIN
    DELETE r FROM BOOKING_REPORTS r JOIN BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
    WHERE b.SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (admin)%';
    DELETE r FROM REVIEWS r JOIN BOOKINGS b ON b.BOOKING_ID = r.BOOKING_ID
    WHERE b.SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (admin)%';
    DELETE bs FROM BOOKING_SERVICES bs JOIN BOOKINGS b ON b.BOOKING_ID = bs.BOOKING_ID
    WHERE b.SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (admin)%';
    DELETE FROM BOOKINGS WHERE SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (admin)%';
END

IF EXISTS (SELECT 1 FROM BOOKINGS WHERE SERVICE_DESCRIPTION LIKE N'DEV SAMPLE (admin)%')
BEGIN
    PRINT 'Sample bookings already exist - nothing more added. Set @Refresh = 1 to rebuild them.';
END
ELSE
BEGIN
    DECLARE @Plan TABLE (Slot INT, CustomerEmail NVARCHAR(155), ProviderEmail NVARCHAR(155), ServiceName NVARCHAR(100),
                         DayOffset INT, BTime TIME(0), Status NVARCHAR(50), Payment NVARCHAR(50), CreatedMinutesAgo INT,
                         ReviewRating TINYINT, ReviewText NVARCHAR(255), ReportReason NVARCHAR(255));
    INSERT INTO @Plan VALUES
     (1, N'dev.ana@servisyo.test',    N'dev.marco@servisyo.test',  N'Pipe Repair',        2, '10:00', N'Accepted',    N'Unpaid', 30,   NULL, NULL, NULL),
     (2, N'dev.brent@servisyo.test',  N'dev.liza@servisyo.test',   N'Deep Cleaning',      3, '13:00', N'Pending',     N'Unpaid', 60,   NULL, NULL, NULL),
     (3, N'dev.dennis@servisyo.test', N'dev.danilo@servisyo.test', N'AC Service',         0, '09:00', N'In Progress', N'Paid',   120,  NULL, NULL,
         N'Customer says the technician left before the job was finished.'),
     (4, N'dev.dennis@servisyo.test', N'dev.marco@servisyo.test',  N'Faucet Replacement', -6, '15:00', N'Completed',  N'Paid',   9000, 1,
         N'Rude and late. Would not recommend.', NULL);

    INSERT INTO BOOKINGS (CUSTOMER_ID, PROVIDER_ID, BOOKING_DATE, BOOKING_TIME, TOTAL_DURATION_MINUTES,
                          SERVICE_LOCATION, SERVICE_DESCRIPTION, TOTAL_FEE, STATUS, PAYMENT_STATUS, CREATED_AT)
    SELECT c.CUSTOMER_ID, p.PROVIDER_ID, DATEADD(DAY, pl.DayOffset, @Today), pl.BTime, s.DURATION_MINUTES,
           N'Sample address, Cebu City', N'DEV SAMPLE (admin) #' + CAST(pl.Slot AS NVARCHAR(5)), s.PRICE,
           pl.Status, pl.Payment, DATEADD(MINUTE, -pl.CreatedMinutesAgo, SYSUTCDATETIME())
    FROM @Plan pl
    JOIN USERS cu ON cu.EMAIL = pl.CustomerEmail
    JOIN CUSTOMERS c ON c.USER_ID = cu.USER_ID
    JOIN USERS pu ON pu.EMAIL = pl.ProviderEmail
    JOIN SERVICE_PROVIDERS p ON p.USER_ID = pu.USER_ID
    JOIN SERVICES s ON s.PROVIDER_ID = p.PROVIDER_ID AND s.SERVICE_NAME = pl.ServiceName;

    INSERT INTO BOOKING_SERVICES (BOOKING_ID, SERVICE_ID, PRICE_SNAPSHOT, DURATION_MINUTES_SNAPSHOT)
    SELECT b.BOOKING_ID, s.SERVICE_ID, s.PRICE, s.DURATION_MINUTES
    FROM @Plan pl
    JOIN BOOKINGS b ON b.SERVICE_DESCRIPTION = N'DEV SAMPLE (admin) #' + CAST(pl.Slot AS NVARCHAR(5))
    JOIN SERVICES s ON s.PROVIDER_ID = b.PROVIDER_ID AND s.SERVICE_NAME = pl.ServiceName;

    INSERT INTO REVIEWS (BOOKING_ID, RATING, [COMMENT], STATUS)
    SELECT b.BOOKING_ID, pl.ReviewRating, pl.ReviewText, N'Flagged'
    FROM @Plan pl
    JOIN BOOKINGS b ON b.SERVICE_DESCRIPTION = N'DEV SAMPLE (admin) #' + CAST(pl.Slot AS NVARCHAR(5))
    WHERE pl.ReviewRating IS NOT NULL;

    INSERT INTO BOOKING_REPORTS (BOOKING_ID, REPORTED_BY_USER_ID, REASON, STATUS)
    SELECT b.BOOKING_ID, cu.USER_ID, pl.ReportReason, N'Open'
    FROM @Plan pl
    JOIN BOOKINGS b ON b.SERVICE_DESCRIPTION = N'DEV SAMPLE (admin) #' + CAST(pl.Slot AS NVARCHAR(5))
    JOIN USERS cu ON cu.EMAIL = pl.CustomerEmail
    WHERE pl.ReportReason IS NOT NULL;
END

COMMIT;

-- What the Admin Dashboard will count (your own real accounts are included too)
SELECT
    (SELECT COUNT(*) FROM USERS WHERE [ROLE] = N'Customer')                                   AS Customers,
    (SELECT COUNT(*) FROM USERS WHERE [ROLE] = N'Service Provider')                           AS Providers,
    (SELECT COUNT(*) FROM SERVICE_PROVIDERS WHERE APPROVAL_STATUS = N'Pending')               AS PendingApplications,
    (SELECT COUNT(*) FROM BOOKINGS)                                                           AS TotalBookings,
    (SELECT COUNT(*) FROM REVIEWS WHERE STATUS = N'Flagged')                                  AS FlaggedReviews,
    (SELECT COUNT(*) FROM USERS WHERE ACCOUNT_STATUS = N'Suspended' AND [ROLE] <> N'Administrator') AS SuspendedAccounts,
    (SELECT COUNT(*) FROM BOOKING_REPORTS WHERE STATUS <> N'Resolved')                        AS OpenReports;
