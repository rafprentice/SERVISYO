# Starting a job inside its start window (database v2.12, "StartWindow")

"Mark In Progress" now follows the clock. Everything is in **Philippine Standard Time** (UTC+8): the app uses the
time zone `Asia/Manila` (or `Singapore Standard Time`, the Windows name for UTC+8 without daylight saving), and the
database adds 8 hours to UTC (`dbo.fn_ManilaNow`). `BOOKING_DATE` + `BOOKING_TIME` are Cebu date and time;
`STARTED_AT` is UTC like every other time column.

## The four states (Provider → Booking Requests, an Accepted booking the customer paid or chose cash for)
Minutes after the scheduled time are counted the way SQL Server's `DATEDIFF(MINUTE, …)` counts them, so the app and
the database always agree (9:45:59 is still minute 15; 9:46:00 is minute 16).

| When | The card shows | The server (`POST /provider/bookings/{id}/start`) |
|---|---|---|
| Before the scheduled time | no button, a muted line: *Scheduled for Nov 27, 2026 · 09:30 AM. Start button unlocks at scheduled time.* | 400 *Cannot start a booking before its scheduled date and time.* |
| 0 – 15 minutes after | the blue **Mark In Progress** | starts it: `STARTED_AT` = now, no reason kept |
| 16 minutes – 2 hours after | the amber **Mark In Progress (Late)** + "25 minutes late"; it opens **Late Start**, where a reason is required (up to 500 characters) | 400 without a reason (or with more than 500 characters); with one: `STARTED_AT` = now, `LATE_REASON` saved |
| More than 2 hours after | no button, a red line: *Start window missed … cancelled automatically (Provider No-Show).* | 400 *Booking start window has expired.* |

- The page redraws itself when a card's state changes (each card says in how many seconds; `booking-start.js`),
  so the button appears at 9:30 and turns amber at 9:46 without pressing Refresh.
- The provider always comes from the sign-in cookie; the browser only says which booking and, when late, the reason.
  Only the assigned, Active, Approved provider can start it, only from Accepted, and only once the customer has paid or
  chosen cash on completion (as before).
- The time is read **once** on the server: the same moment is checked and saved, so a click at 9:45:59 can never be
  saved as 9:46.
- Without JavaScript the buttons are plain forms: the page reloads with the same message (green or red).

## The provider no-show
A booking the customer **paid** (test e-wallet) or **promised cash** for, that is still not started **more than 2 hours**
after its time, is cancelled by the website itself (`NoShowCancellationService`, a minute after the site starts and then
every 5 minutes):
- `STATUS` Accepted → Cancelled; a test e-wallet payment becomes **Refunded** (in BOOKINGS and PAYMENTS_DTL). A cash
  promise simply ends (nothing was paid; the cards say "nothing to pay / nothing to collect").
- System Logs (actor **System**): *"System updated Booking #33: status 'Accepted' -> 'Cancelled' (Provider No-Show —
  missed scheduled start window)."*
- The customer's bell: *"Your booking #33 on Nov 27, 2026 was cancelled because the provider did not start it within
  2 hours of the scheduled time. Your ₱650 test payment was refunded. Please book another provider."* The provider's
  bell says it was cancelled automatically (no longer "cancelled by the customer").
- An **unpaid** quotation is not cancelled (nothing was scheduled); the admin list shows it once its date has passed.
- On hosting that sleeps when nobody visits (the MonsterASP free plan) the check runs once the site is awake again.

Why there is no "cancel reason" column: customers can only cancel **Pending** requests, which never have a payment,
and the database allows Accepted → Cancelled **only** for a no-show. So *Cancelled + a payment row* is always a
no-show — it is worked out, not stored (rule 2).

## The database
| Column | Type | Meaning |
|---|---|---|
| `STARTED_AT` | DATETIME2 NULL | when the provider pressed the button (UTC) |
| `LATE_REASON` | NVARCHAR(500) NULL | why, when more than 15 minutes late |
| `MINUTES_LATE` | INT NULL, **calculated** | minutes late, only for a late start |
| `IS_LATE` | BIT NOT NULL, **calculated** | 1 for a late start, otherwise 0 |

`MINUTES_LATE` and `IS_LATE` are computed columns (not stored): they always follow from `STARTED_AT` and the booking's
date and time, so they can never disagree with them.

The rules the database enforces itself (so SSMS or another program cannot get around them either):
- **CHECK `CK_BOOKINGS_START`**: no start yet (both NULL), or a start on an In Progress / Completed booking from 0 to
  120 minutes after its time, with a reason **exactly** when it was more than 15 minutes late.
- **`trg_Bookings_StatusFlow` v2.12**: `STARTED_AT` must be "now" when the job starts (50163; 5 minutes allowed between
  the app's clock and SQL Server's), and `STARTED_AT` / `LATE_REASON` are written once (50164). Accepted → Cancelled is
  allowed only for a no-show: not started, a payment exists, more than 2 hours after the time, not Paid any more
  (50162).
- **`usp_UpdateBookingStatus` v2.12** takes `@LateReason` and gives the same answers as the website (50156 too early,
  50157 expired, 50158 no reason, 50159 reason too long).
- **`trg_Bookings_Audit` v2.12**: the status line says *"(started on time)"* or *"(late start: 25 minutes late, reason
  'Flat tire')"*, and the no-show line above.
- **`usp_GetBookingsNeedingAttention` v2.12**: the late starts and no-shows of the last 7 days are on the
  administrator's list (*"Late start (25 min late)"*, *"Provider no-show (not started within 2 hours)"*), with
  `LateStartCount` / `NoShowCount`. **Admin → Dashboard → Needs Admin Attention** shows them on their own row:
  *"1 provider no-show and 2 late starts in the last 7 days"* → View Bookings.

## Who sees what
- **Provider:** the states above; an In Progress / Completed card shows *Started on time · 9:32 AM* or *Started late ·
  9:57 AM · 27 minutes late* with the reason; a no-show card says so.
- **Customer → My Bookings:** the same start line (with the reason), and the no-show note on a cancelled booking.
- **Admin → Bookings:** a red *No-show* tag in the list; **View** has a *Job Start* section.

## Install (laptop)
1. Extract the zip in the repo root. The database must already be on v2.11 (`tools\add-booking-completion.ps1`).
2. `powershell -ExecutionPolicy Bypass -File tools\add-start-window.ps1`. It builds and runs the tests, makes and
   checks the migration "StartWindow", says how many existing bookings the no-show check will cancel, makes a backup,
   asks for YES, then checks the result, including a live test inside a transaction that is always rolled back.
3. Optional: run `docs\sql\DatabaseDesign-v2.sql` on a **copy** of the database for the full self-test (tests 38-45).

## Good to know
- Existing bookings are not changed by the update itself. But Accepted, paid bookings that are already more than
  2 hours past their time (old test bookings, for example) are cancelled by the website about a minute after it starts.
  The install script and `docs\sql\check-monster-before-deploy.sql` show how many first; the backup keeps them as they were.
- Jobs started before v2.12 have no `STARTED_AT`: they show no start line.
- Development scripts that use `servisyo_dev_override` can still jump a booking to any status; the CHECK still applies.
