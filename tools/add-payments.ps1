# Payments (Cash on Completion / Mock E-Wallet): one-time setup.
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-payments.ps1
# Safe to run again: anything already done is skipped. It does NOT touch the database; you run "dotnet ef database update" yourself.
$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Text($file)  { [System.IO.File]::ReadAllText((Resolve-Path $file).Path) }
function Write-Text($file, $text) { [System.IO.File]::WriteAllText((Resolve-Path $file).Path, $text, $utf8); Write-Host "Edited: $file" }

# ---------------------------------------------------------------- 1. Entities.cs: Booking.Payment
$entFile = "src\Servisyo.Web\Models\Entities.cs"
$e = Read-Text $entFile
if (-not $e.Contains("public Payment? Payment { get; set; }")) {
    $a = "    public Review? Review { get; set; }"
    $i = $e.IndexOf($a)
    if ($i -lt 0) { throw "COULD NOT FIND '$a' (the Booking class) in $entFile - tell Claude" }
    $e = $e.Insert($i + $a.Length, "`r`n    public Payment? Payment { get; set; }        // PAYMENTS (one per booking, made by Approve & Pay)")
    Write-Text $entFile $e
} else { Write-Host "Already done: $entFile" }

# ---------------------------------------------------------------- 2. ServisyoDbContext.cs
$ctxFile = "src\Servisyo.Web\Data\ServisyoDbContext.cs"
$c = Read-Text $ctxFile
$before = $c

if (-not $c.Contains("DbSet<Payment>")) {
    $a = "public DbSet<Notification> Notifications => Set<Notification>();"
    if (-not $c.Contains($a)) { throw "COULD NOT FIND the Notifications DbSet in $ctxFile - tell Claude" }
    $c = $c.Replace($a, $a + "`r`n    public DbSet<Payment> Payments => Set<Payment>();`r`n    public DbSet<CashPaymentDetails> CashPayments => Set<CashPaymentDetails>();")
}

if (-not $c.Contains("mb.Entity<Payment>")) {
    $a = "        mb.Entity<Notification>(e =>"
    $i = $c.IndexOf($a)
    if ($i -lt 0) { throw "COULD NOT FIND the Notification block in $ctxFile - tell Claude" }
    $block = @"
        mb.Entity<Payment>(e =>
        {
            e.ToTable("PAYMENTS", t =>
            {
                t.HasCheckConstraint("CK_PAYMENTS_METHOD", "[PAYMENT_METHOD] IN ('CashOnCompletion','MockEWallet')");
                t.HasCheckConstraint("CK_PAYMENTS_STATUS", "[PAYMENT_STATUS] IN ('PendingCollection','Paid','Refunded')");
                t.HasCheckConstraint("CK_PAYMENTS_AMOUNT", "[AMOUNT] >= 0");
                // only cash can wait to be collected, and anything paid (or refunded) has a paid date
                t.HasCheckConstraint("CK_PAYMENTS_PENDING_CASH", "[PAYMENT_STATUS] <> 'PendingCollection' OR [PAYMENT_METHOD] = 'CashOnCompletion'");
                t.HasCheckConstraint("CK_PAYMENTS_PAID_AT", "[PAYMENT_STATUS] = 'PendingCollection' OR [PAID_AT] IS NOT NULL");
            });
            e.Property(x => x.PaymentMethod).HasMaxLength(50);
            e.Property(x => x.PaymentStatus).HasMaxLength(50);
            e.Property(x => x.TransactionReference).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Booking).WithOne(b => b.Payment).HasForeignKey<Payment>(x => x.BookingId);
            e.HasIndex(x => x.BookingId).IsUnique();                // one payment per booking: a second one is refused
            e.HasIndex(x => x.TransactionReference).IsUnique();
        });

        mb.Entity<CashPaymentDetails>(e =>
        {
            e.ToTable("CASH_PAYMENT_DETAILS", t =>
                t.HasCheckConstraint("CK_CASH_COLLECTED",
                    "([IS_COLLECTED_BY_PROVIDER] = 0 AND [COLLECTED_AT] IS NULL) OR ([IS_COLLECTED_BY_PROVIDER] = 1 AND [COLLECTED_AT] IS NOT NULL)"));
            e.HasKey(x => x.PaymentId);
            e.Property(x => x.PaymentId).ValueGeneratedNever();     // same number as the payment
            e.Property(x => x.IsCollectedByProvider).HasDefaultValue(false);
            e.HasOne(x => x.Payment).WithOne(p => p.CashDetails).HasForeignKey<CashPaymentDetails>(x => x.PaymentId);
        });

"@
    $c = $c.Insert($i, $block)
}

if ($c -ne $before) { Write-Text $ctxFile $c } else { Write-Host "Already done: $ctxFile" }

# ---------------------------------------------------------------- 3. build + migration
Write-Host ""
Write-Host "Building..."
dotnet build src\Servisyo.Web | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed - fix the errors above (or send them to Claude), then run this script again." }

$migDir = "src\Servisyo.Web\Migrations"
$mig = Get-ChildItem $migDir -Filter "*_AddPayments.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1
if (-not $mig) {
    Write-Host "Creating the AddPayments migration..."
    dotnet ef migrations add AddPayments --project src\Servisyo.Web | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations add failed - see above." }
    $mig = Get-ChildItem $migDir -Filter "*_AddPayments.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1
} else { Write-Host "Already done: $($mig.Name)" }

Write-Host ""
Write-Host "Done. Open $($mig.Name): it should only CreateTable PAYMENTS and CASH_PAYMENT_DETAILS (+ indexes and check constraints)."
Write-Host "If it also drops or changes any other table, STOP and send it to Claude."
Write-Host "Then run:  dotnet ef database update --project src\Servisyo.Web"
