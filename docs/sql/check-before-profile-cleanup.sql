/* SERVISYO - run this BEFORE updating a database to v2.1 (EF migration ProfileCleanup).
   It only READS and changes nothing. Run it on LocalDB and on MonsterASP (db73715).
   The update stops (changing nothing) if any row below is listed, so fix those rows first.
   Phone numbers like 0917-123-4567, +63 917 123 4567 or 9171234567 are fine: the update turns them into 09171234567. */

-- 1. Phone numbers that cannot become an 11-digit mobile number (09XXXXXXXXX)
SELECT USER_ID, FIRST_NAME, LAST_NAME, PHONE_NUMBER AS PHONE_NOW, x.P AS PHONE_AFTER_CLEANUP
FROM dbo.USERS u
CROSS APPLY (SELECT NULLIF(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(u.PHONE_NUMBER)), N' ', N''), N'-', N''), N'.', N''), N'(', N''), N')', N''), N'') AS S) a
CROSS APPLY (SELECT CASE WHEN LEFT(a.S, 3) = N'+63' THEN N'0' + SUBSTRING(a.S, 4, 50)
                         WHEN LEFT(a.S, 2) = N'63' AND LEN(a.S) = 12 THEN N'0' + SUBSTRING(a.S, 3, 50)
                         WHEN LEFT(a.S, 1) = N'9' AND LEN(a.S) = 10 THEN N'0' + a.S
                         ELSE a.S END AS P) x
WHERE x.P IS NOT NULL AND (LEN(x.P) <> 11 OR x.P NOT LIKE N'09[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]');
-- To fix one:   UPDATE dbo.USERS SET PHONE_NUMBER = N'09171234567' WHERE USER_ID = 123;   (or = NULL if unknown)

-- 2. Names longer than 100 characters
SELECT USER_ID, FIRST_NAME, MIDDLE_NAME, LAST_NAME
FROM dbo.USERS
WHERE LEN(FIRST_NAME) > 100 OR LEN(LAST_NAME) > 100 OR LEN(ISNULL(MIDDLE_NAME, N'')) > 100;

-- 3. For your information: the CITY values that the update removes (SERVISYO is Cebu City only)
SELECT N'CUSTOMERS' AS FROM_TABLE, CUSTOMER_ID AS ID, CITY FROM dbo.CUSTOMERS WHERE CITY IS NOT NULL
UNION ALL
SELECT N'SERVICE_PROVIDERS', PROVIDER_ID, CITY FROM dbo.SERVICE_PROVIDERS WHERE CITY IS NOT NULL;
