# Customer > My Profile - notes

Page: `/customer/profile` (sidebar "My Profile"). Customers only.
Files: `Services/CustomerProfileService.cs`, `Controllers/CustomerProfileController.cs`,
`ViewModels/CustomerProfileViewModels.cs`, `Views/CustomerProfile/Index.cshtml`, `wwwroot/js/customer-profile.js`,
`.sv-cp*` rules at the end of `portal.css`, `tests/Servisyo.Tests/CustomerProfileTests.cs`.
It reuses `ProfileRules` (from the provider profile page), so install `provider_profile.zip` first.

## What is on the page
| Card | What it does |
|---|---|
| Summary (left) | Initials avatar, name, email, "Active Account", Member since (account creation date, Philippine time), Total bookings (all of YOUR bookings) |
| Personal Information | Full name, email, phone, city. "Edit" unlocks the boxes; Save / Cancel. Same rules as the provider profile (name needs first + last, email unique, phone 7-15 digits, city max 50) |
| Saved Address | The customer's one address (`CUSTOMERS.ADDRESS`). Add / Edit / remove (trash asks first). Blank = removed |
| Security | Change Password: current password, new password, confirm. Account Status row (always "Active": only Active accounts can open the page) |

Without JavaScript every form is simply open and works as a normal form; with JavaScript the cards start closed.

## Rules (and where each is enforced)
| Rule | Enforced in |
|---|---|
| Only an Active customer account can open or save anything | `[Authorize(Roles = Customer)]` + `FindAsync` on every call (suspended / deactivated accounts are signed out) |
| The customer is the signed-in user, never a form value | Controller reads `ClaimTypes.NameIdentifier`; the forms have no id fields; every query starts from that user |
| Email is also the sign-in name, so it stays unique | Check in `SaveAsync` + the database unique index (race caught as `DbUpdateException`) |
| Nothing is saved when any field is wrong | `SaveAsync` collects all errors first |
| Name / email in the sign-in cookie follow a profile change | `RefreshSignInAsync` (user id, role and "remember me" unchanged) |
| Password: current one must be right; new = 8-100 chars with a letter and a number (same as registration), different from the old, confirmed | `ChangePasswordAsync` + `PasswordRules` |
| Passwords are stored only as an ASP.NET `PasswordHasher` hash, never logged, never sent back to the page | `ChangePasswordAsync`, controller |

## Deliberately NOT built (and why)
- **Several saved addresses (Home / Office / Set default) and Payment Methods (cards).** The documented database has ONE
  address per customer and no table for addresses or cards, so the extra rows in the mockup would need new tables.
  Saving card numbers is also a security/PCI risk: if payments are added later they should go through a payment
  provider that stores the card and gives us only a token, never the number. Needs your decision.
- **Profile photo upload** (the pencil button is disabled, same as the provider page): needs file storage outside
  `wwwroot` with type/size limits - same open question as before.
- **"Last changed 45 days ago"** for the password: there is no column for it. The row shows the password rule instead.
- The mockup's "Switch portal" buttons and settings gear: a prototype feature; each role signs in to its own portal.

## Known limits
- Changing the password does not sign out other browsers (cookie sign-in has no security stamp yet).
- Changing the email does not ask for the password again (same as the provider page).
- There is no limit on wrong-current-password attempts. Add rate limiting before going public.

## Install
1. `Expand-Archive -Path "$env:USERPROFILE\Downloads\customer_profile.zip" -DestinationPath . -Force`
2. In `Program.cs` next to the other `AddScoped` lines add:
   `builder.Services.AddScoped<ICustomerProfileService, CustomerProfileService>();`
   (`IPasswordHasher<User>` is already registered for sign-in.)
3. `dotnet build` then `dotnet test` then `dotnet run`; sign in as a customer and open My Profile.
