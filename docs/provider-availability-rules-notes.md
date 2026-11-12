# Provider Availability - break and day-off rules

## The 1-hour break (Weekly Schedule)
| Rule | Where it is enforced |
|---|---|
| A day's working hours = End time - Start time | `BreakRules.ShiftMinutes` |
| Under 8 hours: no break. "+ Add break" is disabled and shows *"Break is only available for work shifts longer than 8 hours."* | page (`provider-availability.js`) + server (`ValidateDays`) |
| 8 hours or more (e.g. 7:00 am - 5:00 pm = 10 hours): one break, exactly 1 hour | page + server |
| The break starts between the shift's start and its end - 1 hour, chosen from a dropdown (every 30 minutes) | dropdown + server (`BreakRules.Fits`) |
| The page sends only the start; the end (start + 1 hour) is worked out on the server | `ValidateDays` |

- Exactly 8 hours counts as long enough (the request says ">= 8 hours"). The message text is kept as requested.
- Changing a day's hours rebuilds the dropdown right away. If the shift drops under 8 hours the break is greyed out and
  is **not sent** (so it is removed on save); lengthen the shift again and it comes back.
- **Breaks saved before this rule** (e.g. Monday 4:30 - 5:00 pm, 30 minutes): the page selects the nearest start that
  fits (4:00 pm) and says so; nothing changes in the database until **Save Schedule**. The booking page keeps using
  the saved break until then.

## Unavailable dates: Request Day Off (database v2.7)
Since v2.7 a provider does not mark days off directly any more: they **request** them, and an administrator
**approves** or **rejects** each request. The 5-days-a-month limit is gone; the administrator decides.

| Rule | Where it is enforced |
|---|---|
| **Request Day Off** (Availability > Unavailable Dates) opens a form: Start date, End date (empty = one day), Reason | `Views/ProviderAvailability/Index.cshtml`, `provider-availability.js` |
| **1 to 7 days in a row** per request; more is refused with *"Maximum Day Off is 1 week or 7 days"* (the End box only offers up to Start + 6 days) | page (message at once, Send is blocked) + server (`RequestDayOffAsync`, **400 Bad Request** with the form opened again) |
| From **tomorrow** up to **December 31 of this year** (Philippine time). Today: *"Same-day unavailable date selection is not permitted."* (page: *"Same-day off requests are not allowed..."*) | date `min`/`max` + page + server |
| Only **one provider per date**: a date another provider has off **or has requested** is refused (the other provider is not named) | page (`TakenDates`) + server + the UNIQUE index of v2.5 |
| A day with an active booking (Pending, Accepted, In Progress) cannot be requested | server, in a serializable transaction |
| **One request at a time** can wait for an administrator (so nobody holds many dates with requests); the button explains it while one waits | server + page |
| A new request is **Pending**: it shows *"Pending Admin Approval"*, holds its dates, but **does not stop bookings** | `DayOffQueries.BlockingBookings`, `fn_IsProviderAvailable`, `usp_CreateBooking` |
| **Approved** (by an administrator): its days stop bookings; the badge says *"Approved"* | same |
| **Rejected**: its days are deleted (the dates are free again, the provider stays bookable); the request row stays as the record | `DecideDayOffRequestAsync` |
| The provider can **cancel** a Pending request, or **remove** an approved request / a single day (from today on; past days stay) | `RemoveRequestAsync`, `RemoveDayAsync` (only their OWN, checked with the signed-in account) |
| **January 1:** last year's days and the requests left without days are deleted | `Services/UnavailableDatesResetService.cs` |

- The bell: the provider sees the answer (*"Your day off request for Nov 20 to Nov 22, 2026 was approved."*) and an
  extra day an administrator gave; administrators see every request that is still Pending (`usp_GetNotifications`).
- "Today" is always the Philippine date (UTC+8), not the server's DateTime.Today.

