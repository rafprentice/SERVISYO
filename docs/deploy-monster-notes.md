# Deploy to MonsterASP (database v2.12)

The online database must be updated **before** the new website files go up: the new code reads the column
`PROVIDER_UNAVAILABLE_DATES.APPROVED_BY_ACTION_ID` (v2.4), which the old database does not have.

Before you start: on the laptop, the newest database script (now `tools\add-start-window.ps1`, v2.12) ended with
DONE, the site works with `dotnet run`, and the code is committed and pushed.

Monster may be several steps behind the laptop (each step is one EF migration). For example, in November 2026 it
still needed v2.1 to v2.6; after that deploy it still needs v2.7 (day-off requests), v2.8 (name change requests),
v2.9 (inactive accounts), v2.10 (the System Logs standard format), v2.11 (completion proof) and v2.12 (the start
window of "Mark In Progress" and the provider no-show).
The file imports every missing step in date order.

## 1. Make the files (laptop)
```powershell
powershell -ExecutionPolicy Bypass -File tools\make-monster-deploy.ps1
```
It builds, runs the tests, and opens a folder `Documents\SERVISYO-deploy\<date-time>` with:
- `deploy-latest.sql`: the database update. EF makes the steps; the script adds checks:
  - every step is all-or-nothing;
  - a step waits if an earlier one is missing;
  - v2.5 refuses to run while two providers share a day off;
  - the last check says whether the update is complete;
  - steps Monster already has are skipped, so it is safe to import again;
  - it starts with `SET QUOTED_IDENTIFIER ON` (the one-request-at-a-time indexes of v2.7 and v2.8 need it).
- `servisyo-publish.zip`: the website files. It leaves out:
  - `appsettings.Development.json`;
  - `appsettings.Production.json`: Monster keeps its own copy, with your connection string and email settings;
  - `wwwroot\uploads`: the profile photos and the proof photos of completed jobs (v2.11) on Monster are not touched.

It changes nothing online. The folder also has `check-monster-before-deploy.sql`.

## 2. Check the online database first (read-only)
While the website is still running, import `check-monster-before-deploy.sql` the same way as in step 5 (or run it in
SSMS and look at the Messages tab). It changes nothing and prints one line per finding:
- **FIX** lines stop the update. Fix them first: send the lines to Claude for the exact fix.
- **INFO** lines are for you to know. For example, v2.3 removes 4 tables whose data is now calculated, and providers
  above 20 jobs a day become 20.

Continue only when the last line says **READY**.

## 3. Back up the online database
Control Panel → **Databases** → your database → **Backups** → **Create BAK file**. When it shows in *Backup files*,
click **Download** and keep it in `Documents\SERVISYO-backups`. (On the free plan there are no automatic backups.)

## 4. Stop the website
**Websites** → your site → **Overview** → **Stop**. Monster puts an `app_offline.htm` page up, so nobody uses the site
while it changes and the files are not locked.

## 5. Update the database
**Databases** → your database → **Import** → **Upload SQL file** (`deploy-latest.sql`) → **Import SQL file**.
- No error = done. The last message is *SERVISYO: the database update is complete*, if Monster shows messages.
- An error (*Stopped without changing anything...* or *SERVISYO: ...*): the step that failed changed nothing, and the
  steps after it waited. Do **not** upload the files. **Keep the website stopped** and send all the messages to
  Claude. Earlier steps from the same import may already be in, and the old website files may not work with them.
  Claude tells you whether Start is safe.

## 6. Upload the website files
**File Manager** → `wwwroot` → upload `servisyo-publish.zip` → **Extract**, and say yes to overwrite. Then delete
`servisyo-publish.zip` from `wwwroot`.

Do not delete anything else: the `uploads` folder (profile photos and proof photos) and `appsettings.Production.json` stay as they are.

## 7. Start the website
**Overview** → **Start**. If the "being updated" page still shows, delete `app_offline.htm` in `wwwroot`.

## 8. Check online
- **Register as Provider:** Maximum Jobs Per Day stops at 20.
- **A provider's Availability page:**
  - the page opens;
  - Maximum Jobs Per Day is at most 20;
  - **Request Day Off**: 8 days are refused ("Maximum Day Off is 1 week or 7 days"), a date another provider has is refused,
    a good request shows "Pending Admin Approval".
- **Admin → Providers → that provider:** the Days Off section shows the request with Approve / Reject.
- **Admin → Users → View:** the customer's details and recent bookings open.
- **A provider's My Profile → Request Name Change** (v2.8): the same name is refused, a real change shows
  "Pending Admin Approval".
- **Admin → Dashboard:** "Needs Admin Attention" shows the day-off and name change requests; each button opens its page
  (Review Day Offs opens that provider, View Accounts the Suspended tab). **Admin → Name Changes:** Approve changes the
  provider's name.
- **Admin → System Logs** (v2.10): the pills All / INSERT / UPDATE / DELETE / LOGIN show numbers; change a phone
  number on a customer's profile, then the UPDATE pill shows *"... updated User #.. : phone '...' -> '...'."*
- **Provider → Booking Requests → an In Progress booking → Mark Completed** (v2.11): the "Confirm Service Completion"
  window opens; without a photo or a message it shows the red messages; with both, the job is completed and the
  customer's My Bookings card shows the photo and the message.
- **Provider → Booking Requests → a scheduled (Accepted, paid) booking** (v2.12): before its time the card only says
  *"Scheduled for ... Start button unlocks at scheduled time."*; up to 15 minutes after it, the blue **Mark In Progress**;
  16 minutes to 2 hours after, the amber **Mark In Progress (Late)**, which asks for a reason.
- **About a minute after the new site starts** (v2.12): Accepted, paid (or cash) bookings that are more than 2 hours past
  their time without a start are cancelled as **provider no-shows** (a test e-wallet payment becomes Refunded). The
  read-only check above lists how many there are (INFO line) before you import. Admin → System Logs shows one
  *"System updated Booking #N: status 'Accepted' -> 'Cancelled' (Provider No-Show ...)"* line for each.
- **Monster → Logs:** no new errors.

## The inactive-account clean-up (v2.9)
It starts in **report only** mode: it changes nothing, and the admin dashboard (Needs Admin Attention) shows how many
accounts it would warn (v2.10; nothing is stored). To switch it on online, add this to `appsettings.Production.json` on Monster (File Manager →
`wwwroot`), next to the settings already there, then Stop / Start the site:
```json
"InactiveAccounts": { "DeleteEnabled": true }
```
The email settings must work first: the 30-day warning emails go out through them, and nobody is removed without one.
On the free plan the site sleeps when nobody visits; the job runs each time the site starts, then every 24 hours
while it is awake.

## If something goes wrong
- **Database:** **Backups** → **Restore** the BAK from step 3. Monster also saves a safety backup
  `_before_restore_...bak` first.
- **Website files:** send the error (Monster **Logs**) to Claude. Each run of the script makes a new folder, so next
  time the previous zip is still there to upload again.

## Security
`appsettings.Production.json`, the BAK files and the deploy folder hold real data or passwords. Never commit them.
They are outside the repo or in `.gitignore`. Also change the online admin password if it is still the sample one.
