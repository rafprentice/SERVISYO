# Database v2: functions, views, stored procedures and triggers

The full design is in `docs/sql/DatabaseDesign-v2.sql`. It is the improved version of the group's `DatabaseDesign.sql`, written for the app's real tables.

## What was added
- **8 functions**:
  - `fn_DayOfWeek`: 0 = Sunday, whatever `SET DATEFIRST` says.
  - `fn_ManilaNow`: the current Cebu time.
  - `fn_GetFullName`.
  - `fn_GetProviderAvgRating`: visible reviews only; NULL when there are none.
  - `fn_CountProviderJobsOnDate`.
  - `fn_IsProviderAvailable`: checks the whole job (start + duration) against hours, breaks and days off.
  - `fn_HasBookingConflict`.
  - `fn_GetAvailableSlots`: a table function listing the free start times.
- **2 views**:
  - `vw_BookingOverview`: one row per booking, with names, services and payment.
  - `vw_ProviderDirectory`.
- **19 stored procedures**:
  - Accounts: `usp_RegisterCustomer` and `usp_RegisterProvider` (**the website now uses these to sign people up**), `usp_GetUserForLogin`.
  - Admin: `usp_ApproveProvider`, `usp_UpdateAccountStatus`, `usp_ModerateReview`.
  - Provider: `usp_AddService`, `usp_AddAvailability`, `usp_UpdateAvailability`, `usp_DeleteAvailability`.
  - Read-only: `usp_SearchServices`, `usp_GetProviderAvailability`.
  - Bookings: `usp_CreateBooking`, `usp_CancelBooking`, `usp_UpdateBookingStatus`.
  - Payments and reviews: `usp_ApproveAndPay`, `usp_ConfirmCashReceived`, `usp_AddReview`.
  - Reports: `usp_GetMonthlyReport`.
- **3 new triggers**. The three audit triggers are unchanged.

  | Trigger | Table | What it does |
  |---|---|---|
  | `trg_Reviews_RatingSync` | REVIEWS | Keeps `AVG_RATING` and `REVIEW_COUNT` right after any review change |
  | `trg_Bookings_StatusFlow` | BOOKINGS | Refuses status changes outside Pending → Accepted/Rejected/Cancelled, Accepted → In Progress (only once paid or cash chosen), In Progress → Completed |
  | `trg_Payments_Audit` | PAYMENTS | Writes payment events to System Logs |

- **Indexes and checks** (in the EF model):
  - `IX_BOOKINGS_PROVIDER_STATUS`, `IX_BOOKINGS_DATE_PAYMENT`, `IX_NOTIFICATIONS_USER_UNREAD`, `IX_MESSAGES_RECEIVER_UNREAD`;
  - `CK_BOOKINGS_FEE` (fee ≥ 0) and `CK_PROVIDERS_COUNTS` (review count and base fee ≥ 0).

## How it gets into the database
The EF migration **DatabaseV2** runs the same SQL as the `.sql` file. `Data/DatabaseV2Sql.cs` is generated from the `.sql` file, so the two can't differ. So:
- `dotnet ef database update` installs it on LocalDB.
- The idempotent `deploy-latest.sql` installs it on MonsterASP.
- The other laptop gets it with `git pull` and then `dotnet ef database update`.

Safety:
- Every object contains the marker `SERVISYO-DB-V2`.
- The migration first runs `CheckNoClash`. If an object with one of the new names already exists and was **not** made by v2 (for example a procedure someone created by hand from the PDF), it stops with a message and changes nothing.
- Each migration runs in one transaction, so a failure leaves the database as it was.
- `Down` removes exactly these objects.

## What the website uses
Registration (customer and provider) calls `usp_RegisterCustomer` / `usp_RegisterProvider`:
- The **password is hashed in C# first**; the procedure only receives the hash.
- If the procedures are not installed yet (error 2812), the app falls back to the same EF code as before, so publishing the code before updating the database is safe.
- The in-memory unit tests use the EF path.

Everything else in the website keeps its tested C# code, and the database procedures follow the same rules.
- **Triggers:** the triggers apply to every path. Because they exist, EF is told about them (`HasTrigger`) for REVIEWS, BOOKINGS and PAYMENTS. The code with those `HasTrigger` lines must be deployed together with (or before) the database update.

