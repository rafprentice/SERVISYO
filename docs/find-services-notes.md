# Customer > Find Services — implementation notes

Page: **Find Services** in the customer sidebar → `/customer/find-services`.
(The two "Find a Service" buttons on the customer dashboard now open this page too.)

## What it does

| Part | Behaviour |
|---|---|
| Search box | Matches the provider's **name**, **business name** or **city** (not case-sensitive, extra spaces ignored, max 100 characters). Press Enter to search. |
| Sort list | Highest Rating (default) · Most Reviews · Lowest Price · Name (A–Z). Changing it searches straight away (needs JavaScript; without it, press Enter). |
| Category chips | "All" plus one chip per active category that at least one listed provider offers, each with how many providers. They follow the search text, keep the sort order, and the chosen chip is highlighted. |
| "N providers found" | Count after search + category. "Clear filters" appears when something is filtered. |
| Provider card | Avatar initials, person, business, city, category badge, availability badge, stars + rating + "(N reviews)" (or "No reviews yet"), short description (2 lines), up to 3 service tags + "+N more", price with its label, **Book this Provider**. |
| Paging | 12 providers per page, Previous / Next, page numbers outside the range are corrected. |
| Empty states | "No providers match your search" (with Clear filters) or "No providers are available yet". |

## Rules and where each is enforced

| Rule | Where |
|---|---|
| Only a signed-in **Customer** opens the page | `[Authorize(Roles = "Customer")]` on the controller |
| Suspended / deactivated / removed customers are signed out | `FindServicesService.GetAsync` returns `Allowed = false` → controller signs out |
| Who is asking comes from the sign-in cookie | The address only carries `q`, `category`, `sort`, `page` — never a user id |
| Only **Approved** providers with an **Active** account and **at least one active service** are listed | `WhereEligible()` (same rule as the home page and dashboard) + `Services.Any(IsActive)` |
| Pending, rejected, suspended and deactivated providers can never be found — not even by typing their exact name | Same base query is used for the list, the counts and the search |
| Inactive categories (switched off by the administrator) get no chip and cannot be filtered on | Chips are built from active categories; an unknown / inactive `category` number is ignored |
| Unknown sort values fall back to "Highest Rating" | `FindServicesSort.Find` |
| Search text is data, never SQL | EF Core parameters; `%`, `_`, `[` are plain characters (tested) |
| The page changes nothing in the database | Read-only (`AsNoTracking`, GET only) — tested |
| Stable paging | Every sort ends with the provider id, so ties never repeat or skip a provider between pages |

## Files

New
- `Controllers/FindServicesController.cs`, `Services/FindServicesService.cs`, `ViewModels/FindServicesViewModels.cs`
- `Views/FindServices/Index.cshtml`, `wwwroot/js/find-services.js`
- `tests/Servisyo.Tests/FindServicesTests.cs`, `docs/find-services-notes.md`

Changed
- `Views/Shared/_PortalLayout.cshtml` — customer **Find Services** link is live.
- `Views/Dashboard/Customer.cshtml` — the two "Find a Service" buttons go to this page (they went to the home page before).
- `wwwroot/css/portal.css` — `.sv-fs*` styles added at the end.

## One line to add (Program.cs)

```csharp
builder.Services.AddScoped<IFindServicesService, FindServicesService>();
```

## Choices I made where the design is silent — please check these

1. **I only had the screenshot.** The text file attached to this page was the *Provider Profile* spec, so every behaviour below is my reading of the picture. If you have the real text spec for this page, send it and I will adjust.
2. **Sorting:** the picture shows "Sort: Highest Rating". The other three options (Most Reviews, Lowest Price, Name A–Z) are my choice. "Highest Rating" = rating, then number of reviews, then business name. New providers with no reviews come last.
3. **Search covers person, business and city only** — not service names or descriptions. Easy to add (one more `||` line) if you want "drain" to find plumbers.
4. **"Book this Provider" is disabled** (tooltip: "Booking opens in an upcoming update"). The customer-side booking form is a later stage; a button that did nothing would be worse than an honest disabled one.
5. **Price:** shows the provider's **Base Fee** when one is set; otherwise **"Starting at"** the cheapest *active* service. "Lowest Price" sorts by that same number.
6. **Availability badge is a hint**, worked out the same way as the dashboard's recommended providers: weekly schedule + unavailable dates + minimum notice, looking 7 days ahead. It does **not** look at existing bookings, so it is not a guarantee — the booking stage does the strict check. Badge text: "Available Today", "Next: Tomorrow", "Next: This Week"; nothing when no working day is found.
7. **Category badge:** when a chip is chosen the badge shows that category; with "All" it shows the provider's first category alphabetically.
8. **Category chips with zero providers are hidden** (otherwise the row fills with "(0)" chips).
9. The page is a centred column about 680 px wide, two cards per row (one on phones), like the design.
10. The header's **Customer / Admin / Home** switch buttons are not built — same as on the other pages.

## Not covered by automated tests

- Real SQL Server translation of the queries (the tests use the in-memory database). The queries use only standard EF Core operators (`Contains`, `GroupBy`, `Min`, `OrderBy`), but please run the manual checklist once against LocalDB.
- The Razor view, CSS and JavaScript were checked by reading and syntax only — never opened in a browser.

## Manual test checklist

1. Sign in as a customer → **Find Services**. Approved providers appear; pending / suspended ones do not.
2. Type part of a name, a business name and a city (try CAPITALS and extra spaces) → matching providers; the count updates.
3. Type `zzz` → "No providers match your search" + Clear filters. Click it → full list again.
4. Click a category chip → only providers offering it; the badge on every card names the category; "All" still shows the full count.
5. Search `marco`, then click a chip → the search text stays in the box and in the address.
6. Change **Sort** → page reloads in the new order; the choice is kept when you click a chip or page.
7. With 13+ providers, **Next** shows page 2; edit the address to `?page=999` → last page; `?page=abc` → page 1.
8. Edit the address to `?category=99999` or `?sort=hack` → ignored, normal list.
9. In **Admin > Categories** switch a category off → its chip disappears. In **Admin > Providers** suspend a provider → it disappears from the list.
10. Sign out and open `/customer/find-services` → sign-in page. Sign in as a provider and open it → access denied.
11. Press the browser Back button after a search → the boxes still show what you typed.
