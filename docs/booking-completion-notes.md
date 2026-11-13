# Completing a job with proof (database v2.11, "BookingCompletionProof")

A provider can no longer mark a job completed with one click. "Mark Completed" opens **Confirm Service Completion**,
and the job is only completed with a **proof photo** and a **completion message**.

## The provider (Provider → Booking Requests → an In Progress booking → Mark Completed)
- **Proof Photo \*** — "Choose Photo" opens the device's file manager (`accept="image/png, image/jpeg, image/webp"`).
  A preview appears at once. JPG, PNG or WebP, up to 5 MB.
- **Completion Message \*** — up to 1,000 characters (a counter shows how many).
- Sending without a message or without a photo is blocked, with a red message under the field:
  - *Completion message is required. Please input a message before submitting.*
  - *Please select a proof photo from your file manager.*
- Cancel, ✕, Esc or a click outside closes it; nothing is saved. While it is open, a live update waits.

## The server decides (`ProviderBookingsService.CompleteAsync`, `POST /provider/bookings/{id}/complete`)
- `multipart/form-data`: `completionMessage`, `proofPhoto`, the anti-forgery token, and the tab / page to return to.
- The provider comes from the sign-in cookie, never from the form. They must be **Active and Approved**, and the
  booking must be **theirs** and **In Progress**. Someone else's booking answers like a missing one.
- **400 Bad Request** when the message is empty or whitespace, or longer than 1,000 characters. The same goes for
  a photo that is missing, empty, over 5 MB, not named `.jpg / .jpeg / .png / .webp`, or whose first bytes are not
  really a JPG, PNG or WebP. Both problems are reported at once, under their fields.
- The photo is saved as `wwwroot/uploads/completions/{bookingId}_{guid}{ext}`. The server makes the name; the
  extension comes from the file's bytes. Then, in one transaction, BOOKINGS gets `COMPLETION_MESSAGE` (trimmed),
  `COMPLETION_PHOTO_URL`, `COMPLETED_AT` (UTC) and `STATUS = 'Completed'`. If that save fails (for example the
  customer changed the booking at the same moment), the photo file is deleted again.
- The old one-click form can no longer complete a job: it answers *"To mark a booking completed, add a completion
  message and a proof photo."*

## The database
| Column | Type | Meaning |
|---|---|---|
| `COMPLETION_MESSAGE` | NVARCHAR(1000) NULL | what the provider did |
| `COMPLETION_PHOTO_URL` | NVARCHAR(500) NULL | `/uploads/completions/31_….jpg` (the file itself is not in the database) |
| `COMPLETED_AT` | DATETIME2 NULL | when it was completed with proof (UTC) |

- All three are NULL until the job is completed with proof. Jobs completed before v2.11 have no proof.
- **CHECK `CK_BOOKINGS_COMPLETION`**: the proof is all or nothing, only on a Completed booking, with a message that is
  not blank. Half a proof, or proof on a job that is not completed, is refused by SQL Server itself.
- Stored, not calculated: `COMPLETED_AT` is not `UPDATED_AT` (which changes again, e.g. when cash is confirmed),
  and System Logs is an audit trail, not the source of truth.

## Who sees the proof
- **Customer → My Bookings:** the completed card shows the photo (it opens full size), the message and the time.
- **Admin → Bookings → View:** a *Completion Proof* section. Older jobs say *"No proof recorded: this job was
  completed before a photo and a message were required."*
- **Provider:** their own completed card shows it too.
- A photo is only shown if its address is a plain file inside `/uploads/completions/`.

## System Logs
The audit trigger `trg_Bookings_Audit` (v2.11) writes ONE line, in the standard format:
*Service Provider Marco Reyes updated Booking #31: status 'In Progress' -> 'Completed' (proof photo and completion
message saved).*
The message itself is not copied into System Logs (one fact, one place: it is in BOOKINGS). A later change of the
proof would be logged old -> new like any other field.

## Install (laptop)
1. Extract the zip in the repo root.
2. `powershell -ExecutionPolicy Bypass -File tools\add-booking-completion.ps1`. It builds and runs the tests, makes
   and checks the migration, makes a backup, asks for YES, then checks the result. It also runs a live test
   inside a transaction that is always rolled back.
3. Optional: run `docs\sql\DatabaseDesign-v2.sql` on a **copy** of the database for the full self-test (test 37).

## Good to know
- Proof photos are in `wwwroot/uploads/completions`. Like the profile photos, they are outside Git and outside the
  deploy zip, so a deploy never overwrites them. Back up that folder on Monster together with the database.
- Anyone who has a photo's address can open it. The address contains a random GUID, so it cannot be guessed.
- In SSMS, `usp_UpdateBookingStatus` can still complete a job without proof (the CHECK allows "no proof at all",
  for older jobs). The website always requires it.
