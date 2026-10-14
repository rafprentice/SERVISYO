/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: two DISABLED sample categories so the admin Categories
   page shows what the design shows (3 active, 2 disabled).

   Adds (only if they do not exist yet - existing categories are never changed):
     Electrical    - Disabled - Electrical wiring, panel upgrades, outlet installation, troubleshooting.
     Landscaping   - Disabled - Garden maintenance, lawn care, tree trimming, and outdoor cleaning.

   The Providers and Bookings columns come from real data. To see numbers like the design,
   run these first:  dev-sample-users-data.sql, dev-sample-providers-data.sql, dev-sample-bookings-data.sql

   Safe to re-run. Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-categories-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

INSERT INTO SERVICE_CATEGORIES (CATEGORY_NAME, DESCRIPTION, IS_ACTIVE)
SELECT v.Name, v.Descr, 0
FROM (VALUES (N'Electrical',  N'Electrical wiring, panel upgrades, outlet installation, troubleshooting.'),
             (N'Landscaping', N'Garden maintenance, lawn care, tree trimming, and outdoor cleaning.')) AS v(Name, Descr)
WHERE NOT EXISTS (SELECT 1 FROM SERVICE_CATEGORIES c WHERE c.CATEGORY_NAME = v.Name);

COMMIT;

SELECT c.CATEGORY_NAME AS Category,
       CASE WHEN c.IS_ACTIVE = 1 THEN N'Active' ELSE N'Disabled' END AS Status,
       (SELECT COUNT(*) FROM PROVIDER_CATEGORIES pc
         JOIN SERVICE_PROVIDERS p ON p.PROVIDER_ID = pc.PROVIDER_ID AND p.APPROVAL_STATUS = N'Approved'
        WHERE pc.CATEGORY_ID = c.CATEGORY_ID) AS Providers,
       (SELECT COUNT(DISTINCT bs.BOOKING_ID) FROM BOOKING_SERVICES bs
         JOIN SERVICES s ON s.SERVICE_ID = bs.SERVICE_ID
        WHERE s.CATEGORY_ID = c.CATEGORY_ID) AS Bookings
FROM SERVICE_CATEGORIES c
ORDER BY c.IS_ACTIVE DESC, c.CATEGORY_ID;
