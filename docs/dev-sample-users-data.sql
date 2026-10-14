/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: sample CUSTOMERS so the admin Users page has
   something to show and you can try Suspend / Deactivate / Reactivate / Restore.

   Adds (only the ones that do not exist yet - it never changes existing rows):
     Ana Reyes (Mandaue City), Brent Lim (Naga City), Carla Tan (Talisay City, Suspended),
     Dennis Go (Lapu-Lapu City), Elena Cruz (Danao City), Rey Aquino (Toledo City, Deactivated)
   Emails end in @servisyo.test and the accounts CANNOT sign in (the password
   hash is not a real hash).

   If you already ran dev-sample-admin-data.sql / dev-sample-provider-data.sql,
   some of these customers exist already and are kept exactly as they are.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-users-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

DECLARE @Dev TABLE (Email NVARCHAR(155), FirstName NVARCHAR(155), LastName NVARCHAR(155),
                    AccountStatus NVARCHAR(50), City NVARCHAR(50));
INSERT INTO @Dev VALUES
    (N'dev.ana@servisyo.test',    N'Ana',    N'Reyes',   N'Active',      N'Mandaue City'),
    (N'dev.brent@servisyo.test',  N'Brent',  N'Lim',     N'Active',      N'Naga City'),
    (N'dev.carla@servisyo.test',  N'Carla',  N'Tan',     N'Suspended',   N'Talisay City'),
    (N'dev.dennis@servisyo.test', N'Dennis', N'Go',      N'Active',      N'Lapu-Lapu City'),
    (N'dev.elena@servisyo.test',  N'Elena',  N'Cruz',    N'Active',      N'Danao City'),
    (N'dev.rey@servisyo.test',    N'Rey',    N'Aquino',  N'Deactivated', N'Toledo City');

INSERT INTO USERS (FIRST_NAME, LAST_NAME, EMAIL, PASSWORD_HASH, [ROLE], ACCOUNT_STATUS)
SELECT d.FirstName, d.LastName, d.Email, N'AAAA', N'Customer', d.AccountStatus
FROM @Dev d
WHERE NOT EXISTS (SELECT 1 FROM USERS u WHERE u.EMAIL = d.Email);

INSERT INTO CUSTOMERS (USER_ID, CITY)
SELECT u.USER_ID, d.City
FROM @Dev d JOIN USERS u ON u.EMAIL = d.Email
WHERE NOT EXISTS (SELECT 1 FROM CUSTOMERS c WHERE c.USER_ID = u.USER_ID);

COMMIT;

SELECT u.FIRST_NAME + N' ' + u.LAST_NAME AS Name, u.EMAIL, c.CITY, u.ACCOUNT_STATUS AS Status
FROM USERS u LEFT JOIN CUSTOMERS c ON c.USER_ID = u.USER_ID
WHERE u.[ROLE] = N'Customer'
ORDER BY u.FIRST_NAME, u.LAST_NAME;
