# Admin > Providers > View Details

## What it does
"View Details" opens a pop-up (the same dialog the application review uses). It shows: name, business, category and city,
status badges, email, phone, member-since date, completed jobs, rating, address, bio, and the active services with fee and duration.
Buttons depend on the account's CURRENT status:
- Active: Suspend Provider, Deactivate Provider
- Suspended: Reactivate Account, Deactivate Provider
- Deactivated: Restore Account
Each button asks "are you sure?"; an optional reason (max 255) is saved in the admin activity log (ADMIN_ACTIONS, e.g. "Account Suspended").
Without JavaScript the same content opens as a normal page (`/admin/providers/{id}`).

## Rules (enforced on the server)
- Active administrators only (checked in the database; the admin comes from the sign-in cookie).
- The target must be a provider account; a customer or admin can never be changed from here.
- Allowed changes come from the single `AccountTransitions` table (same as Admin > Users).
- The status change and the log entry are saved in one SaveChanges.
- A stale pop-up (status changed by someone else) gets "...so that action isn't available".

## Differences from the mock-up text
- Reactivate/Restore buttons were added: without them a suspended provider could never be unsuspended.
- Prices come from the real services (name, category, price, duration); there is no per-hour "rate".
- Member Since is the account creation date (Cebu time).

## Known limit
Suspending does not cancel bookings that already exist; it only stops new activity (sign-in is blocked for suspended/deactivated accounts).

## Install
`Expand-Archive -Path "$env:USERPROFILE\Downloads\provider_details.zip" -DestinationPath . -Force`, then `dotnet build`, `dotnet test`, `dotnet run`.
No new service registration is needed (IProviderManagementService already exists).
