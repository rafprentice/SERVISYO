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
| Personal Information | Full name, email, phone, city. "Edit" unlocks the boxes; Save / Cancel. Name needs first + last, email unique, phone REQUIRED (09XXXXXXXXX or +639XXXXXXXXX, at most 13 characters, saved as 09XXXXXXXXX) |
| Saved Address | The customer's one address (`CUSTOMERS.ADDRESS`). "Edit Address" (or "+ Add Address" for an older account with none) opens a dropdown of the 5 Cebu City addresses. There is NO remove button: a saved address can be changed, never removed |
| Security | Change Password: current password, new password, confirm. Account Status row (always "Active": only Active accounts can open the page) |

Without JavaScript every form is simply open and works as a normal form; with JavaScript the cards start closed.

## Rules (and where each is enforced)
| Rule | Enforced in |
|---|---|
| Only an Active customer account can open or save anything | `[Authorize(Roles = Customer)]` + `FindAsync` on every call (suspended / deactivated accounts are signed out) |
| The customer is the signed-in user, never a form value | Controller reads `ClaimTypes.NameIdentifier`; the forms have no id fields; every query starts from that user |
| Email is also the sign-in name, so it stays unique | Check in `SaveAsync` + the database unique index (race caught as `DbUpdateException`) |
| Nothing is saved when any field is wrong | `SaveAsync` collects all errors first |
| The phone number is required (the service provider must be able to contact the customer): blank is refused, so it can be changed but never removed; only 09XXXXXXXXX or +639XXXXXXXXX | `SaveAsync` (`InputRules.PhoneRequiredMessage`, `InputRules.IsPhone`) + `required maxlength=13 pattern` on the box |
| Name / email in the sign-in cookie follow a profile change | `RefreshSignInAsync` (user id, role and "remember me" unchanged) |
| Password: current one must be right; new = 8-100 chars with a letter and a number (same as registration), different from the old, confirmed | `ChangePasswordAsync` + `PasswordRules` |
| Passwords are stored only as an ASP.NET `PasswordHasher` hash, never logged, never sent back to the page | `ChangePasswordAsync`, controller |
| The saved address is one of the 5 Cebu City options (`CebuLocations`), exact text only | `SaveAddressAsync` (`CebuLocations.Find`); the page offers only those options |
| A saved address can be edited but never removed (blank = refused: "Saved addresses cannot be removed once set. Please edit your address instead.") | `SaveAddressAsync` + `SavedAddressRules`; no remove button; `DELETE /api/profile/address` always answers 400 |

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

## Saved address API (added later)
`Controllers/CustomerAddressApiController.cs`, tests in `tests/Servisyo.Tests/CustomerAddressApiTests.cs`.
With JavaScript the Saved Address card saves through this API (no page reload); without JavaScript the same form posts
to `POST /customer/profile/address`. Both call `CustomerProfileService.SaveAddressAsync`, so the rules are in one place.

| Request | Answer |
|---|---|
| `PUT /api/profile/address` with `{ "address": "Salinas Drive, Brgy. Lahug, Cebu City" }` and the `RequestVerificationToken` header | 200 `{ message, address, streetAddress, barangay, city }` |
| same, with a blank address or one that is not in the list | 400 `{ message }`, nothing changes |
| `DELETE /api/profile/address` | always 400 `{ "message": "Saved addresses cannot be removed once set. Please edit your address instead." }` |
| not signed in / not a customer / suspended or deactivated | 401 / 403 (scripts get a status code, not the login page) |

`streetAddress`, `barangay` and `city` are worked out from the chosen option; the database keeps ONE column
(`CUSTOMERS.ADDRESS`) with the full text (one fact, one place). Older accounts that typed an address before the
dropdown existed keep it until they pick one of the 5 options.

## Show / hide password (Security)
- Current Password, New Password and Confirm New Password each have an eye button inside the right edge of the box
  (`Views/Shared/_PasswordToggle.cshtml`, the same one as the sign-in, registration and reset pages).
- Eye = hidden; eye with a line = visible. Each box toggles on its own. The buttons are `type="button"` (they never
  send the form), `aria-label="Toggle password visibility"`, and `aria-pressed` says whether it is shown.
- Update Password and Cancel put every box back to hidden (`wwwroot/js/password-toggle.js`, loaded by the portal and
  sign-in layouts; it replaces `auth.js`). Edge's own built-in eye is hidden where ours is, so there are never two.
- Providers and administrators have no Change Password form yet; any future one gets the eye by wrapping the box in
  `<div class="sv-pwfield">` and adding `<partial name="_PasswordToggle" model="@("the-box-id")" />`.

## The profile lock (Nov 2026)
While a customer has a booking that is **not finished** (any status except Completed, Cancelled and Rejected: a
request, a quotation waiting for payment, a paid/scheduled job, a job in progress), their **personal information**
(name, email, phone) and **saved address** cannot be changed, so the provider keeps the details they were booked with.
- One definition: `Data/CustomerProfileLock.cs` (`ActiveForCustomer`, `CustomerHasActiveBookingAsync`,
  `CountCustomerActiveBookingsAsync`, read with AsNoTracking).
- My Profile shows the amber notice *"Profile editing is currently locked ..."* with the number of active bookings; the
  **Edit** and **Edit Address / + Add Address** buttons are disabled (faded, not-allowed cursor, tooltip), the boxes are
  grey and the address form is not on the page. **Change Password** stays open.
- The server checks again on every save (`CustomerProfileService.SaveAsync` / `SaveAddressAsync`): nothing is written
  and the answer is **400** with *"Profile information and saved address cannot be modified while you have an active
  booking."* - the form posts show the page again with that message, `PUT /api/profile/address` returns it as JSON.
- When the last booking is completed, cancelled or rejected, the page is editable again on the next visit (the
  existing phone and Cebu City address rules apply as before).
- Not locked: the password and the profile photo.
