# Customer > My Bookings — implementation notes

Page: **My Bookings** in the customer sidebar → `/customer/bookings`.
(On the customer dashboard, **View all** and **View booking details** now open this page too.)

## What it does

| Part | Behaviour |
|---|---|
| Header | "HISTORY" label, **My Bookings**, "Track and manage all your service requests." |
| Filter tabs | **All (n)** plus one tab for each status the customer actually has: Pending, Accepted, In Progress, Completed, Rejected, Cancelled — each with its count. The address keeps the tab (`?status=accepted`), so the Back button and bookmarks work. |
| Booking card | Service title + booking number (`#1001`), "Provider name · Category", a coloured status badge, **Date & Time**, **Location**, **Submitted**, and the customer's request notes in a grey box. |
| **Cancel Request** | Only on **Pending** bookings. Asks "are you sure?", then cancels. The provider gets a notification ("Booking #n was cancelled by the customer."). |
| **Leave a Review** | Shown on **Completed** bookings that have no review yet. *Disabled for now* (see choices below). A booking that already has a review shows "You reviewed this booking" instead. |
| Paging | 10 bookings per page, Previous / Next. A page number outside the range is corrected. |
| Empty states | "No bookings yet" with a **Find a Service** button (no bookings at all) or "No bookings in this category." (a tab with nothing in it). |
| After cancelling | Redirect back with a green message. A refresh never repeats the cancel. When cancelling from the Pending tab you land on **All**, where the booking now shows as Cancelled. |

## Rules and where each is enforced

| Rule | Where |
|---|---|
| Only a signed-in **Customer** reaches the page | `[Authorize(Roles = "Customer")]` on the controller |
| Suspended / deactivated / removed customers are signed out | `CustomerBookingsService` returns `Allowed = false` → controller signs out |
| A customer sees **only their own** bookings | Every query starts from `Bookings.Where(CustomerId == <customer found from the sign-in cookie>)`; the address carries only `status` and `page` |
| A customer can cancel **only their own** booking | `CancelAsync` loads the booking by id **and** customer id. Someone else's booking gets the same answer as a missing one ("That booking could not be found.") — it never reveals that the booking exists |
| Customers can cancel **only Pending** bookings | `CancelAsync` refuses every other status on the server, whatever the browser posts (the button is only hidden for other statuses — that is not the protection) |
| Provider answers and customer cancels at the same moment | The booking row has a concurrency token (`RowVersion`). If the provider's Accept/Reject was saved first, the cancel is refused with "…was just updated by the provider…"; if the cancel was saved first, the provider's Accept is refused. Nobody's answer is silently overwritten |
| Status change and notification are saved together or not at all | One `SaveChanges` (a single database transaction) |
| Cancelling frees the time slot | `Cancelled` is not an "active" status, so the database's one-active-booking-per-slot rule no longer counts it |
| A double click / refreshed form cannot cancel twice | The second request finds the booking already Cancelled and is refused (tested); the button also locks after the first click |
| Booking history stays visible if the provider is later suspended | The list does not filter on the provider's account status (tested) |
| CSRF | `[ValidateAntiForgeryToken]` + the form tag helper |

## Files

New
- `Controllers/CustomerBookingsController.cs`, `Services/CustomerBookingsService.cs`, `ViewModels/CustomerBookingsViewModels.cs`
- `Views/CustomerBookings/Index.cshtml`, `wwwroot/js/customer-bookings.js`
- `tests/Servisyo.Tests/CustomerBookingsTests.cs`
- `docs/customer-bookings-notes.md`, `docs/dev-sample-customer-bookings-data.sql`

Changed
- `Views/Shared/_PortalLayout.cshtml` — customer **My Bookings** link is live.
- `Views/Dashboard/Customer.cshtml` — "View all" and "View booking details" are links now (this file also carries the Find Services links from the previous stage).
- `wwwroot/css/portal.css` — a few `.sv-cb*` lines at the end. The cards, tabs and pager reuse the `.sv-bk*` / `.sv-tab*` styles of the provider's Booking Requests page.

## One line to add (Program.cs)

