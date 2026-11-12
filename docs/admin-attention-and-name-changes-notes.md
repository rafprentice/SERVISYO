# Admin Dashboard "Needs Admin Attention" + Name Change Requests (database v2.8)

## Needs Admin Attention (Admin > Dashboard)
Each row appears only while something really waits. The counts come from the database every time the page loads.

| Row (dot colour) | Counted from | Button | Goes to |
|---|---|---|---|
| "[X] provider day-off request(s) awaiting approval" (amber) | `PROVIDER_DAY_OFF_REQUESTS` still Pending, with a day from today on (the same list as Admin > Service Providers) | **Review Day Offs** | `/admin/providers?review={id}`: the provider who has waited longest opens by itself, and their request is highlighted |
| "[X] name change request(s) pending review" (amber) | `PROVIDER_NAME_CHANGE_REQUESTS` still Pending | **Review Requests** | `/admin/name-changes` (Pending tab) |
| "[X] new service provider application(s) pending" (red) | providers with APPROVAL_STATUS Pending | **Review Providers** | the oldest application, in the review pop-up (as before) |
| "[X] suspended account(s) requiring review" (red) | Suspended customers and providers | **View Accounts** | `/admin/users?status=Suspended` (Suspended tab active). Only providers suspended: `/admin/providers?status=Suspended`. Both: the row adds "(2 customers, 1 provider)" and a second link **Suspended providers** |
| flagged reviews (amber), bookings that need attention (violet) | unchanged | Moderate Reviews / View Bookings | unchanged |

- Nothing waits: *"All caught up! No pending requests requiring admin attention."*
- Every button is a real link (hand cursor; hover = indigo-100 background). The "soon" disabled button is gone:
  every row now has a page to go to (`AdminAttentionAction`, `Views/Dashboard/Admin.cshtml`).
- **Live:** when a provider sends or cancels a day-off or name change request, or an administrator answers one, the
  server pushes `dayOffChanged` / `nameChangeChanged` (SignalR, hub group "admins"). The bell and this section
  (`#sv-attn`, `data-sv-admin-live="dayoff namechange"`) are fetched again by themselves (`admin-approval.js`).
  If the live connection is down, the page asks every 30 seconds.
- `?review=12` is only a number from the address: the pop-up asks the server for provider 12 with every normal check.
  After it opens, `?review` is removed from the address, so a reload does not open it again.

## Name change requests (database v2.8)
An approved provider's name is locked ("Profile updates require Admin assistance."), so they ask an administrator.

| Rule | Where it is enforced |
|---|---|
| Only an **Active, Approved** provider can ask (found from the sign-in cookie, never from the browser) | `NameChangeRequestService.RequestAsync` |
| First, middle and last name: letters only, middle name at least 2 characters, at most 100 each (the same rules as provider registration); a reason is required (max 255) | page (`provider-profile.js`) + server |
| It must be a real change (*"That is already the name on your account..."*) | page + server |
| **One waiting request per provider** (the button is greyed out and says why) | server (serializable transaction) + the database: filtered UNIQUE index `UX_PROVIDER_NAME_CHANGE_REQUESTS_ONE_PENDING` |
| The provider can **cancel** their own request while it is Pending | `CancelAsync` (only their OWN request) |
| **Approve** copies the new name to `USERS` (one place for the name); **Reject** changes nothing | `DecideAsync` (Active administrators only) |
| Each answer is an `ADMIN_ACTIONS` row ('Name Change Approved' / 'Name Change Rejected', REASON = "old name to new name") that the request points to (`DECIDED_BY_ACTION_ID`), saved in the same transaction; a request is answered only once | `DecideAsync` + CHECK `CK_PROVIDER_NAME_CHANGE_REQUESTS_DECISION` |

- **Provider > My Profile:** the card "Name on Your Account" (under Business Information) with **Request Name Change**
  (a pop-up that opens only when clicked, or when the server sent it back with problems: `data-open="true"`).
  While a request waits it shows *"Pending Admin Approval"* and **Cancel Request**. When the administrator answers,
  the card, the header name and the locked name boxes change by themselves (live).
- After an approval the public pages show the new name from the next visit to My Profile (the sign-in cookie is
  refreshed there).
- **Admin > Name Changes** (new item in the sidebar): tabs Pending / Approved / Rejected / All, Approve and Reject ask
  first. The list updates by itself.
- **The bell:** administrators see every waiting request (`name-change-new`); the provider sees the answer (`name-change`).

### Database v2.8 ("NameChangeRequests")
- New table `PROVIDER_NAME_CHANGE_REQUESTS` (REQUEST_ID, PROVIDER_ID, FIRST_NAME, MIDDLE_NAME, LAST_NAME, REASON, STATUS,
  REQUESTED_AT, DECIDED_BY_ACTION_ID). Only the NEW name is stored; the current one stays in USERS (the USERS audit
  trigger keeps the old one in SYSTEM_LOGS when it changes).
- CHECKs on STATUS (Pending / Approved / Rejected) and on the decision; FOREIGN KEY to ADMIN_ACTIONS (no cascade, UNIQUE);
  filtered UNIQUE index: one Pending request per provider.
- `usp_GetNotifications` changed (`Data/NameChangeRequestsSql.cs`); nothing else.
- Run `tools\add-name-change-requests.ps1` once (after v2.7). It changes no data: the table starts empty.
- The design file `docs/sql/DatabaseDesign-v2.sql` is v2.8 (PART 1 table, PART 4 procedure, self-test 34).

## Tests
`tests/Servisyo.Tests/NameChangeRequestTests.cs` (asking, the rules, one at a time, cancel, approve / reject, the
pages, the model, the SQL, the design file, the tools), `AdminDashboardTests.cs` (the rows, their wording, colours and
links; the counts from the data).
