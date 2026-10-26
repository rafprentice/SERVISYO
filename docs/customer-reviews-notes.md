# Leave a Review + Book Again (customer)

## Leave a Review
- Page: `GET/POST /customer/bookings/{bookingId}/review` (`CustomerReviewsController`, Customer accounts only).
- Opened from the dashboard's "Service complete" card and from **My Bookings** (Completed, not yet reviewed).
- Rules (project PDF): only the booking's own customer, only Completed bookings, one review per booking, 1 to 5 stars,
  comment optional (max 255). Someone else's booking id is answered like a missing booking.
- On save, in one database transaction: the review (Visible), the provider's `AvgRating` / `ReviewCount`
  (Visible reviews only, same rule as `fn_GetProviderAvgRating` and the admin moderation page), and a bell notification for the provider.
- Two submits at the same moment: the unique index on `REVIEWS.BookingId` rejects the second; the customer is told it was already reviewed.
- Providers cannot create or edit reviews (no provider route exists). Admins moderate on the existing Reviews page.

## Book Again
- Dashboard "Recent Bookings" → Completed rows link to `/customer/book/{providerId}?service=ID&service=ID`.
- The booking page ticks those services only if the provider still offers them (active service, active category, at most the usual limit).
  Everything is validated again when the form is sent, so the link cannot be used to book something unavailable.

## Setup
`builder.Services.AddScoped<ICustomerReviewService, CustomerReviewService>();` in Program.cs
(or run `tools\finish-setup.ps1` again). No database change: the REVIEWS table already exists.

## Known limits
- Two different customers reviewing the same provider at the same instant could leave the average one review behind
  until the next review or moderation recalculates it.
- Reviews cannot be edited by the customer after posting (by design, matches "providers cannot edit"; admins can hide/restore).
