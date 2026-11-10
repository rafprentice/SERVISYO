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
| From today up to **December 31 of this year** (Philippine time); 2027, 2028, 2030 ... are refused | date picker `min`/`max` + server (`ValidateDates`) |
| At most **5 per calendar month**: *"You can only set up to 5 unavailable days per month."* Days of this month that already passed count too | page (message as soon as the 6th day is picked; Save is blocked) + server |
| **January 1:** last year's dates are deleted, so every year starts with an empty list | `Services/UnavailableDatesResetService.cs` |

- The reset runs when the website starts and then every hour; it only deletes dates **before January 1 of the current
  year**, so running it again changes nothing. A website that was asleep on January 1 resets when it starts again.
  Old unavailable dates are not used by anything else (a booking only checks its own day), so no history is lost.
- A date saved earlier in a later year (for example Nov 11, 2030) is shown in red with "Remove this date before saving";
  the schedule cannot be saved until it is removed.
- In December, January dates cannot be added until January 1 (that is what "until Dec 31 of this year" means).

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

## Program.cs
```csharp
builder.Services.AddHostedService<UnavailableDatesResetService>();
```
(already in the zip's Program.cs)

## Tests
`tests/Servisyo.Tests/ProviderAvailabilityTests.cs` (break rules, the 8-hour boundary, the dropdown, 5 a month,
Dec 31, the past days of this month, and the yearly reset).
