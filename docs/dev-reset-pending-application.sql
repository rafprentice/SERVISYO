/* =====================================================================
   SERVISYO - DEVELOPMENT ONLY: put a SAMPLE provider back to "Pending" so you can try the
   approval pop-up again after you have approved or rejected them.

   - Only works for sample accounts (emails ending in @servisyo.test).
   - Changes ONE provider's APPROVAL_STATUS and clears DATE_APPROVED. Nothing is deleted:
     the earlier admin-log entry and notification stay (that is correct, they really happened).

   HOW TO RUN (PowerShell, from C:\Users\ralph\Servisyo) - change @Email first if you want another provider:
     sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-reset-pending-application.sql
   ===================================================================== */
SET NOCOUNT ON;

DECLARE @Email NVARCHAR(155) = N'dev.felix@servisyo.test';

UPDATE p
SET p.APPROVAL_STATUS = N'Pending', p.DATE_APPROVED = NULL
FROM SERVICE_PROVIDERS p
JOIN USERS u ON u.USER_ID = p.USER_ID
WHERE u.EMAIL = @Email AND u.EMAIL LIKE N'%@servisyo.test';

SELECT @@ROWCOUNT AS ProvidersReset;

SELECT u.FIRST_NAME + N' ' + u.LAST_NAME AS Provider, p.APPROVAL_STATUS AS Approval, p.DATE_APPROVED AS DateApproved
FROM SERVICE_PROVIDERS p JOIN USERS u ON u.USER_ID = p.USER_ID
WHERE u.EMAIL = @Email;
