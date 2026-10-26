/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: the four sample services from the My Services design
   for one provider:
     Pipe Repair           P800    Active
     Fixture Install       P600    Active
     Drain Unclogging      P500    Active
     Water Heater Install  P1,500  Inactive

   WHICH PROVIDER? Set @ProviderEmail below to the e-mail of a provider you can SIGN IN as
   (an approved one - approve a new provider in Admin > Providers first). The sample providers
   from dev-sample-providers-data.sql (dev.marco@...) cannot sign in.

   - The provider must already exist and be Approved, otherwise this script stops with a message.
   - Services the provider already has (same name) are left alone; only missing ones are added.
   - Adds the "Plumbing" category if it does not exist, and links the provider to it.
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-services-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ProviderEmail NVARCHAR(155) = N'dev.marco@servisyo.test';   -- <-- change this to your own provider's e-mail

DECLARE @ProviderId INT = (SELECT p.PROVIDER_ID
                           FROM SERVICE_PROVIDERS p JOIN USERS u ON u.USER_ID = p.USER_ID
                           WHERE u.EMAIL = @ProviderEmail AND p.APPROVAL_STATUS = N'Approved');

IF @ProviderId IS NULL
BEGIN
    DECLARE @Msg NVARCHAR(500) = N'No APPROVED provider has the e-mail ' + @ProviderEmail
        + N'. Change @ProviderEmail at the top of this script (and approve the provider first if needed).';
    THROW 50001, @Msg, 1;
END

BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM SERVICE_CATEGORIES WHERE CATEGORY_NAME = N'Plumbing')
    INSERT INTO SERVICE_CATEGORIES (CATEGORY_NAME, DESCRIPTION, IS_ACTIVE)
    VALUES (N'Plumbing', N'Leaks, pipes, fixtures and installation.', 1);

DECLARE @CategoryId INT = (SELECT CATEGORY_ID FROM SERVICE_CATEGORIES WHERE CATEGORY_NAME = N'Plumbing');

IF NOT EXISTS (SELECT 1 FROM PROVIDER_CATEGORIES WHERE PROVIDER_ID = @ProviderId AND CATEGORY_ID = @CategoryId)
    INSERT INTO PROVIDER_CATEGORIES (PROVIDER_ID, CATEGORY_ID) VALUES (@ProviderId, @CategoryId);

DECLARE @Sample TABLE (Name NVARCHAR(100), Descr NVARCHAR(255), Price DECIMAL(10,2), Minutes INT, IsActive BIT, Sort INT);
INSERT INTO @Sample VALUES
    (N'Pipe Repair',          N'Fix leaking or burst pipes, both residential and commercial.', 800,  120, 1, 1),
    (N'Fixture Install',      N'Install faucets, shower heads, and other bathroom fixtures.',  600,  90,  1, 2),
    (N'Drain Unclogging',     N'Clear blocked kitchen and bathroom drains.',                   500,  60,  1, 3),
    (N'Water Heater Install', N'Install and configure water heaters (electric or gas).',       1500, 180, 0, 4);

-- inserted in design order, so the list on the page keeps that order
INSERT INTO SERVICES (PROVIDER_ID, CATEGORY_ID, SERVICE_NAME, DESCRIPTION, PRICE, DURATION_MINUTES, IS_ACTIVE)
SELECT @ProviderId, @CategoryId, s.Name, s.Descr, s.Price, s.Minutes, s.IsActive
FROM @Sample s
WHERE NOT EXISTS (SELECT 1 FROM SERVICES x WHERE x.PROVIDER_ID = @ProviderId AND x.SERVICE_NAME = s.Name)
ORDER BY s.Sort;

COMMIT;

SELECT s.SERVICE_NAME AS Service, s.PRICE AS Price, s.DURATION_MINUTES AS Minutes, s.IS_ACTIVE AS Active
FROM SERVICES s
WHERE s.PROVIDER_ID = @ProviderId
ORDER BY s.IS_ACTIVE DESC, s.SERVICE_ID;