## Testing in SSMS
Open `docs/sql/DatabaseDesign-v2.sql`, connect to `(localdb)\MSSQLLocalDB`, choose the SERVISYO database, then press **Execute**.
- PART 6 runs 28 tests: registration, double email, booking rules (overlap, break, hours, free slots), start-before-payment, cash on completion, e-wallet, double payment, the rating trigger, cancel rules, ownership, the status trigger, search and a suspended account.
- Tests 22-28 (v2.1): +63 phone saved as 09..., bad phone / email domain / numbers-only address refused, the status copies follow USERS, and suspending a provider cancels their Pending bookings but keeps Accepted ones.
- **Every test is rolled back**, so no data is kept. The IDENTITY numbers do move on a little.
- The last grid shows PASS/FAIL per test, and the Messages tab gives the total.

## Developer override
`docs/dev-sample-booking.sql` jumps a sample booking to any stage. It now runs this first, which only affects that connection:

`EXEC sp_set_session_context @key = N'servisyo_dev_override', @value = 1;`

This lets the status trigger allow the jump.

## Sample ratings
`docs/dev-sample-providers-data.sql` writes made-up ratings without real reviews.
- The rating trigger does not change existing values.
- The first real review change for such a provider recalculates their rating from their actual reviews. That is correct behaviour, but the made-up number will disappear for that provider.

## Database v2.1: profile clean-up and status copies (migration ProfileCleanup)
**What changes**
- **No more CITY.** SERVISYO is Cebu City only, so `CUSTOMERS.CITY` and `SERVICE_PROVIDERS.CITY` are removed, together with every City box, column and search.
  - The sign-up pages show a fixed "Cebu City" (not saved).
  - Before a provider accepts a job, its location shows as "Cebu City" instead of the customer's city.
- **Column sizes:**
  - `EMAIL` `NVARCHAR(256)`;
  - `FIRST_NAME` / `MIDDLE_NAME` / `LAST_NAME` `NVARCHAR(100)`;
  - `PHONE_NUMBER` `NVARCHAR(11)`, always a Philippine mobile number `09XXXXXXXXX`.
- **Stored-procedure parameters stay larger on purpose** (`NVARCHAR(400)`). SQL Server silently cuts a too-long value down to the parameter size, so a bigger parameter lets the procedure see the real length and refuse it with a clear message.
- **Status copies.** `CUSTOMERS.ACCOUNT_STATUS` and `SERVICE_PROVIDERS.ACCOUNT_STATUS` are copies of `USERS.ACCOUNT_STATUS`, which stays the real status. Three triggers keep them equal:
  - `trg_Users_StatusSync` copies a changed status to the customer / provider row;
  - `trg_Customers_StatusCopy` and `trg_Providers_StatusCopy` set the copy back if anything writes a different value.
  - The app also sets both in the same save.
  - `SERVICE_PROVIDERS.APPROVAL_STATUS` (Pending / Approved / Rejected) is a different thing and is not touched.
- **Suspending or deactivating a provider** (admin Providers page, and `usp_UpdateAccountStatus`):
  - their **Pending** bookings are cancelled and each customer gets a notification;
  - **Accepted / In Progress** bookings are not changed, because they may already be paid. The admin sees them listed in the message (the procedure returns them as a second result).
- **Foreign keys** stay `ON DELETE NO ACTION` (Restrict): deleting a user that still has a customer / provider / booking / admin-action row is refused. Accounts are suspended or deactivated, never deleted. The only cascade is a weekly schedule taking its breaks with it.

**Input rules (browser and server)**
| Field | Rule |
|---|---|
| Names | letters (including ñ and accents), spaces, `.`, `'`, `-`; no numbers or symbols; 100 characters |
| Email | `[username]@[domain].[tld]`; domain and TLD letters only (no numbers), each part at least 2 letters (`juan@a.c` and `juan@gmail2.com` are refused); 256 characters |
| Phone | `09XXXXXXXXX` (11 digits) or `+639XXXXXXXXX`; digits and one leading `+` only; saved as `09XXXXXXXXX` |
| Street address / barangay | free text but not numbers only |

