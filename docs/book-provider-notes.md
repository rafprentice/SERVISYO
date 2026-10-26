# Book this Provider — notes

## What it does
Find Services -> "Book this Provider" opens `/customer/book/{providerId}`. The customer picks services, a date, a free time, an address and a description. The booking is saved as **Pending / Unpaid** and the provider gets the notification "You have a new booking request (Booking #n)."

## Rules enforced on the server (inside one Serializable transaction)
- Only an Active customer; the customer always comes from the sign-in cookie.
- Provider must be Approved + Active, with at least one active service in an active category.
- Services must belong to that provider and be active; price and duration are read from the database, never from the form.
- Date >= today + provider's MIN_NOTICE_DAYS, and at most 90 days ahead.
- Not an unavailable date; the weekday schedule must be switched on.
- Daily limit (MAX_JOBS_PER_DAY) counts Pending/Accepted/In Progress bookings.
- Time must be one of the offered 30-minute starts: whole job inside working hours, not touching a break, not overlapping an active booking.
- Race safety: Serializable transaction + filtered unique index `UX_BOOKINGS_ACTIVE_SLOT`; a collision returns "That time was just taken by someone else."

## Deliberate differences from the PDF
- The whole job (total duration) must fit working hours and avoid breaks (PDF checks the start time).
- Any overlap with an active booking blocks a slot (PDF: same start time only).
- 90-day advance limit and 30-minute grid.
- JavaScript is needed to pick a time.
- TotalFee = sum of service prices (BaseFee not added).

## Install
1. `Expand-Archive -Path "$env:USERPROFILE\Downloads\book_provider.zip" -DestinationPath . -Force`
2. In Program.cs: `builder.Services.AddScoped<IBookProviderService, BookProviderService>();`
3. `dotnet build`, `dotnet test`, `dotnet run`

## Not done
Dashboard "Book Again" and the recommended-provider rows have no Book button yet. Nothing here was compiled in the authoring environment.
