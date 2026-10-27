# System Logs (database triggers) + Admin monthly PDF report

## What is logged, and where
| Trigger | Table | Logs |
|---|---|---|
| `trg_Bookings_Audit` | BOOKINGS (after INSERT, UPDATE) | new booking requests; every STATUS change; every PAYMENT_STATUS change (EntityName "Payment") |
| `trg_Users_Audit` | USERS (after INSERT, UPDATE, DELETE) | new accounts; role changes; Active/Suspended/Deactivated; profile edits (name / email / phone); password changes; deleted accounts |
| `trg_Services_Audit` | SERVICES (after INSERT, UPDATE, DELETE) | new services; price changes; activated / deactivated; renamed / details edited; deleted |
| app (AccountController) | — | LOGIN on every successful sign-in |

- There is no separate payments table in the SERVISYO schema: payment is BOOKINGS.PAYMENT_STATUS, so payments are logged by
  `trg_Bookings_Audit` (no `trg_Payments_Audit`).
- WHO: `AuditSessionInterceptor` runs `sp_set_session_context` on every connection with the signed-in user's id and role.
  The triggers read it. Not signed in (e.g. registering) = "Guest"; app start-up / SSMS = "System".
- Passwords are never written to the log, only "Password changed for …".
- EF Core is told the tables have triggers (`t.HasTrigger(...)`); without that SQL Server refuses EF's INSERT/UPDATE statements.
- Readable copy of the trigger SQL: docs/sql/audit-triggers-readable.sql (the migration uses Data/AuditTriggersSql.cs).

## Admin pages
- **System Logs** (sidebar): `/admin/logs` — search, From/To (Cebu dates), Action (INSERT/UPDATE/DELETE/LOGIN), Role, 25 per page.
- **Export PDF Report** (dashboard, top right): opens `/admin/reports/summary?print=true` in a new tab and shows the print window.
  Choose **Save as PDF**. The month can be changed at the top of the report.
  Contents: revenue, platform fees, provider earnings, paid bookings; bookings by status, completed vs cancelled, completion rate;
  top 5 categories; active customers/providers, waiting applications, new sign-ups; revenue for the last 6 months.
- Platform fee: SERVISYO stores no commission, so the report ESTIMATES it as a percentage of revenue:
  `"Reports": { "PlatformFeePercent": 10 }` in appsettings (default 10).

## Setup
1. `powershell -ExecutionPolicy Bypass -File tools\add-system-logs.ps1`
   (DbContext: SystemLogs table + HasTrigger; Program.cs: services + interceptor; builds; creates migration AddSystemLogs with the triggers)
2. Check the migration, then `dotnet ef database update --project src\Servisyo.Web`
3. Monster: `dotnet ef migrations script --idempotent -o deploy-latest.sql --project src\Servisyo.Web`, import it, then publish.

## Checking the triggers by hand (SSMS on SERVISYO)
```sql
SELECT name FROM sys.triggers;                                   -- the three trg_..._Audit
SELECT TOP 20 * FROM SYSTEM_LOGS ORDER BY SYSTEM_LOG_ID DESC;     -- newest lines
```
