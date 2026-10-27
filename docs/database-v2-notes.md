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
- PART 6 runs 21 tests: registration, double email, booking rules (overlap, break, hours, free slots), start-before-payment, cash on completion, e-wallet, double payment, the rating trigger, cancel rules, ownership, the status trigger, search and a suspended account.
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