The sign-in and forgot-password pages keep the basic email check, so an account made earlier with an address the new sign-up rule would refuse can still sign in.

**Before updating a database**
1. Run `docs/sql/check-before-profile-cleanup.sql`. It only reads.
   - It lists any phone number that cannot become `09XXXXXXXXX` and any name longer than 100 characters.
   - It also shows the CITY values that will be removed.
2. The migration first turns phone numbers like `0917-123-4567` / `+63 917 123 4567` / `9171234567` into `09171234567`.
3. It then **stops without changing anything** if a row still does not fit. Fix those rows first.

## Database v2.2: admin-action targets with foreign keys (migration AdminTargets)

**What changed in `ADMIN_ACTIONS`**
- `TARGET_ID` is gone. It held four different kinds of id, so it could not be a foreign key.
- Each kind of target now has its own column, and each one is a **FOREIGN KEY** (`ON DELETE NO ACTION`):

| TARGET_TYPE | Column | Points to | Written by |
|---|---|---|---|
| `CUSTOMER` | `TARGET_CUSTOMER_ID` | `CUSTOMERS.CUSTOMER_ID` | suspending / deactivating / re-activating a customer |
| `SERVICE_PROVIDER` | `TARGET_PROVIDER_ID` | `SERVICE_PROVIDERS.PROVIDER_ID` | approving / rejecting an application; suspending / deactivating / re-activating a provider |
| `REVIEW` | `TARGET_REVIEW_ID` | `REVIEWS.REVIEW_ID` | review moderation |
| `CATEGORY` | `TARGET_CATEGORY_ID` | `SERVICE_CATEGORIES.CATEGORY_ID` | category created / updated / enabled / disabled |

- `TARGET_TYPE` is specific: there is no `USER` any more.
- `CK_ADMIN_ACTIONS_TARGET_TYPE`: only the four types above.
- `CK_ADMIN_ACTIONS_TARGET`: exactly one target column is filled, and it is the one `TARGET_TYPE` names.
- The app never deletes customers, providers, reviews or categories, so these foreign keys never block the app.
- `usp_ApproveProvider`, `usp_UpdateAccountStatus` and `usp_ModerateReview` write the new columns.

**Existing rows are converted by the migration**
- `USER` rows become `CUSTOMER` (that user's `CUSTOMER_ID`) or `SERVICE_PROVIDER` (that user's `PROVIDER_ID`).
- `SERVICE_PROVIDER`, `REVIEW` and `CATEGORY` rows keep their id in the matching column.
- The migration saves the targets first, because EF removes `TARGET_ID` before it adds the new columns.
- It **stops without changing anything** if a row points to something that no longer exists.

**Before updating a database**
1. Run `docs/sql/check-before-admin-targets.sql` in SSMS. It only reads.
   - Grid 1 shows every row before and after.
   - Grid 2 must be **empty**. If it lists rows, send it to Claude.
2. Back up the database.
3. Run `tools/add-admin-targets.ps1`, then `dotnet ef database update --project src\Servisyo.Web`.

## Notification clean-up (keeps NOTIFICATIONS small)

- Once a day the website deletes **read** notifications older than **6 months**.
- **Unread** notifications are never deleted, however old.
- Each run that deletes something writes one SYSTEM_LOGS line: `System · DELETE · Notification · "Daily clean-up: N read notification(s) older than 6 months deleted ..."`.
- It runs about 2 minutes after the website starts, then every 24 hours. If the host restarts the website (MonsterASP recycles it), it simply runs again after the restart.
- A failure is logged and never stops the website.
- Settings in `appsettings.json`, section `NotificationCleanup`:
  - `Enabled` (true / false);
  - `KeepReadMonths` (default 6, never less than 1);
  - `FirstRunDelayMinutes`, `IntervalHours`.
- The same clean-up by hand in SSMS:
  ```sql
  DELETE FROM dbo.NOTIFICATIONS WHERE IS_READ = 1 AND CREATED_AT < DATEADD(MONTH, -6, SYSUTCDATETIME());
  ```
