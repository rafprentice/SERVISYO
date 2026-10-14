# Provider > Booking Requests — implementation notes

Page: **Bookings** in the provider sidebar → `/provider/bookings` (optional `?status=pending|accepted|in-progress|completed|rejected|cancelled` and `&page=2`).

## What it does

| Part | Behaviour |
|---|---|
| Tabs | All, Pending, Accepted, In Progress, Completed, Rejected, Cancelled — each with a count. Counts always cover **all** of the provider's bookings, whichever tab is open. |
| Cards | Service title + `#id`, "from *Customer* · *Category*", status badge, Date & Time, Location, Submitted (Cebu time), and the customer's request notes in a grey box. Newest request first (then highest id). 20 per page. |
| Pending | **Accept** and **Reject**. Reject asks "are you sure?" first. |
| Accepted | **Mark In Progress** |
| In Progress | **Mark Completed** |
| Completed / Rejected / Cancelled | No buttons (finished). |
| Empty tab | "No bookings in this category." |
| After a click | Redirect back to the same tab/page with a green (done) or red (refused) message. A refresh never repeats the action. |

The customer receives a notification with the same wording as `usp_UpdateBookingStatus` in the PDF:
`Your booking #12 is now Accepted.`

## Business rules and where each is enforced

| Rule | Where |
|---|---|
| Only a signed-in Service Provider reaches the page | `[Authorize(Roles = "Service Provider")]` on the controller |
| Only an **Active** account with an **Approved** application may list or change bookings (a suspended/deactivated provider cannot accept anything) | `ProviderBookingsService.FindAsync` — pending applicants go to the "pending approval" page, rejected ones to the dashboard message, everyone else is signed out |
| A provider can only touch **their own** bookings | The provider id is read from the sign-in cookie; the booking is looked up with `BookingId = x AND ProviderId = mine`. Someone else's booking answers exactly like a missing one ("could not be found"). The browser never sends a provider/customer id. |
| Only the permitted status changes: Pending → Accepted / Rejected, Accepted → In Progress, In Progress → Completed | `BookingActionInfo` (one table) checked in `ChangeStatusAsync`; anything else is refused with a message and nothing changes (so a double-click, an old tab, or a customer who just cancelled all fail safely) |
| Cancelled is **not** a provider action (customers cancel their own Pending bookings) | There is no "cancel" action in the page or the service |
| Accepting must not clash with a job the provider already accepted or started on the same day | Time ranges are compared (`start < otherEnd && otherStart < end`), so jobs that only touch end-to-start (9–10 and 10–11) are fine. Only **Accepted** and **In Progress** jobs count; other Pending requests, other days, other providers and finished jobs do not. |
| Two requests cannot accept clashing jobs at the same moment | On SQL Server the accept runs in a transaction that first locks the provider's row (`UPDLOCK, HOLDLOCK`), so one provider's accepts run one after another. The booking row also has a `RowVersion`, so if the customer cancels at the same instant the save fails and the provider is told "just changed". The existing filtered unique index `UX_BOOKINGS_ACTIVE_SLOT` still stops two active bookings with the same start time. |
| Status change + customer notification are saved together | One `SaveChanges` (and one transaction on SQL Server) |
| Action names are checked | The form posts one of four names; a missing/unknown/undefined value is refused and never defaults to "Accept" |
| CSRF | Every button is a POST form with the antiforgery token (global filter + `[ValidateAntiForgeryToken]`) |

## Files

New
- `Controllers/ProviderBookingsController.cs`
- `Services/ProviderBookingsService.cs`
- `ViewModels/ProviderBookingsViewModels.cs`
- `Views/ProviderBookings/Index.cshtml`
- `wwwroot/js/provider-bookings.js` (confirm on Reject, lock the card after one click)
- `tests/Servisyo.Tests/ProviderBookingsTests.cs`
- `docs/dev-sample-provider-bookings-data.sql`, `docs/provider-bookings-notes.md`

Changed
- `Views/Shared/_PortalLayout.cshtml` — the provider **Bookings** link is now live (it was greyed out).
- `Views/Dashboard/Provider.cshtml` — "View All", "View booking details", the row buttons (Review Request / Start Service / Complete Service) and Today's Schedule "View" now **link** to this page (they were disabled placeholders). They open the matching tab; they do not change a booking by themselves.
- `wwwroot/css/portal.css` — `.sv-bk*` styles added at the end.

## One line to add (Program.cs)

```csharp
builder.Services.AddScoped<IProviderBookingsService, ProviderBookingsService>();
```

## Choices I made where the design/PDF is silent

- **Category** is shown from the services inside the booking (a booking has no category column). A booking with two services in two categories shows "Plumbing, Roofing".
- **Badge text** is the plain status ("Accepted", "Rejected", …) as in the mockup — not the friendlier "Scheduled"/"Declined" the dashboard uses.
- **Accept** is the primary (indigo) button and **Reject** a red outline button; the mockup colours were not available to me in detail.
- **No date guard** on Mark In Progress / Mark Completed — `usp_UpdateBookingStatus` has none, so a provider can start or finish a job before its date. Say if you want one.
- **Daily job limit / minimum notice / working hours** are *not* re-checked on Accept. The PDF checks them when the customer **creates** the booking (`usp_CreateBooking`), which is the customer-side stage that is not built yet. Accept only re-checks the overlap with already-accepted jobs, as the project rules require ("recheck booking conflicts").
- **Payment** is untouched (Option A vs B is still open).

## Not covered by automated tests

- The SQL Server-only parts (the `UPDLOCK` row lock and the `RowVersion` conflict message) — the unit tests use the in-memory database, which has no transactions or real locking. Please try them once for real (two browser windows, same provider, two overlapping Pending requests, press Accept in both quickly).
- The Razor view and the JavaScript were checked by reading/syntax only.

## Manual test checklist

1. Run the services sample, then `docs\dev-sample-provider-bookings-data.sql` (edit `@ProviderEmail` first).
2. Sign in as that provider → **Bookings**. Tabs should read All (9) · Pending (3) · Accepted (1) · In Progress (1) · Completed (2) · Rejected (1) · Cancelled (1).
3. Pending tab → **Accept** the "Fixture Install … 9:30 AM" request (Dennis): it must be **refused** (overlaps the accepted Pipe Repair 9–11). Reject it → asks to confirm → moves to Rejected.
4. Accept the other two Pending requests → they move to Accepted; the customer gets a notification (check `NOTIFICATIONS`).
5. Accepted → **Mark In Progress** → In Progress → **Mark Completed**. Completed/Rejected/Cancelled cards have no buttons.
6. Press the browser Back button after a click and try the same button again → refused with a message, nothing changes twice.
7. Sign in as a *different* provider, open `/provider/bookings` → none of the first provider's bookings are visible.
8. Run the sample script again with `@Refresh = 1` to reset.
