# Provider "My Services" - notes

## What it does
- **My Services** (`/provider/services`) lists the signed-in provider's own services: name, Active/Inactive tag,
  description, price (and, beyond the mock-up, duration and category). Active services come first, then in the order they were added.
- **+ Add Service** opens the *Add New Service* dialog. After a successful save the list reloads with a green message.
  If something is wrong the dialog opens again with what you typed and the problem under the box.
- **Deactivate / Activate** switches one service. Inactive cards are dimmed; the button stays clear.
- Same rules as `usp_AddService` in the PDF: only an **approved**, active provider can add services, the category must be
  **active**, and the provider is linked to the category automatically (no duplicate links).

## Where the code is
| Part | File |
|---|---|
| Rules (list, add, activate/deactivate) | `Services/ProviderOfferingsService.cs` |
| Page / dialog form values | `ViewModels/ProviderServicesViewModels.cs` |
| Controller (`provider/services`) | `Controllers/ProviderServicesController.cs` |
| Page + dialog | `Views/ProviderServices/Index.cshtml` |
| Dialog behaviour | `wwwroot/js/provider-services.js` |
| Looks | end of `wwwroot/css/portal.css` (`.sv-svc*`, `.sv-svcform*`) |
| Sidebar link | `Views/Shared/_PortalLayout.cshtml` (My Services is live; menu order now matches the mock-ups) |

## Program.cs - one new line next to the other services
```csharp
builder.Services.AddScoped<IProviderOfferingsService, ProviderOfferingsService>();
```

## Try it by hand
1. You need a provider you can **sign in as** and that is **Approved**. Register one at `/Account/RegisterProvider`,
   sign in as the administrator and approve it (Dashboard > Review Applications). (The `dev.*@servisyo.test` people cannot sign in.)
2. Optional: load the four design services into that provider: edit `@ProviderEmail` in `docs\dev-sample-services-data.sql`, then
   `sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-services-data.sql`
3. Sign in as the provider, open **My Services**. You should see 3 Active cards and "Water Heater Install" Inactive and dimmed.
4. **Deactivate** "Pipe Repair": green message, the tag turns grey, the card moves below the active ones, button says **Activate**. Press **Activate**: it comes back.
5. **+ Add Service**: leave everything empty and press *Add Service*: the browser asks for the required boxes.
   Fill name "Pipe Repair" (already exists) + a category + price 800: the dialog stays open with "You already have a service with this name."
6. Add "Faucet Repair", category Plumbing, price 450, 1 hr: dialog closes, message, new card at the end of the active ones.
7. **Cancel**, the **x**, **Esc**, or a click on the dark area: dialog closes and the boxes are empty next time.
8. Database check:
   ```sql
   SELECT * FROM SERVICES WHERE PROVIDER_ID = (SELECT PROVIDER_ID FROM SERVICE_PROVIDERS p JOIN USERS u ON u.USER_ID=p.USER_ID WHERE u.EMAIL='<your provider email>');
   SELECT * FROM PROVIDER_CATEGORIES;   -- one row per category the provider uses, never twice
   ```
9. Security checks: a customer or administrator opening `/provider/services` is refused/redirected; a provider who is still Pending is sent to the waiting page.

## Left out on purpose
- **Delete a service** - not built: bookings point to their services (BOOKING_SERVICES), so a service is deactivated instead.
- **Deactivating does not touch existing bookings.** Customers just stop seeing the service (customer search is a later stage).
- Two services of one provider cannot share a name (case ignored, inactive ones count too). That is a small extra rule, not in the PDF.
- **Duration** is not in the mock-up either, but booking-conflict checks (Stage 9) need it, so it is collected here instead of guessing 60 minutes for everyone.

## Edit Service (added later)
- Each card has **Edit Service**. It opens the *Edit Service* dialog with the card's **title, category, description and
  price**; the duration is kept as it is. Same checks as Add (name unique per provider, active category, price 0 to
  99,999,999.99 with at most 2 decimals). A category an administrator has switched off since may be **kept**, not newly chosen.
- **Active booking lock:** while the service is part of a booking that is still going on (**Pending, Accepted or
  In Progress**: `ActiveBookingRules`), the button is greyed out and shows the tooltip *"Editing is locked while there are
  active bookings for this service to prevent price tampering."* The server refuses the change too (`UpdateAsync` ->
  `Locked`), so it cannot be bypassed by sending the form by hand. The check and the save run in one **serializable**
  transaction, so a customer cannot book the service in between.
  (The request named the statuses Requested / Quotation / Paid / Scheduled / InProgress; SERVISYO's booking statuses are
  Pending / Accepted / In Progress, which cover the same steps; paid-ness is the separate PAYMENT_STATUS.)
- Finished bookings (Completed, Cancelled, Rejected) keep the price they were booked at (`BOOKING_SERVICES.PRICE_SNAPSHOT`
  and `BOOKINGS.TOTAL_FEE`), so editing never changes them.
- Code: `ProviderOfferingsService.UpdateAsync` / `GetWithRejectedEditAsync`, `ProviderServicesController.Edit`
  (`POST provider/services/{id}/edit`), `EditServiceForm` / `ServiceEditRules` / `EditServiceResult`, the dialog in
  `Views/ProviderServices/Index.cshtml`, `provider-services.js`, tests in `tests/Servisyo.Tests/ProviderServiceEditTests.cs`.
- Try it: edit a service with no bookings (new price shows on the card); then book a service as a customer (Pending) and
  check that its **Edit Service** button is grey and shows the tooltip; accept/complete or cancel the booking and it unlocks.
