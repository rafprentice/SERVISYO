# Sign-up validation (customer + provider)

Checked in three places, so skipping one does not get a bad sign-up through:
1. Browser: jQuery unobtrusive validation (same patterns), `type="tel"` + `pattern`, number boxes refuse e / + / -.
2. Controller: `if (!ModelState.IsValid) return View(...)` (Register, RegisterProvider) — nothing is saved.
3. AuthService: the same rules again (`CheckCommon`), for requests that skip the page.

| Field | Rule | Message |
|---|---|---|
| First / Middle / Last name, City | letters, spaces, . ' - ; at least one letter (accents allowed: Peñaranda) | Please enter a valid name containing letters only. |
| Full Name (customer) | two or more words of the above | Please enter your first and last name using letters only. |
| Phone (optional) | `^\+?[0-9\s-]{7,15}$`; stored without spaces/hyphens | Phone number can only contain digits and valid phone symbols. |
| Password | required, 8+ chars, a letter and a number | Password must be at least 8 characters / contain a letter and a number |
| Confirm password | required, must match | The passwords do not match. |
| Business name | required, must contain a letter (numbers allowed: "A1 Plumbing") | The business name must contain letters. |
| Base fee / Minimum notice / Max jobs | numbers only, 0–1,000,000 / 0–30 / 1–50 | (range messages) |

Not changed: Street Address and Barangay accept numbers ("123 Rizal St.", "Brgy. 12").

## Required fields (all except City)
- Customer: Full Name, Email, Phone, Street Address, Barangay, Password, Confirm Password, Terms. City optional.
- Provider: First, Middle, Last name, Email, Phone, Password, Confirm, Business name, Description, Address,
  Base fee, Minimum notice, Max jobs. City optional.
