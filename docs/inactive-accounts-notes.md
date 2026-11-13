# Inactive accounts: warned at 335 days, removed after a year (database v2.9)

Accounts nobody uses are removed so the database does not keep personal data nobody needs.

## The one rule: the last sign-in
| What | Where |
|---|---|
| `USERS.LAST_ACTIVITY_AT`: set at registration and at **every successful sign-in** (a wrong password does not count) | `AuthService.ValidateLoginAsync`; the column default `SYSUTCDATETIME()` covers `usp_RegisterProvider` |
| `USERS.INACTIVITY_WARNED_AT`: when the warning email went out; a sign-in sets it back to null | `InactiveAccountService`, `AuthService` |
| Existing accounts start from their last LOGIN line in System Logs, or their registration | `Data/InactiveAccountsSql.cs` (migration step) |

## The daily job (`Services/InactiveAccountCleanupService.cs`)
Runs 2 minutes after the site starts, then every 24 hours (`PeriodicTimer`). Each run is safe to repeat.

| Day without a sign-in | What happens |
|---|---|
| 335 | Email: *"Your SERVISYO account will be deleted on {date}"*, saying that signing in keeps it. If the email cannot be sent, nothing is saved and it tries again the next day. |
| 365, and at least 30 days after the warning | The account is removed (one transaction per account, checked again inside it). |

Never touched:
- **administrators**;
- anyone with an **unfinished booking**, as customer or provider. Unfinished means Pending, Accepted, In Progress, or
  Completed with the cash not collected yet. These are SERVISYO's names for "Requested / Quotation / Scheduled /
  In progress / Paid".

### Removing an account
- **No bookings at all:** the account and everything it owns is deleted:
  - the USERS row and the CUSTOMERS / SERVICE_PROVIDERS row;
  - for a provider: schedule and breaks, days off and day-off requests, name change requests, categories and services;
  - messages;
  - the admin history about it;
  - the profile photo file.
- **Finished bookings with someone else:** the bookings stay, with their payments, reviews and messages, so the other
  person's history, the provider's rating and the reports stay correct. The person is erased:
  - name → "Deleted User", email → `deleted-user-{id}@deleted.servisyo.invalid`, no phone, address or photo;
  - empty password hash (can never sign in), Deactivated;
  - each booking's address → "Removed (account deleted)";
  - a provider becomes "Deleted provider". Its schedule, days off, categories and unused services are deleted, and the
    services its bookings need are turned off.

  An erased account is never processed again.
- Every removal writes SYSTEM_LOGS:
  - USER_ID null, USER_ROLE System, ACTION_TYPE DELETE, ENTITY_NAME User, ENTITY_ID the user's id;
  - DESCRIPTION in the v2.10 standard format: *"System deleted User #{id}: Automatically purged inactive account
    (> 1 year): {email}."*, with *"(finished bookings kept under "Deleted User")"* before the full stop for an erased
    account.

  Database v2.10: the audit triggers are switched off for that one save (`SESSION_CONTEXT servisyo_audit_off`), so the
  erased values are not copied into System Logs as old -> new lines. This line is the only one.
- No foreign key cascades: the service deletes in the right order inside the transaction. The project's safe default
  is "never cascade", and SQL Server refuses two cascade paths, e.g. BOOKINGS → CUSTOMERS / SERVICE_PROVIDERS → USERS.
  `SYSTEM_LOGS.USER_ID` has no foreign key on purpose, so the log lines stay.

## Report only until you switch it on
`appsettings.json`: `"InactiveAccounts": { "DeleteEnabled": false }`.
- **false (default):** nothing changes and no email is sent. The admin dashboard's **Needs Admin Attention** shows a
  row such as *"3 inactive accounts with no sign-in for 335+ days (1 over a year); automatic deletion is off"*, with a
  View Users button. It is worked out when the dashboard opens and nothing is stored. Database v2.10 replaced the v2.9
  REPORT lines in System Logs with this row; the migration removes the old REPORT lines.
- **true:** warnings go out; removals start 30 days after each warning. Online: put it in `appsettings.Production.json`
  (see `deploy-monster-notes.md`).

## Database v2.9 ("InactiveAccounts")
Run `tools\add-inactive-accounts.ps1` once, after v2.8. It adds the two columns and `IX_USERS_LAST_ACTIVITY_AT`, and
fills LAST_ACTIVITY_AT from the sign-in log. It deletes nothing.

## Tests
`tests/Servisyo.Tests/InactiveAccountTests.cs`: sign-in, report only, the warning, the 365 + 30 days, administrators,
unfinished bookings, delete vs erase, the audit line, the model, the worker settings, the design file and the tools.
