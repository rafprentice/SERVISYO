# Approve & Pay (demo) + live booking updates

## Approve & Pay (no payment gateway)
- `POST /customer/bookings/{id}/pay` (`CustomerPaymentsController` -> `BookingPaymentService`). Customer accounts only.
- Allowed only for the customer's OWN booking that is `Accepted` (quotation given) and `Unpaid`.
- Sets `PAYMENT_STATUS = Paid` (no money moves). The booking `STATUS` keeps the six PDF values; the meter reads both columns:

| Meter step | STATUS | PAYMENT_STATUS |
|---|---|---|
| Requested | Pending | Unpaid |
| Quotation | Accepted | Unpaid |
| Paid / Scheduled | Accepted | Paid |
| In Progress | In Progress | Paid |
| Completed | Completed | Paid |

- NEW RULE: the provider's "Mark In Progress" appears (and is accepted by the server) only after the customer paid.
- The provider gets a bell notification "Booking #N was approved and paid (₱650). It is now scheduled."
- The booking row's concurrency token stops a payment that races with a provider change.

## Live updates (SignalR)
- Hub: `/hubs/bookings` (`Hubs/BookingHub.cs`), signed-in users only. Browsers only listen.
- After a booking request, accept/reject, payment, start, complete or customer cancel, the server sends
  `bookingChanged` to the customer's and provider's user ids (`IBookingLiveUpdates`). The message has no personal data.
- `wwwroot/js/live-bookings.js`: refreshes the bell; on pages with `ViewData["Live"] = true`
  (customer dashboard, My Bookings, provider dashboard, Booking Requests) it re-downloads the same page and swaps the main
  area, so the stepper, badges and buttons are drawn by the server with all its permission checks. If the user is typing,
  or on other pages, it shows a small "Booking #N was updated — Refresh" note instead.
- If the SignalR script cannot load or the connection fails, everything still works with normal page loads.

## Setup
Run `tools\finish-setup.ps1` (adds `AddSignalR()`, two services and `app.MapHub<BookingHub>(...)` to Program.cs).
No database change.

## Hosting note
MonsterASP free sites run on IIS; SignalR uses WebSockets when enabled and falls back to long polling automatically.
