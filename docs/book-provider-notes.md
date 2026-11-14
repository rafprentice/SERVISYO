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

## Exact start times (Nov 2026)
- **The provider's exact shift start** is offered as it is (a shift from 4:04 AM offers 4:04 AM), then every :00 and :30.
- **Today, when the provider needs 0 notice days**, the first button is the **soonest** start: the next whole minute in
  Philippine time (4:05 AM when it is 4:04 AM), marked *Soonest*. The list for today is asked for again every minute.
- **Enter exact time** (under the buttons) opens an `<input type="time">`: any whole minute. The server checks it as
  the customer types (`GET customer/book/{providerId}/check-time`) and again when the form is sent: at least the next
  whole minute today, inside the working hours for the whole job, outside the break, not overlapping another active
  booking, and no seconds. Under the box: the provider's hours that day.
- The form no longer requires "one of the listed times": any time that passes those rules is saved
  (`BookingSlotRules.CheckTime`, `BookProviderService.CreateAsync`).
- Note: the SQL procedure `usp_CreateBooking` in DatabaseDesign-v2.sql still only takes :00 / :30 starts (50138). The
  website does not use it (it saves through EF), so this does not affect booking; aligning it would be a database update.
