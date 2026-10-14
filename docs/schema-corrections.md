# SERVISYO schema corrections (Stage 2)

Changes from the PDF schema, and whether they affect documented requirements.

| # | Change | Reason | Affects requirements? |
|---|--------|--------|-----------------------|
| 1 | New `BOOKING_SERVICES` table; removed `FKSERVICE_ID` and `FKCATEGORY_ID` from `BOOKINGS` | Rules say a booking has one or more services. Category is derived from the services | Fixes a gap. Needs your approval (category no longer stored on BOOKINGS) |
| 2 | `SERVICES.DURATION_MINUTES`, `BOOKINGS.TOTAL_DURATION_MINUTES`, computed `END_TIME` | Real overlap detection | Enables the "no conflicting bookings" rule |
| 3 | `BOOKINGS.BASE_FEE` renamed `TOTAL_FEE` | It is a snapshot of the sum of service prices | No |
| 4 | `BOOKINGS.ROW_VERSION` | Optimistic concurrency | No |
| 5 | Unique indexes: provider+category, provider+day, provider+unavailable date, category name | Prevent duplicates | No |
| 6 | Filtered unique index on active bookings (provider, date, time) | Backstop against double booking | No |
| 7 | CHECK: END_TIME > START_TIME, break end > break start, price >= 0, duration > 0, limits | Data integrity | No |
| 8 | `DATETIME2` + `SYSUTCDATETIME()` defaults | Store UTC. Convert to Asia/Manila in the app | No |
| 9 | Names are NVARCHAR | Supports names like Niña | No |
| 10 | Delete behavior Restrict everywhere, except breaks cascade with their schedule | Prevent accidental data loss | No |
| 11 | C# entity `Provider` maps to table `SERVICE_PROVIDERS`; columns are UPPER_SNAKE_CASE (e.g. `USER_ID` instead of `FKUSER_ID`) | Avoid a name clash; consistent naming | Cosmetic. Table names match the PDF |

Add to .gitignore: `appsettings.*.local.json`
