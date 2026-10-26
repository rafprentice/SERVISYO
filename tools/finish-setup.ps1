# Run from the repo root:  powershell -ExecutionPolicy Bypass -File tools\finish-setup.ps1
# Adds what the feature files need to files only you have: Program.cs, Entities.cs, ServisyoDbContext.cs.
# Safe to run more than once: anything already there is skipped.
$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Edit-File($file, [scriptblock]$change) {
    $path = (Resolve-Path $file).Path
    $before = [System.IO.File]::ReadAllText($path)
    $after = & $change $before
    if ($after -ne $before) { [System.IO.File]::WriteAllText($path, $after, $utf8); Write-Host "Edited: $file" }
    else { Write-Host "Nothing to change: $file" }
}

# ---- 1. Program.cs: register every service ----
$lines = @(
    "builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));",
    "builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();",
    "builder.Services.AddScoped<IAuthService, AuthService>();",
    "builder.Services.AddScoped<IBookProviderService, BookProviderService>();",
    "builder.Services.AddScoped<IBookingMonitorService, BookingMonitorService>();",
    "builder.Services.AddScoped<ICategoryManagementService, CategoryManagementService>();",
    "builder.Services.AddScoped<ICustomerBookingsService, CustomerBookingsService>();",
    "builder.Services.AddScoped<ICustomerDashboardService, CustomerDashboardService>();",
    "builder.Services.AddScoped<ICustomerProfileService, CustomerProfileService>();",
    "builder.Services.AddScoped<ICustomerReviewService, CustomerReviewService>();",
    "builder.Services.AddScoped<IBookingPaymentService, BookingPaymentService>();",
    "builder.Services.AddScoped<IBookingLiveUpdates, BookingLiveUpdates>();",
    "builder.Services.AddSignalR();",
    "builder.Services.AddScoped<IEmailService, EmailService>();",
    "builder.Services.AddScoped<IFindServicesService, FindServicesService>();",
    "builder.Services.AddScoped<IHomePageService, HomePageService>();",
    "builder.Services.AddScoped<IMessagingService, MessagingService>();",
    "builder.Services.AddScoped<INotificationService, NotificationService>();",
    "builder.Services.AddScoped<IOtpService, OtpService>();",
    "builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();",
    "builder.Services.AddScoped<IProviderApprovalService, ProviderApprovalService>();",
    "builder.Services.AddScoped<IProviderAvailabilityService, ProviderAvailabilityService>();",
    "builder.Services.AddScoped<IProviderBookingsService, ProviderBookingsService>();",
    "builder.Services.AddScoped<IProviderDashboardService, ProviderDashboardService>();",
    "builder.Services.AddScoped<IProviderManagementService, ProviderManagementService>();",
    "builder.Services.AddScoped<IProviderOfferingsService, ProviderOfferingsService>();",
    "builder.Services.AddScoped<IProviderProfileService, ProviderProfileService>();",
    "builder.Services.AddScoped<IReviewModerationService, ReviewModerationService>();",
    "builder.Services.AddScoped<IUserManagementService, UserManagementService>();"
)
Edit-File "src\Servisyo.Web\Program.cs" {
    param($c)
    $anchor = "builder.Services.AddScoped<IAuthService, AuthService>();"
    if (-not $c.Contains($anchor)) { Write-Host "COULD NOT FIND '$anchor' in Program.cs - tell Claude"; return $c }
    $missing = $lines | Where-Object { $_ -ne $anchor -and -not $c.Contains($_) }
    if ($missing.Count -eq 0) { return $c }
    $c.Replace($anchor, $anchor + "`r`n" + ($missing -join "`r`n"))
}

# ---- 1b. Program.cs: the live-updates hub address ----
Edit-File "src\Servisyo.Web\Program.cs" {
    param($c)
    $hub = "app.MapHub<Servisyo.Web.Hubs.BookingHub>(Servisyo.Web.Hubs.BookingHub.Path);"
    if ($c.Contains($hub)) { return $c }
    $anchor = "app.Run();"
    $i = $c.LastIndexOf($anchor)
    if ($i -lt 0) { Write-Host "COULD NOT FIND 'app.Run();' in Program.cs - tell Claude"; return $c }
    $c.Insert($i, $hub + "`r`n")
}

# ---- 2. Entities.cs: User.EmailVerificationPending ----
Edit-File "src\Servisyo.Web\Models\Entities.cs" {
    param($c)
    if ($c.Contains("EmailVerificationPending")) { return $c }
    $anchor = "    public Customer? Customer { get; set; }"
    if (-not $c.Contains($anchor)) { Write-Host "COULD NOT FIND the User class anchor in Entities.cs - tell Claude"; return $c }
    $i = $c.IndexOf($anchor)
    $c.Insert($i + $anchor.Length, "`r`n    public bool EmailVerificationPending { get; set; }")
}

# ---- 3. ServisyoDbContext.cs: EmailOtps table ----
Edit-File "src\Servisyo.Web\Data\ServisyoDbContext.cs" {
    param($c)
    if (-not $c.Contains("DbSet<EmailOtp>")) {
        $a = "public DbSet<Notification> Notifications => Set<Notification>();"
        if ($c.Contains($a)) { $c = $c.Replace($a, $a + "`r`n    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();") }
        else { Write-Host "COULD NOT FIND the Notifications DbSet - tell Claude" }
    }
    if (-not $c.Contains("mb.Entity<EmailOtp>")) {
        $a = "        mb.Entity<Notification>(e =>"
        $block = @"
        mb.Entity<EmailOtp>(e =>
        {
            e.ToTable("EMAIL_OTPS");
            e.Property(x => x.Purpose).HasMaxLength(30);
            e.Property(x => x.CodeHash).HasMaxLength(255);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.Purpose });
        });

"@
        $i = $c.IndexOf($a)
        if ($i -ge 0) { $c = $c.Insert($i, $block) } else { Write-Host "COULD NOT FIND the Notification block - tell Claude" }
    }
    $c
}

Write-Host ""
Write-Host "Done. Next: dotnet build"
