/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: sample PROVIDERS so the admin Providers page
   shows what the design shows: 3 approved, 1 approved-but-suspended, 2 pending.

   Providers (only the ones that do not exist yet are added; existing rows are never changed,
   except the sample rating below):
     Marco Reyes     Approved   Plumbing  - Cebu City
     Liza Santos     Approved   Cleaning  - Mandaue
     Danilo Cruz     Approved   Repair    - Lapu-Lapu
     Rhea Villanueva Approved + account SUSPENDED   Cleaning - Talisay
     Felix Torres    Pending    Plumbing  - Mandaue
     Maricel Bautista Pending   Cleaning  - Poblacion
   Each approved provider also gets 4 sample services (so the card shows 2 tags and "+2").
   Emails end in @servisyo.test and the accounts CANNOT sign in.

   NOTE ABOUT RATINGS: in the real system a provider's average rating and review count are
   calculated from review rows. For this display-only sample they are typed in directly,
   and ONLY for sample providers that still have 0 reviews. Do not rely on these numbers.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-providers-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

-- Categories (the homepage seeder normally creates these)
INSERT INTO SERVICE_CATEGORIES (CATEGORY_NAME, DESCRIPTION, IS_ACTIVE)
SELECT v.Name, v.Descr, 1
FROM (VALUES (N'Plumbing', N'Leaks, pipes, fixtures and installation.'),
             (N'Cleaning', N'Residential and commercial cleaning for homes and offices.'),
             (N'Repair',   N'Appliance, furniture and general home repairs.')) AS v(Name, Descr)
WHERE NOT EXISTS (SELECT 1 FROM SERVICE_CATEGORIES c WHERE c.CATEGORY_NAME = v.Name);

-- Users (password hash 'AAAA' is not a real hash, so nobody can sign in with these)
DECLARE @Dev TABLE (Email NVARCHAR(155), FirstName NVARCHAR(155), LastName NVARCHAR(155), AccountStatus NVARCHAR(50),
                    Business NVARCHAR(155), City NVARCHAR(50), Approval NVARCHAR(50), Category NVARCHAR(100),
                    AppliedDaysAgo INT, SampleRating DECIMAL(2,1), SampleReviews INT);
INSERT INTO @Dev VALUES
    (N'dev.marco@servisyo.test',   N'Marco',   N'Reyes',      N'Active',    N'Marco''s Plumbing Works', N'Cebu City', N'Approved', N'Plumbing', 60, 4.9, 112),
    (N'dev.liza@servisyo.test',    N'Liza',    N'Santos',     N'Active',    N'Liza Clean Services',     N'Mandaue',   N'Approved', N'Cleaning', 50, 4.8, 87),
    (N'dev.danilo@servisyo.test',  N'Danilo',  N'Cruz',       N'Active',    N'Cruz General Repairs',    N'Lapu-Lapu', N'Approved', N'Repair',   40, 4.7, 95),
    (N'dev.rhea@servisyo.test',    N'Rhea',    N'Villanueva', N'Suspended', N'Rhea''s Home Care',       N'Talisay',   N'Approved', N'Cleaning', 30, 5.0, 43),
    (N'dev.felix@servisyo.test',   N'Felix',   N'Torres',     N'Active',    N'Torres Plumbing Co.',     N'Mandaue',   N'Pending',  N'Plumbing', 2,  NULL, 0),
    (N'dev.maricel@servisyo.test', N'Maricel', N'Bautista',   N'Active',    N'MB Cleaning Solutions',   N'Poblacion', N'Pending',  N'Cleaning', 1,  NULL, 0);

INSERT INTO USERS (FIRST_NAME, LAST_NAME, EMAIL, PASSWORD_HASH, [ROLE], ACCOUNT_STATUS)
SELECT d.FirstName, d.LastName, d.Email, N'AAAA', N'Service Provider', d.AccountStatus
FROM @Dev d
WHERE NOT EXISTS (SELECT 1 FROM USERS u WHERE u.EMAIL = d.Email);

INSERT INTO SERVICE_PROVIDERS (USER_ID, BUSINESS_NAME, CITY, REVIEW_COUNT, MIN_NOTICE_DAYS, MAX_JOBS_PER_DAY,
                               APPROVAL_STATUS, DATE_APPLIED, DATE_APPROVED)
SELECT u.USER_ID, d.Business, d.City, 0, 1, 5, d.Approval,
       DATEADD(DAY, -d.AppliedDaysAgo, SYSUTCDATETIME()),
       CASE WHEN d.Approval = N'Approved' THEN SYSUTCDATETIME() END
FROM @Dev d JOIN USERS u ON u.EMAIL = d.Email
WHERE NOT EXISTS (SELECT 1 FROM SERVICE_PROVIDERS p WHERE p.USER_ID = u.USER_ID);