### The administrator (Admin > Service Providers > View Details > Days Off)
- Every request and day off from today to Dec 31, with its badge; a Pending one has **Approve** and **Reject** (both ask first).
- **Approve** is refused while one of its days has an active booking (a customer may have booked it while it was Pending):
  *"Vincent Valencia has booking #12 (Accepted) on Nov 21, 2026. That booking has to be settled first (or reject the request), ..."*
- Each answer is an ADMIN_ACTIONS row ('Day Off Approved' / 'Day Off Rejected', REASON = the dates) that the request
  points to (`DECIDED_BY_ACTION_ID`), saved in the same transaction. A request is answered only once.
- **Give extra day off** (the emergency path, even for today) still works as in v2.4.

### Live updates (no reload needed)
- A provider sends, cancels or removes a request, or an administrator approves / rejects / gives an extra day:
  `DayOffLiveUpdates` pushes `dayOffChanged { providerId, what }` over the existing SignalR connection
  (`/hubs/bookings`) to every administrator (hub group "admins", from the sign-in role) and to that provider.
- Admin > Service Providers: the yellow **"Day off requests waiting for you"** list and the card badge
  **"Day off request"** appear by themselves; **Review** opens the provider with Approve / Reject. An open provider
  pop-up redraws only its Days Off list (what the admin is typing in "Give extra day off" is kept).
- Provider > Availability: "Pending Admin Approval" turns into "Approved" (or the request disappears when rejected)
  by itself. The bell updates too.
- The message has no personal details: the page asks the server for the fresh list itself, with all the checks.
  If the live connection is down, the pages ask every 30 seconds instead.
- Fixed: the Request Day Off pop-up opened on every visit. Razor always writes `data-*` attributes (`data-open=""`),
  and the script only checked that the attribute existed; now it opens only for `data-open="true"`.

### Database v2.7 ("DayOffRequests")
- New table `PROVIDER_DAY_OFF_REQUESTS` (REQUEST_ID, PROVIDER_ID, REASON, STATUS, REQUESTED_AT, DECIDED_BY_ACTION_ID) with
  CHECKs on STATUS (Pending / Approved / Rejected) and on the decision (none while Pending, required after).
- `PROVIDER_UNAVAILABLE_DATES.REQUEST_ID` (nullable FK, no cascade): the request's days, one row per date (so the v2.5
  one-provider-per-date index still works). CHECK: a day has a request OR an administrator's extra day, never both.
- `fn_IsProviderAvailable`, `usp_CreateBooking` and `usp_GetNotifications` changed (`Data/DayOffRequestsSql.cs`).
- Run `tools\add-day-off-requests.ps1` once (after v2.6). It changes no data: the days off saved so far have no
  request, so they stay days off.

## Days with a booking, and extra days given by an administrator (database v2.4)
| Rule | Where it is enforced |
|---|---|
| A NEW day off cannot be on a day with an active booking (Pending, Accepted, In Progress): *"Nov 20, 2026: you already have booking #12 (Accepted) on this day. A day with an active booking cannot be marked unavailable."* | `ProviderAvailabilityService.RequestDayOffAsync` since v2.7 (serializable transaction); approving is refused too while a booking is on one of the days |
| Emergency (e.g. sick today): an administrator opens the provider (Admin > Service Providers > View Details) > **Days Off** > date + reason > **Give extra day off** | `ProviderManagementService.ApproveExtraDayOffAsync` |
| The approval writes an ADMIN_ACTIONS row ('Extra Day Off Approved', reason "Nov 20, 2026: Sick") and the day off, which points to it through the new column `PROVIDER_UNAVAILABLE_DATES.APPROVED_BY_ACTION_ID` (FOREIGN KEY, no cascade). One save = one transaction | same + `ServisyoDbContext` |
| The provider sees it with "Extra day off given by an administrator" (v2.7: the 5-a-month count it was exempt from is gone) | the page |
| The admin cannot approve a day with an active booking, a day that is already off, a past day, a day after Dec 31, or without a reason (max 200 characters); only for Approved providers; only an Active administrator | `ApproveExtraDayOffAsync` |
| Whether a day was approved is never taken from the browser (looked up in the database) | `ProviderAvailabilityService` |

