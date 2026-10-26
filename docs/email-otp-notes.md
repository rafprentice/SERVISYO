# Email codes, Stage A: Forgot Password (Gmail)

## What this stage adds
- `EmailService`: sends mail through Gmail SMTP, or (default) writes the mail to the log instead ("Log mode").
- `OtpService`: makes and checks 6-digit codes. Codes are stored hashed, expire after 15 minutes, work once, are locked after 5 wrong tries, and a new code can be asked for once a minute.
- `PasswordResetService` + the **Forgot password** and **Reset password** pages.
- New table `EMAIL_OTPS` and a new column `USERS.EmailVerificationPending` (not used yet; Stage B uses it for sign-up verification. Existing users and the seeded admin get `false`, so nobody is locked out).

## Rules the code enforces
| Rule | Where |
|---|---|
| Same answer for known and unknown emails (no "who is registered" leak) | `PasswordResetService.RequestResetAsync`, `AccountController.ForgotPassword` |
| Only **Active** accounts get a code | `PasswordResetService.FindActiveUserAsync` |
| Code is random, 6 digits, stored only as a hash | `OtpService.IssueAsync` |
| 15-minute expiry, single use, 5 tries, 60 s between codes | `OtpService` constants |
| New code cancels the older one | `OtpService.IssueAsync` |
| Password rules (8+ chars, letter + number) | `ResetPasswordViewModel` |

---

## Step 1. Three small edits in files I cannot see

### 1a. `src\Servisyo.Web\Models\Entities.cs`, class `User`
Add one line (anywhere inside the class, e.g. under `CreatedAt`):
```csharp
public bool EmailVerificationPending { get; set; }
```

### 1b. `src\Servisyo.Web\Data\ServisyoDbContext.cs`
Add the DbSet next to the others:
```csharp
public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
```
and add this block inside `OnModelCreating`, next to the `Notification` block:
```csharp
mb.Entity<EmailOtp>(e =>
{
    e.ToTable("EMAIL_OTPS");
    e.Property(x => x.Purpose).HasMaxLength(30);
    e.Property(x => x.CodeHash).HasMaxLength(255);
    e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
    e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    e.HasIndex(x => new { x.UserId, x.Purpose });
});
```

### 1c. `src\Servisyo.Web\Program.cs`
Next to the other `AddScoped` lines add:
```csharp
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
```

## Step 2. Build and create the migration
```powershell
dotnet build
dotnet ef migrations add AddEmailOtp --project src\Servisyo.Web
dotnet ef database update --project src\Servisyo.Web
```
The migration should contain `CreateTable("EMAIL_OTPS")` and `AddColumn<bool>("EmailVerificationPending")` with `defaultValue: false`. Nothing else. If it contains anything that drops or renames something, stop and send it to me.

## Step 3. Run the tests
```powershell
dotnet test
```

## Step 4. Try it on your laptop (Log mode, no Gmail needed)
1. `dotnet run --project src\Servisyo.Web`
2. Open **Sign In**, then **Forgot password?**, type an email that is registered, click **Send Reset Code**.
3. Look at the **terminal where dotnet run is running**. You will see a line starting with `EMAIL (Log mode, not sent)` and, inside it, `Your code: 123456`.
4. Type that code and a new password on the next page, then sign in with the new password.

## Step 5. Real Gmail (when you want actual emails)
1. Your Google account needs **2-Step Verification** turned on.
2. Open https://myaccount.google.com/apppasswords, create an App Password (name it SERVISYO). Google shows 16 letters. Copy them (spaces do not matter).
3. Locally, store the settings in user-secrets (never in a file that goes to GitHub):
```powershell
dotnet user-secrets set "Email:Mode" "Smtp" --project src\Servisyo.Web
dotnet user-secrets set "Email:Username" "yourname@gmail.com" --project src\Servisyo.Web
dotnet user-secrets set "Email:AppPassword" "abcdefghijklmnop" --project src\Servisyo.Web
```
4. Run again and send yourself a code. It should arrive in your inbox (check Spam the first time).

Use a throw-away Gmail for the project if you prefer. The App Password can be deleted any time on the same Google page.

## Step 6. Put it online (Monster)
1. Update the Monster database. This script is safe to run again, it skips what already exists:
```powershell
dotnet ef migrations script --idempotent -o deploy2.sql --project src\Servisyo.Web
```
Upload `deploy2.sql` in Monster: database, **Import**, **Upload SQL file**, **Import SQL file**.
2. Add this to `publish\appsettings.Production.json` (next to `ConnectionStrings` and `Seed`):
```json
"Email": {
  "Mode": "Smtp",
  "Username": "yourname@gmail.com",
  "AppPassword": "abcdefghijklmnop"
}
```
3. Publish again, zip, upload and unzip into `wwwroot` as before.
4. **Monster's free plan may block outgoing mail.** If no email arrives, open **Logs** on the Monster site. An error like `Could not send an email through smtp.gmail.com:587` means the host blocks it. Then set `"Mode": "Log"` in `appsettings.Production.json`. The code will be written to the Monster **Logs** page, so you can still demo the feature.

## Known limits
- Changing a password does not sign out devices that are already logged in (cookie sign-in has no server-side session list).
- Rate limiting is per account (1 code/minute, 5 tries per code), not per IP address.
- Two simultaneous requests with the same correct code could both pass the check. The window is a few milliseconds and the code is used up straight away.
- Stage B (sign-up verification) is not built yet: registration still works as before.
