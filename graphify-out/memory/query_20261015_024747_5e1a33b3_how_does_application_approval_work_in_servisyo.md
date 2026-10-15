---
type: "query"
date: "2026-10-15T02:47:47.217681+00:00"
question: "how does application approval work in Servisyo?"
contributor: "graphify"
outcome: "useful"
source_nodes: ["ApprovalStatus", "Pending", "Rejected", "AccountController.cs", "ApplicationNotApproved.cshtml", "DashboardController.cs", ".Denied()", "AdminApplicationsController", ".Admin()", "ProviderApprovalService.cs"]
---

# Q: how does application approval work in Servisyo?

## Answer

In Servisyo, "application approval" is how an admin approves or rejects someone who signs up as a service provider. Every provider record has an `ApprovalStatus` of `Pending`, `Approved` or `Rejected`.

## 1. Applying
When someone signs up as a provider, the app signs them in and sends them to the **Pending Approval** page (`AccountController.cs:69`). Their record starts as `Pending`.

## 2. What the provider can open while they wait
Every provider page checks the status first: the dashboard, services, bookings, availability, profile and messages.
- **Pending:** they are sent back to the Pending Approval page.
- **Rejected:** the dashboard shows `ApplicationNotApproved.cshtml`, and the other provider pages send them to that dashboard.
- **Approved:** they get full access.
- **Suspended or deactivated account (or not a provider):** they are signed out.

You can see this in `DashboardController.cs:47-53`, and each provider controller has the same check in its `Denied(...)` helper.

## 3. Admin review
`AdminApplicationsController` handles this, and only administrators can reach it:
- **`GET admin/applications/{id}`** shows the application: name, business, location, services, description, base fee, contact details and date applied. Add `?partial=true` to get just the contents of the review dialog. The services listed are the provider's categories, or their active services if they have no categories yet (`ProviderApprovalService.cs:64-78`).
- **`POST admin/applications/{id}/decision`** takes a decision (`Approve` or `Reject`) and an optional note of up to 255 characters. The request is protected against cross-site forgery (`[ValidateAntiForgeryToken]`).

## 4. Making the decision
`ProviderApprovalService.DecideAsync` (`ProviderApprovalService.cs:107`) runs these steps:
1. **Re-checks the admin.** The user must still be an **Active** Administrator in the database. If not, they are signed out.
2. **Validates the request.** The decision must be a known value and the note must fit the length limit.
3. **Checks the application.** It must exist and still be `Pending`. Otherwise the admin sees "already decided, so nothing was changed."
4. **Applies the decision in one transaction.** On SQL Server it runs `UPDATE … WHERE ApprovalStatus = 'Pending'`, so if two admins decide at the same moment, only one update succeeds.
   - The status becomes `Approved` (with `DateApproved` set to the current time) or `Rejected`.
   - It writes an `AdminAction` log entry with the type, target, provider ID and note.
   - It sends the provider a `Notification`: "Your provider application was Approved/Rejected."
5. The admin goes back to the Admin Dashboard, or to the Providers list on the tab and page they came from, with a success or error message.

Some limits on what happens after a decision:
- It is final. Only `Pending` applications can be decided, and there is no re-review flow.
- Approval status is separate from account status (Active, Suspended, Deactivated, managed in `AccountTransitions.cs`). An approved provider can still be suspended.

The tests are in `tests/Servisyo.Tests/ProviderApprovalTests.cs`.

## Outcome

- Signal: useful

## Source Nodes

- ApprovalStatus
- Pending
- Rejected
- AccountController.cs
- ApplicationNotApproved.cshtml
- DashboardController.cs
- .Denied()
- AdminApplicationsController
- .Admin()
- ProviderApprovalService.cs
- Decision
- Approve