Database change: run `tools\add-extra-day-off.ps1` once (it builds, tests, creates the migration "ExtraDayOff", checks it
only adds that column + index + foreign key, backs up SERVISYO, and updates it after you type YES).
The design file `docs/sql/DatabaseDesign-v2.sql` is now v2.4 (PART 1 shows the column and its foreign key).

## Only one provider off per day (database v2.5)
| Rule | Where it is enforced |
|---|---|
| A date can be off for only ONE provider (first come, first served), so the other providers stay bookable that day | page (warns as soon as a taken date is picked) + `RequestDayOffAsync` (`TakenByOthersAsync`, v2.7) + the database (UNIQUE index `IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE`) |
| Provider message: *"Nov 14, 2026: Another provider already has this day off. Only one provider can be off per day, so please choose another date."* (the other provider is not named) | `UnavailableDateRules.TakenMessage` |
| Administrators follow it too: *"Angel Diolin already has Nov 14, 2026 off. Only one provider can be off per day, so Vincent Valencia cannot be given that day."* | `ApproveExtraDayOffAsync` |
| Two providers asking for the same date at the same moment: the transaction + unique index let only the first through; the second gets "Nothing was sent: another provider may have just taken one of these days ..." | `RequestDayOffAsync` catch |

Database change: run `tools\add-one-day-off-per-date.ps1` once (after v2.4). It lists dates that more than one provider
already has off; with YES it keeps the FIRST one saved for each date and removes the later ones (after a backup), then
adds the unique index. To choose yourself, answer NO, remove the unwanted day on that provider's Availability page, and
run it again.

## Maximum jobs per day: 1 to 20 (database v2.6)
The number is kept in one place, `ProviderLimits.MaxJobsPerDay` (`Models/Constants.cs`), and the rest of the app uses it.
| Rule | Where it is enforced |
|---|---|
| A provider accepts at most **20** jobs a day (was 50). Message: *"Enter a whole number of jobs from 1 to 20."* | Register as Provider form (`[Range]` + `max="20"`) and `AuthService.RegisterProviderAsync` |
| The same on the Availability page: *"Maximum jobs per day must be between 1 and 20."* | `max="20"` on the field + `ProviderAvailabilityService.SaveAsync` (a rejected save returns 400) |
| The database refuses anything outside 1 to 20, even from SQL typed by hand | CHECK `CK_PROVIDERS_LIMITS`: `[MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] BETWEEN 1 AND 20` |
| Registering through the stored procedure | `usp_RegisterProvider`: THROW 50110 *"Enter a whole number of jobs per day from 1 to 20."* |

Database change: run `tools\set-max-jobs-20.ps1` once (after v2.5). It shows the providers saved with more than 20
(they become 20, otherwise the new CHECK cannot be added), backs up SERVISYO, and after YES applies the migration
`MaxJobsPerDay20` (EF changes the CHECK; the script adds the `Data/MaxJobsSql.cs` steps: lower the old values first,
then the new procedure; Down puts the v2.3 procedure back but cannot restore numbers that were lowered).
Tests: `tests/Servisyo.Tests/MaxJobsPerDayTests.cs`.

## Program.cs
```csharp
builder.Services.AddHostedService<UnavailableDatesResetService>();
```
(already in the zip's Program.cs)

## Tests
`tests/Servisyo.Tests/ProviderAvailabilityTests.cs` (break rules, the 8-hour boundary, the dropdown, the yearly reset),
`DayOffRequestTests.cs` (requests, 7 days, same day, one at a time, approve / reject, which days block bookings, the
model, the SQL and the design file), `ProviderDaysOffIsolationTests.cs` (each provider only their own; one per date),
`ProviderExtraDayOffTests.cs` (bookings and the administrator's extra day).
