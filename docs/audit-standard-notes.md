# System Logs: one standard format (database v2.10, "AuditStandard")

Every line in Admin → System Logs now reads the same way, and every change of a phone number, an address, a price, a
status... shows the **old and the new value**. Before v2.10 only some changes were logged, in different wordings, and
a customer's address or a provider's business details were not logged at all.

## The four action types (nothing else)
`SYSTEM_LOGS.ACTION_TYPE` can only be `INSERT`, `UPDATE`, `DELETE` or `LOGIN`: the database refuses anything else
(CHECK `CK_SYSTEM_LOGS_ACTION_TYPE`). In C# they are `AuditActionType.Insert / Update / Delete / Login` (`Models/SystemLog.cs`).

| Action | Format | Example |
|---|---|---|
| INSERT | `{Role} {Full Name} created {Entity} #{Id}: {summary}.` | Customer Ana Reyes created Booking #12: Pending request with Mary Plumbing on 2026-11-20 at 09:00, PHP 650.00. |
| UPDATE | `{Role} {Full Name} updated {Entity} #{Id}: {field} '{old}' -> '{new}'.` | Customer Ana Reyes updated User #5: phone '09171234567' -> '09181234567'. |
| DELETE | `{Role} {Full Name} deleted {Entity} #{Id}: {summary}.` | Administrator Ada Lim deleted Service #7: Wiring (PHP 500.00). |
| LOGIN | `{Full Name} ({Email}) signed in.` | Ana Reyes (ana@gmail.com) signed in. |
| LOGIN (failed) | `Failed login attempt for {Email}.` (role Guest, no user) | Failed login attempt for ana@gmail.com. |

- **One UPDATE line per changed field.** Saving a profile that changes the phone and the address writes two lines.
- **Who:** the signed-in user's role and full name. Nobody signed in (registering) = `Guest`; the app itself, a
  scheduled job or SSMS = `System`.
- Empty values show as `(none)`; long text is cut to 150 characters in the line (the full value is still compared).
- Text is compared exactly, so `juan` → `Juan` is a change too.

## Who writes the lines
The **database triggers** write every INSERT / UPDATE / DELETE line (`Data/AuditStandardSql.cs`, the same SQL as
`docs/sql/DatabaseDesign-v2.sql` PART 5). Whatever changes a row (a page of the website, a stored procedure, SSMS) is
logged the same way, and no page can forget to log. The **app** writes only the LOGIN lines (`Services/AuditLogService.cs`).

| Trigger | Table | INSERT | UPDATE (old → new) | DELETE |
|---|---|---|---|---|
| `trg_Users_Audit` | USERS | new account | first / middle / last name, email, phone, account status, role; "password changed" | yes |
| `trg_Customers_Audit` (new) | CUSTOMERS | – (the account line) | address, profile photo | – |
| `trg_Providers_Audit` (new) | SERVICE_PROVIDERS | – (the account line) | business name, description, address, base fee, minimum notice days, max jobs per day, approval status, profile photo | – |
| `trg_Services_Audit` | SERVICES | yes | name, description, category, price, duration, active | yes |
| `trg_Bookings_Audit` | BOOKINGS | yes | status, payment status, date, time, duration, total fee, location, request description | yes |
| `trg_PaymentsDtl_Audit` | PAYMENTS_DTL | yes | status, method, reference | yes |
| `trg_Reviews_Audit` (new) | REVIEWS | yes | status, rating, comment | yes |
| `trg_UnavailableDates_Audit` (new) | PROVIDER_UNAVAILABLE_DATES | an administrator's extra day | date, reason | yes |
| `trg_DayOffRequests_Audit` (new) | PROVIDER_DAY_OFF_REQUESTS | yes | status, reason | yes |
| `trg_NameChangeRequests_Audit` (new) | PROVIDER_NAME_CHANGE_REQUESTS | yes | status, requested name, reason | yes |

Never logged:
- passwords (only "password changed");
- `LAST_ACTIVITY_AT`, `INACTIVITY_WARNED_AT`, `NOTIFICATIONS_SEEN_AT` (they change at every sign-in or visit);
- calculated or copied columns: `AVG_RATING`, `REVIEW_COUNT`, and the `ACCOUNT_STATUS` copies in CUSTOMERS /
  SERVICE_PROVIDERS (the real status is logged once, on USERS);
- the days of a day-off request one by one (the request has its own lines).

## Admin → System Logs
- **Pills:** All · INSERT · UPDATE · DELETE · LOGIN, each with its number of lines. The numbers follow the other
  filters (search, role, dates), so they always match what the pill shows.
- **20 lines a page**, newest first. The database counts and cuts the page (`PagedResult<T>`, `ViewModels/PagedResult.cs`);
  the rows are read without change tracking (`AsNoTracking`).
- Every pill and page link keeps the filters: `/admin/logs?actionType=UPDATE&search=phone&role=Customer&page=2`.
- Indexes: `IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT` (a pill, newest first) and `IX_SYSTEM_LOGS_USER_ID_CREATED_AT`
  (one person's history). They replace `IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE`.

## The inactive-account clean-up (v2.9) is the one exception
Erasing a person would otherwise copy every erased value (name, email, phone, address) into System Logs as old → new
lines. So the clean-up sets `SESSION_CONTEXT servisyo_audit_off = 1` on its own connection for that one save, and
writes ONE line itself:
*System deleted User #12: Automatically purged inactive account (> 1 year): ana@gmail.com.*

The v2.9 **REPORT** lines are gone. While automatic deletion is off, the admin dashboard's **Needs Admin Attention**
shows *"3 inactive accounts with no sign-in for 335+ days; automatic deletion is off"*, worked out when the page opens.
Nothing is stored.

## Install (laptop)
0. Database v2.9 must already be in (tools\add-inactive-accounts.ps1 ended with DONE). If it is not, install the v2.9
   zip and run that script FIRST: EF makes each migration from the code that is there, so the v2.9 step has to be
   made before the v2.10 files arrive.
1. Extract the zip in the repo root, then delete the old file it replaces:
   `Remove-Item src\Servisyo.Web\Services\SystemLogService.cs`
2. `powershell -ExecutionPolicy Bypass -File tools\add-audit-standard.ps1`
   1. It builds and runs the tests.
   2. It makes the migration `AuditStandard` and checks it.
   3. It shows the SYSTEM_LOGS action types and makes a backup.
   4. It asks for YES, then updates.
   5. At the end it runs a live test inside a transaction that is always rolled back: one phone change, then the
      same with the audit off.
3. Optional: run `docs\sql\DatabaseDesign-v2.sql` on a **copy** of the database for the full self-test (test 36 is
   this version).

## Checking by hand (SSMS on SERVISYO)
```sql
SELECT name, OBJECT_NAME(parent_id) AS on_table, is_disabled FROM sys.triggers WHERE name LIKE N'trg[_]%[_]Audit';   -- 10, all enabled
SELECT TOP (20) * FROM dbo.SYSTEM_LOGS WHERE ACTION_TYPE = N'UPDATE' ORDER BY CREATED_AT DESC;
```

## Good to know
- Older lines keep their old wording; only new lines use the standard format.
- Old → new values stay in System Logs even after an account is purged a year later. The log is an audit trail, so this
  is on purpose. If personal data must go too, ask Claude for a clean-up of the purged person's lines.
- Every failed sign-in writes one line. There is no limit on sign-in attempts yet, so someone could fill the log by
  trying many passwords (each try is slowed down by the password check). A sign-in rate limit is a possible next step.
- A sign-in can rarely write "password changed": when ASP.NET Core upgrades an old password hash to a newer format.
