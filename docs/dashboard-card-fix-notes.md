# Customer dashboard: current-booking card fix

## Cause of the squashed stepper
The availability page's number stepper (+/-) also used the CSS class `.sv-stepper` and, being later in portal.css,
overrode the dashboard's progress bar (`inline-flex` made it narrow, `align-items:center` raised step 5).
The dashboard progress bar now uses its own class `.sv-track`; labels never wrap (`white-space: nowrap`).

## Buttons now real
- Waiting for Quotation (Pending): **Cancel Request** (confirm box, same server rule as My Bookings: Pending only, own booking only).
- Quotation Received / Booking Confirmed / In Progress: **Message Provider** (opens the Messages conversation).
- Quotation Received: Approve & Pay stays disabled (no payment feature exists in the PDF/schema yet).
- Completed: Leave a Review stays disabled (reviews not built yet).

## Not changed
The card title is the booking's SERVICE name(s). If it shows the provider's name, the provider named the service
that way (Provider > My Services). Quotation/Paid steps stay as before: Accepted+Unpaid = "Quotation Received".
