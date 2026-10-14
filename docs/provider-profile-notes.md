# Provider > My Profile — implementation notes

Page: **My Profile** in the provider sidebar → `/provider/profile`.

## What it does

| Part | Behaviour |
|---|---|
| Header card | Initials avatar, name, "Business · City", and the **Profile Strength** bar with the hint ("Add a profile photo to reach 100%"). Uses the *same six checks as the dashboard card* (service, availability, bio, photo, address + city, phone), so the two always agree. |
| Business Information form | Full Name, Business Name, Phone Number, Email Address, City / Location, Address (optional), About / Bio, **Save Changes**. Everything is checked first; if any box is wrong **nothing** is saved and the form comes back with what you typed plus a message under each problem. |
| Services Offered | Pills for the provider's own **active** services (in the order they were added). Empty → link to My Services. |
| Reviews | The latest 3 **visible** reviews of this provider (removed/flagged ones are hidden): stars, score, comment, "Customer name · date". Empty → "No reviews yet…". |
| After saving | Redirect back with a green "Your profile was updated." A refresh never sends the form twice. |

## Rules and where each is enforced

| Rule | Where |
|---|---|
| Only a signed-in Service Provider reaches the page | `[Authorize(Roles = "Service Provider")]` |
| Only an **Active + Approved** provider can open or save (suspended/deactivated are signed out, pending go to the pending page, rejected to the dashboard message) | `ProviderProfileService.AccessOf` / `Denied()` in the controller |
| A provider can only edit **their own** profile | The form has no id at all; the provider and its user account are found from the sign-in cookie |
| Approval status, rating, review count, notice days, jobs/day, role, account status can **not** be changed here | They are not on the form and the service never writes them (tested) |
| Email is the sign-in name → must be valid, lower-cased, ≤ 155 chars and **unique** | Checked before saving (case-insensitive); the unique index in the database is the backstop, and a clash at the last moment shows the same message |
| Name ≤ 155, business ≤ 155, phone ≤ 15 characters, city ≤ 50, address ≤ 255, bio ≤ 255 | Same limits as the database columns and the registration forms |
| CSRF | `[ValidateAntiForgeryToken]` + the form tag helper |

## Files

New
- `Controllers/ProviderProfileController.cs`, `Services/ProviderProfileService.cs`, `ViewModels/ProviderProfileViewModels.cs`
- `Views/ProviderProfile/Index.cshtml`, `wwwroot/js/provider-profile.js`
- `tests/Servisyo.Tests/ProviderProfileTests.cs`, `docs/provider-profile-notes.md`

Changed
- `Views/Shared/_PortalLayout.cshtml` — provider **My Profile** link is live.
- `Views/Dashboard/Provider.cshtml` — "Complete Profile" is now a link to this page (it was a disabled button). (Also contains the Booking Requests links from the previous stage.)
- `wwwroot/css/portal.css` — `.sv-pf*` styles added at the end.

## One line to add (Program.cs)

```csharp
builder.Services.AddScoped<IProviderProfileService, ProviderProfileService>();
```

## Choices I made where the design/PDF is silent — please check these

1. **Full Name is one box, the database has first / middle / last.** Same rule as the customer registration page: *first word = first name, everything after = last name* ("Juan Dela Cruz" → Juan | Dela Cruz). The name is only re-split **if you actually change it**; if you leave it alone first/middle/last are never rewritten, and a stored middle name is never touched. Weak spot: a first name with two words ("Maria Luisa Santos") becomes Maria | Luisa Santos. If that matters, I can switch the box to separate First / Last fields.
2. **Phone is stored without spaces** (`+639175551234`), because `USERS.PHONE_NUMBER` holds only 15 characters and the design's "+63 917 555 1234" has 16. You can type it with spaces; it is cleaned on save. Valid = optional `+` and 7–14 digits (or 7–15 digits without `+`). A number saved earlier in another style is left alone until you edit it.
3. **Email is stored lower-case** (like registration). Changing it changes the **sign-in name**. There is no email-verification step in the project yet, so the change takes effect immediately. After a save the sign-in cookie is re-issued so the name/email shown on the public pages are up to date (the "remember me" setting is kept).
4. **I added an optional "Address" box** (not in the design). Registration collects it, `SERVICE_PROVIDERS.ADDRESS` exists, and the dashboard's Profile Strength needs *address + city* — without the box a provider with no address could never reach 100%.
5. **The profile photo is not built.** The pencil on the avatar is shown but disabled ("coming in a later update"). Uploading needs a decision on where files are stored and how they are served, so I left it for its own stage. Until then Profile Strength tops out at **83%** (5 of 6) — the design's "85%" is sample data; the real number is calculated.
6. **Services Offered** shows your active services (the design's pills — "Faucet Replacement", "Emergency Plumbing" — are just sample text).
7. **Reviews** shows up to 3, with the reviewer's full name as in the design (the dashboard shows "Elena C."). Only visible reviews count.
8. The page is a centred column about 640 px wide, like the design.

## Not covered by automated tests

- Re-issuing the sign-in cookie, and the "email taken in the last millisecond" database path — they need a real browser/SQL Server.
- The Razor view and the JavaScript were checked by reading/syntax only.

## Manual test checklist

1. Sign in as an approved provider → **My Profile**. The boxes show the saved values; strength matches the dashboard.
2. Change the business name and city, **Save Changes** → green message; the header card and the sidebar subtitle show the new business name.
3. Change the name to one word ("Marco") → red message under Full Name, nothing saved. Change it to "Marco Antonio Reyes" → saved; sidebar and the public pages show the new name straight away.
4. Type `abc` as the phone, then `+63 917 555 1234` → first refused, then saved (stored without spaces).
5. Use another account's email → "already used by another account". Use a brand-new email → saved; sign out and sign in with the new email.
6. Make the bio 256 characters (paste text) → the browser stops at 255; if it gets through the server refuses it.
7. Double-click Save Changes → only one request is sent.
8. Open the page as a customer or a signed-out user → refused / sent to sign in. Open `/provider/profile` as a pending provider → pending page.