INSERT INTO PROVIDER_CATEGORIES (PROVIDER_ID, CATEGORY_ID)
SELECT p.PROVIDER_ID, c.CATEGORY_ID
FROM @Dev d
JOIN USERS u ON u.EMAIL = d.Email
JOIN SERVICE_PROVIDERS p ON p.USER_ID = u.USER_ID
JOIN SERVICE_CATEGORIES c ON c.CATEGORY_NAME = d.Category
WHERE NOT EXISTS (SELECT 1 FROM PROVIDER_CATEGORIES pc WHERE pc.PROVIDER_ID = p.PROVIDER_ID AND pc.CATEGORY_ID = c.CATEGORY_ID);

-- 4 sample services for each APPROVED provider
DECLARE @Svc TABLE (Email NVARCHAR(155), Category NVARCHAR(100), Name NVARCHAR(100), Price DECIMAL(10,2), Minutes INT);
INSERT INTO @Svc VALUES
    (N'dev.marco@servisyo.test',  N'Plumbing', N'Pipe Repair',                850,  120),
    (N'dev.marco@servisyo.test',  N'Plumbing', N'Fixture Install',            700,  90),
    (N'dev.marco@servisyo.test',  N'Plumbing', N'Water Heater Install',       1500, 180),
    (N'dev.marco@servisyo.test',  N'Plumbing', N'Leak Detection',             600,  60),
    (N'dev.liza@servisyo.test',   N'Cleaning', N'Deep Cleaning',              1200, 180),
    (N'dev.liza@servisyo.test',   N'Cleaning', N'Post-Construction Cleaning', 2500, 300),
    (N'dev.liza@servisyo.test',   N'Cleaning', N'Move-out Cleaning',          1800, 240),
    (N'dev.liza@servisyo.test',   N'Cleaning', N'Window Cleaning',            800,  120),
    (N'dev.danilo@servisyo.test', N'Repair',   N'Appliance Repair',           900,  90),
    (N'dev.danilo@servisyo.test', N'Repair',   N'Carpentry',                  1100, 180),
    (N'dev.danilo@servisyo.test', N'Repair',   N'Electrical Repair',          800,  90),
    (N'dev.danilo@servisyo.test', N'Repair',   N'Aircon Cleaning',            700,  60),
    (N'dev.rhea@servisyo.test',   N'Cleaning', N'Home Cleaning',              1000, 180),
    (N'dev.rhea@servisyo.test',   N'Cleaning', N'Laundry',                    500,  120),
    (N'dev.rhea@servisyo.test',   N'Cleaning', N'Ironing',                    400,  90),
    (N'dev.rhea@servisyo.test',   N'Cleaning', N'Sofa Cleaning',              900,  90);

INSERT INTO SERVICES (PROVIDER_ID, CATEGORY_ID, SERVICE_NAME, DESCRIPTION, PRICE, DURATION_MINUTES, IS_ACTIVE)
SELECT p.PROVIDER_ID, c.CATEGORY_ID, s.Name, N'Sample service.', s.Price, s.Minutes, 1
FROM @Svc s
JOIN USERS u ON u.EMAIL = s.Email
JOIN SERVICE_PROVIDERS p ON p.USER_ID = u.USER_ID
JOIN SERVICE_CATEGORIES c ON c.CATEGORY_NAME = s.Category
WHERE NOT EXISTS (SELECT 1 FROM SERVICES x WHERE x.PROVIDER_ID = p.PROVIDER_ID AND x.SERVICE_NAME = s.Name);

-- Display-only sample ratings (only for sample providers that have no real reviews counted yet)
UPDATE p
SET p.AVG_RATING = d.SampleRating, p.REVIEW_COUNT = d.SampleReviews
FROM SERVICE_PROVIDERS p
JOIN USERS u ON u.USER_ID = p.USER_ID
JOIN @Dev d ON d.Email = u.EMAIL
WHERE d.SampleRating IS NOT NULL AND p.REVIEW_COUNT = 0 AND p.AVG_RATING IS NULL;

COMMIT;

SELECT u.FIRST_NAME + N' ' + u.LAST_NAME AS Provider, p.BUSINESS_NAME AS Business, p.CITY,
       p.APPROVAL_STATUS AS Approval, u.ACCOUNT_STATUS AS Account, p.AVG_RATING AS Rating, p.REVIEW_COUNT AS Reviews,
       (SELECT COUNT(*) FROM SERVICES s WHERE s.PROVIDER_ID = p.PROVIDER_ID AND s.IS_ACTIVE = 1) AS Services
FROM SERVICE_PROVIDERS p JOIN USERS u ON u.USER_ID = p.USER_ID
ORDER BY p.DATE_APPLIED;
