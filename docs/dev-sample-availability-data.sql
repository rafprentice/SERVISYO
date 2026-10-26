/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: a sample weekly schedule so the provider Availability page
   shows what the design shows:
     Monday-Friday  8:00 am - 5:00 pm, Saturday and Sunday closed,
     a lunch break 12:00 - 1:00 pm on Monday,
     one unavailable date (Personal Leave, 10 days from today),
     minimum notice 1 day, at most 4 jobs per day.

   WHICH PROVIDER? Set @ProviderEmail below to the e-mail of a provider you can SIGN IN as
   (an approved one - approve a new provider in Admin > Providers first). The sample providers
   from dev-sample-providers-data.sql (dev.marco@...) cannot sign in, so they are only useful
   for looking at the data in SQL.

   - The provider must already exist and be Approved, otherwise this script stops with a message.
   - If the provider already has a schedule, nothing is changed (set @Replace = 1 to overwrite it).
   - Do not run this against a real/production database.

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo):
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-availability-data.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ProviderEmail NVARCHAR(155) = N'dev.marco@servisyo.test';   -- <-- change this to your own provider's e-mail
DECLARE @Replace BIT = 0;                                              -- 1 = overwrite an existing schedule

DECLARE @Today DATE = CAST(DATEADD(HOUR, 8, SYSUTCDATETIME()) AS DATE);   -- Manila/Cebu date
DECLARE @ProviderId INT = (SELECT p.PROVIDER_ID
                           FROM SERVICE_PROVIDERS p JOIN USERS u ON u.USER_ID = p.USER_ID
                           WHERE u.EMAIL = @ProviderEmail AND p.APPROVAL_STATUS = N'Approved');

IF @ProviderId IS NULL
BEGIN
    DECLARE @Msg NVARCHAR(500) = N'No APPROVED provider has the e-mail ' + @ProviderEmail
        + N'. Change @ProviderEmail at the top of this script (and approve the provider first if needed).';
    THROW 50001, @Msg, 1;
END

IF EXISTS (SELECT 1 FROM PROVIDER_AVAILABILITY WHERE PROVIDER_ID = @ProviderId) AND @Replace = 0
BEGIN
    PRINT 'This provider already has a schedule - nothing changed. Set @Replace = 1 to overwrite it.';
    RETURN;
END

BEGIN TRAN;

IF @Replace = 1
BEGIN
    DELETE b FROM PROVIDER_BREAKS b JOIN PROVIDER_AVAILABILITY a ON a.AVAILABILITY_ID = b.AVAILABILITY_ID WHERE a.PROVIDER_ID = @ProviderId;
    DELETE FROM PROVIDER_AVAILABILITY WHERE PROVIDER_ID = @ProviderId;
    DELETE FROM PROVIDER_UNAVAILABLE_DATES WHERE PROVIDER_ID = @ProviderId AND UNAVAILABLE_DATE >= @Today;
END

-- day numbers: 0 = Sunday ... 6 = Saturday
INSERT INTO PROVIDER_AVAILABILITY (PROVIDER_ID, DAY_OF_WEEK, IS_AVAILABLE, START_TIME, END_TIME)
SELECT @ProviderId, d.Dow, CASE WHEN d.Dow BETWEEN 1 AND 5 THEN 1 ELSE 0 END, '08:00', '17:00'
FROM (VALUES (0), (1), (2), (3), (4), (5), (6)) AS d(Dow);

INSERT INTO PROVIDER_BREAKS (AVAILABILITY_ID, BREAK_START, BREAK_END)
SELECT a.AVAILABILITY_ID, '12:00', '13:00'
FROM PROVIDER_AVAILABILITY a
WHERE a.PROVIDER_ID = @ProviderId AND a.DAY_OF_WEEK = 1;

INSERT INTO PROVIDER_UNAVAILABLE_DATES (PROVIDER_ID, UNAVAILABLE_DATE, REASON)
SELECT @ProviderId, DATEADD(DAY, 10, @Today), N'Personal Leave'
WHERE NOT EXISTS (SELECT 1 FROM PROVIDER_UNAVAILABLE_DATES WHERE PROVIDER_ID = @ProviderId AND UNAVAILABLE_DATE = DATEADD(DAY, 10, @Today));

UPDATE SERVICE_PROVIDERS SET MIN_NOTICE_DAYS = 1, MAX_JOBS_PER_DAY = 4 WHERE PROVIDER_ID = @ProviderId;

COMMIT;

SELECT a.DAY_OF_WEEK AS Day, a.IS_AVAILABLE AS Open, a.START_TIME AS Start, a.END_TIME AS [End],
       (SELECT COUNT(*) FROM PROVIDER_BREAKS b WHERE b.AVAILABILITY_ID = a.AVAILABILITY_ID) AS Breaks
FROM PROVIDER_AVAILABILITY a WHERE a.PROVIDER_ID = @ProviderId ORDER BY a.DAY_OF_WEEK;
