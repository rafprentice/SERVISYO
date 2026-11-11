# Provider Availability - break and unavailable-date rules

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

## Unavailable dates
| Rule | Where it is enforced |
|---|---|
| From **tomorrow** up to **December 31 of this year** (Philippine time); 2027, 2028, 2030 ... are refused | date picker `min`/`max` + server (`ValidateDates`) |
| **No same-day requests:** today cannot be added. The page shows *"Same-day off requests are not allowed. Unavailable dates must be marked at least 1 day in advance."* under the box and blocks Save; the server refuses it with *"Same-day unavailable date selection is not permitted."* and answers **400 Bad Request** (the page with the problems) | `provider-availability.js` + `ValidateDates` + `ProviderAvailabilityController.Save` |
| A day off for today that was saved EARLIER (when it was still tomorrow) stays; only new days are checked. "Saved earlier" is looked up in the database (`AlreadySaved`, `[BindNever]`) | `GetAsync` / `ValidateDates` |
| An administrator can still approve today as an extra day off (the emergency path, e.g. sick this morning) | `ApproveExtraDayOffAsync` |
| At most **5 per calendar month**: *"You can only set up to 5 unavailable days per month."* Days of this month that already passed count too | page (message as soon as the 6th day is picked; Save is blocked) + server |
| **January 1:** last year's dates are deleted, so every year starts with an empty list | `Services/UnavailableDatesResetService.cs` |

- The reset runs when the website starts and then every hour; it only deletes dates **before January 1 of the current
  year**, so running it again changes nothing. A website that was asleep on January 1 resets when it starts again.
  Old unavailable dates are not used by anything else (a booking only checks its own day), so no history is lost.
- A date saved earlier in a later year (for example Nov 11, 2030) is shown in red with "Remove this date before saving";
  the schedule cannot be saved until it is removed.
- In December, January dates cannot be added until January 1 (that is what "until Dec 31 of this year" means).
- "Today" is always the Philippine date (UTC+8), not the server's DateTime.Today: the hosting server may run on another
  time zone, where "today" changes at a different hour.

## Days with a booking, and extra days approved by an administrator (database v2.4)
| Rule | Where it is enforced |
|---|---|
| A NEW day off cannot be on a day with an active booking (Pending, Accepted, In Progress): *"Nov 20, 2026: you already have booking #12 (Accepted) on this day. A day with an active booking cannot be marked unavailable."* | `ProviderAvailabilityService.SaveAsync` (serializable transaction, so a customer cannot book that day in between) |
| Days off saved before this rule stay, even if a booking is on them (only new days are checked) | same |
| Emergency beyond the 5: an administrator opens the provider (Admin > Service Providers > View Details) > **Days Off** > date + reason > **Approve extra day off** | `ProviderManagementService.ApproveExtraDayOffAsync` |
| The approval writes an ADMIN_ACTIONS row ('Extra Day Off Approved', reason "Nov 20, 2026: Sick") and the day off, which points to it through the new column `PROVIDER_UNAVAILABLE_DATES.APPROVED_BY_ACTION_ID` (FOREIGN KEY, no cascade). One save = one transaction | same + `ServisyoDbContext` |
| Such a day does NOT count toward the provider's 5; the provider sees it with "Extra day approved by an administrator" | `ValidateDates`, `CountPastDaysThisMonthAsync`, the page |
| The admin cannot approve a day with an active booking, a day that is already off, a past day, a day after Dec 31, or without a reason (max 200 characters); only for Approved providers; only an Active administrator | `ApproveExtraDayOffAsync` |
| Whether a day was approved is never taken from the browser (`[BindNever]`, looked up in the database) | `UnavailableDateForm.ApprovedByAdmin` |

Database change: run `tools\add-extra-day-off.ps1` once (it builds, tests, creates the migration "ExtraDayOff", checks it
only adds that column + index + foreign key, backs up SERVISYO, and updates it after you type YES).
The design file `docs/sql/DatabaseDesign-v2.sql` is now v2.4 (PART 1 shows the column and its foreign key).

## Only one provider off per day (database v2.5)
| Rule | Where it is enforced |
|---|---|
| A date can be off for only ONE provider (first come, first served), so the other providers stay bookable that day | page (warns as soon as a taken date is picked) + `SaveAsync` (`TakenByOthersAsync`) + the database (UNIQUE index `IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE`) |
| Provider message: *"Nov 14, 2026: Another provider already has this day off. Only one provider can be off per day, so please choose another date."* (the other provider is not named) | `UnavailableDateRules.TakenMessage` |
| Administrators follow it too: *"Angel Diolin already has Nov 14, 2026 off. Only one provider can be off per day, so Vincent Valencia cannot be given that day."* | `ApproveExtraDayOffAsync` |
| A provider's own saved day is never "taken" by themselves (re-saving the page works) | only NEW days are checked |
| Two providers saving the same new date at the same moment: the transaction + unique index let only the first through; the second gets "Nothing was saved: another provider may have just taken one of your new days off ..." | `SaveAsync` catch |

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
`tests/Servisyo.Tests/ProviderAvailabilityTests.cs` (break rules, the 8-hour boundary, the dropdown, 5 a month,
Dec 31, the past days of this month, and the yearly reset).
