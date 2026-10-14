# Provider application approval - notes

## What it does
- **Needs Admin Attention > Review Applications** and **Pending Applications > Review Application** (Admin Dashboard),
  and **Review Application** on a Pending card (Providers page) open the *Review Provider Application* pop-up.
- **Approve & Activate Provider**: Pending -> Approved, DATE_APPROVED set, admin-log entry
  (`Approved` / `SERVICE_PROVIDER`), notification "Your provider application was Approved.", green message.
- **Reject Application** (asks "are you sure?" first): Pending -> Rejected, DATE_APPROVED stays empty, admin-log entry
  (`Rejected`), notification "Your provider application was Rejected.".
- The optional **Note** is saved in ADMIN_ACTIONS.REASON. The applicant never sees it.
- Only a **Pending** application can be decided. A second click / second admin gets "already been decided" and nothing changes.
- With several applications waiting, the banner button opens the **oldest** one first.

## Where the code is
| Part | File |
|---|---|
| Rules (approve/reject, log, notification) | `Services/ProviderApprovalService.cs` |
| Pages | `Controllers/AdminApplicationsController.cs` (`admin/applications/{id}` and `admin/applications/{id}/decision`) |
| Pop-up content | `Views/AdminApplications/_ApplicationReview.cshtml` |
| No-JavaScript page | `Views/AdminApplications/Review.cshtml` |
| Pop-up shell / message | `Views/Shared/_ApplicationReviewDialog.cshtml`, `Views/Shared/_AdminToast.cshtml` |
| Behaviour | `wwwroot/js/admin-approval.js` |
| Looks | end of `wwwroot/css/portal.css` (`.sv-rv__*`, `.sv-toast`) |

## Program.cs - one new line next to the other services
```csharp
builder.Services.AddScoped<IProviderApprovalService, ProviderApprovalService>();
```

## Try it by hand
1. Make sure there are pending sample providers: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d SERVISYO -E -i docs\dev-sample-providers-data.sql`
2. Sign in as the administrator. The dashboard should say "2 provider applications waiting for review".
3. Click **Review Applications** in the banner. The pop-up opens for the oldest applicant (Felix Torres).
   Check: Applicant ID `#PRV-<year>-<id>`, name, business, location, services, email.
4. Press **Esc**, the **x**, the **Close** link or click the dark area: the pop-up closes and nothing changes.
5. Open it again and press **Approve & Activate Provider**. You come back to the dashboard with a green message
   "Felix Torres has been approved as an active service provider.", the banner now says 1 application, and the
   Pending Approvals number goes 2 -> 1.
6. Approve or reject the last one: banner disappears, widget says "No pending applications".
7. Providers page: the **Approved** tab number went up, **Pending** went down. Rejected providers appear under **Rejected**.
8. Check the database:
   ```sql
   SELECT TOP 5 * FROM ADMIN_ACTIONS  ORDER BY 1 DESC;
   SELECT TOP 5 * FROM NOTIFICATIONS  ORDER BY 1 DESC;
   ```
9. To repeat the test: `sqlcmd ... -i docs\dev-reset-pending-application.sql` (sample accounts only).
10. Two-tab race test: open the same pending application in two browser tabs, approve in tab 1, then approve (or reject) in
    tab 2. Tab 2 must show "already been decided" and the log must contain only ONE entry for that provider.
11. Turn JavaScript off (or open `/admin/applications/<id>` directly): the same content appears as a normal page and the
    buttons still work.

## Limits (on purpose)
- **No document uploads exist** in the database design, so the "Submitted Documents" row says so instead of showing
  made-up "Verified" ticks.
- A **rejected** application cannot be re-opened for approval from this screen (the mock-up has no such button).
- The race-proof `UPDATE ... WHERE Pending` runs only on SQL Server. The unit tests use the in-memory database, which cannot run it,
  so step 10 above is the real test of that part.
- If you ever turn on `EnableRetryOnFailure` for SQL Server, the manual transaction in `DecideAsync` must be wrapped in
  `Database.CreateExecutionStrategy().ExecuteAsync(...)`.
