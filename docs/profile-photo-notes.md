# Profile photo + name lock

## Profile photo (customers and providers)
- The pencil on the avatar opens the file picker; choosing a photo uploads it at once (`POST /account/photo`,
  `ProfilePhotoController` -> `ProfilePhotoService`). "Remove photo" appears under the avatar once there is one.
- Rules: Active customer/provider accounts only; JPG, PNG or WebP (decided by the file's first bytes, not its name);
  max 2 MB; saved as `wwwroot/uploads/avatars/{userId}-{random}.{ext}`; the old file is deleted.
- Stored in the existing `CUSTOMERS.ProfilePhoto` / `SERVICE_PROVIDERS.ProfilePhoto` columns (no database change).
- The sidebar shows the photo on every portal page; the provider's Profile Strength counts it (reaches 100% when the rest is complete).
- `app.UseStaticFiles()` is needed so photos uploaded after the build are served; `wwwroot/uploads/` is git-ignored.
- Online (Monster): photos live in the site's `wwwroot/uploads/avatars`; uploading a new publish zip does not delete them.

## Name lock during active bookings
- Active = Pending (Requested), Accepted (Quotation / Paid / Scheduled) or In Progress.
- Customer: Full Name is read-only on the page and refused by the server while one of their bookings is active.
- Provider: Full Name and Business Name, same rule (bookings with them as the provider).
- Other fields (email, phone, city, address, bio) can still be saved. Message: "Name editing is locked while you have active bookings."