```csharp
builder.Services.AddScoped<ICustomerBookingsService, CustomerBookingsService>();
```

## Choices I made where the design is silent — please check these

1. **Cancel Request is an addition.** The design shows no Pending booking, so no cancel button. But the project rules say a customer can cancel a Pending booking, and this page says "manage all your service requests", so I added it (with a confirmation). Remove the `@if (c.CanCancel)` block in the view if you do not want it yet.
2. **Leave a Review is disabled** (tooltip: "Reviews open in an upcoming update"). The design does not show the review form, and writing it here would mean guessing its fields. It is the natural next page. The card already knows which bookings are eligible: Completed, no review yet.
3. **Tabs: only statuses that exist.** The design shows All, Accepted, Completed — consistent with "hide empty tabs". If you would rather always show all seven tabs, it is a one-line change in the service (remove the `.Where(...)` on `tabs`). A tab you open by address that has nothing in it still appears, with (0).
4. **Order: newest request first** (by the day it was submitted, then booking number). The design's two cards fit this order.
5. **Provider shown as the person** ("Marco Reyes · Plumbing"), as in the design. The category comes from the services in the booking (a booking has no category column of its own); a booking with services in two categories shows "Plumbing, Cleaning".
6. **Booking number is the real database id** (`#1001`), not the `BK-2026-0001` style used on the dashboard card. The design shows `#1001`.
7. **Date & Time** follows the screenshot ("Sep 20, 2026 · 9:00 AM"). **Submitted** is the day the request was sent, in Philippine time.
8. Location or notes that are empty are not shown as blanks: the location says "Not provided", the notes box is left out.
9. **Payment** (Paid / Unpaid) is not on the page — the design does not show it.
10. The page is a centred column about 900 px wide, like the design.
11. The header's **Provider / Admin / Home** switch buttons are not built — same as on the other pages.
12. The customer **Messages** and **My Profile** links in the sidebar are still disabled (their pages do not exist yet).

## Not covered by automated tests

- The "provider answered at the same moment" path — the in-memory test database does not enforce the concurrency token, so it needs real SQL Server (see manual check 8).
- The Razor view, CSS and JavaScript were checked by reading and syntax only — never opened in a browser.
- Real SQL Server translation of the queries (the tests use the in-memory database).

## Try it with sample data

The app has no customer-side "book a service" page yet, so a new customer has no bookings. This script gives one customer seven bookings, one per status:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -I -i docs\dev-sample-customer-bookings-data.sql
```

First edit `@CustomerEmail` at the top to the e-mail of a customer you can sign in as, and run `docs\dev-sample-providers-data.sql` before it if you have not. **Use `-I`** — without it SQL Server refuses to change the bookings table. Set `@Refresh = 1` and run it again to put every status back after you have cancelled the pending one.

## Manual test checklist

1. Sign in as that customer → **My Bookings**. Tabs show All (7), Pending (1), Accepted (1), In Progress (1), Completed (2), Rejected (1), Cancelled (1). Newest request is first.
2. Click each tab → only that status; the address shows `?status=...`; Back works.
3. The Completed booking without a review shows a (disabled) **Leave a Review**; the reviewed one shows "You reviewed this booking".
4. Only the Pending booking has **Cancel Request**. Click it → "Are you sure?" → Cancel → green message, you land on All and the booking is now **Cancelled**. The Pending tab is gone (no pending bookings left).
5. Press **Back** and then the old Cancel button/form again (or double-click the button) → no second notification; at worst a red "already cancelled" message.
6. Sign in as the **provider** of that booking → the notification bell shows "Booking #n was cancelled by the customer." and **Booking Requests > Cancelled** lists it.
7. Edit the address to `?status=hack` or `?page=999` → shows All / the last page. Sign out and open `/customer/bookings` → sign-in page. Open it as a provider → access denied.
8. Race check (optional): open My Bookings (Pending booking) in one browser and Booking Requests in another. Press **Accept** as the provider first, then **Cancel Request** as the customer → red message "…just updated by the provider…", booking stays Accepted.
9. In **Admin > Users** suspend the customer, then press any link on My Bookings → signed out.
