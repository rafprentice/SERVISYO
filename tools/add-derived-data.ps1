# SERVISYO database v2.3 ("DerivedData"): one fact in one place, nothing stored that can be calculated,
# derived tables become TOP 25 queries.
#   - NOTIFICATIONS, BOOKING_REPORTS, EMAIL_OTPS and CASH_PAYMENT_DETAILS are removed.
#   - PAYMENTS becomes the list of payment methods (1 Cash on Completion, 2 GCash, 3 Maya);
#     PAYMENTS_DTL (new) holds each booking's payment. Existing payments are copied.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-derived-data.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not touched until the very end, and only if you type YES):
#   1. build + run all tests
#   2. create the EF migration "DerivedData" and put the data-copy SQL in it (checks what EF generated)
#   3. read-only check of SERVISYO (docs\sql\check-before-derived-data.sql): stops if a payment cannot be converted
#   4. BACKUP of SERVISYO to Documents\SERVISYO-backups (COPY_ONLY: your normal backups are not affected)
#   5. a COPY of that backup as SERVISYO_V23_TEST: update it, run the self-test (DatabaseDesign-v2.sql PART 6),
#      then undo (Down) and redo (Up) the migration and check that every payment is still there
#   6. only if all of that passed and you type YES: update SERVISYO, then check it
# Safe to run again: anything already done is skipped.
$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)
$Server = "(localdb)\MSSQLLocalDB"
$Main = "SERVISYO"
$Copy = "SERVISYO_V23_TEST"
$Web = "src\Servisyo.Web"
$MigDir = "$Web\Migrations"

function Conn([string]$db) { "Server=$Server;Database=$db;Trusted_Connection=True;TrustServerCertificate=True" }
function Say([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Stop-Here([string]$text) { Write-Host ""; Write-Host "STOPPED: $text" -ForegroundColor Red; Write-Host "Nothing was changed in $Main."; exit 1 }

# runs SQL batches (split on GO lines) on ONE connection; returns @{ Tables = all result tables; Messages = PRINT / error text }
function Invoke-Sql([string]$db, [string]$sql, [int]$timeout = 600, [switch]$ErrorsAsMessages) {
    $messages = New-Object System.Collections.Generic.List[string]
    $tables = New-Object System.Collections.Generic.List[System.Data.DataTable]
    $c = New-Object System.Data.SqlClient.SqlConnection (Conn $db)
    $c.add_InfoMessage({ param($src, $ev) foreach ($err in $ev.Errors) { $messages.Add($err.Message) } }.GetNewClosure())
    $c.FireInfoMessageEventOnUserErrors = [bool]$ErrorsAsMessages
    $c.Open()
    try {
        foreach ($batch in [regex]::Split($sql, '(?m)^\s*GO\s*$')) {
            if ($batch.Trim().Length -eq 0) { continue }
            $cmd = $c.CreateCommand()
            $cmd.CommandText = $batch
            $cmd.CommandTimeout = $timeout
            $ds = New-Object System.Data.DataSet
            [void](New-Object System.Data.SqlClient.SqlDataAdapter $cmd).Fill($ds)
            foreach ($t in $ds.Tables) { $tables.Add($t) }
        }
    } finally { $c.Close() }
    return @{ Tables = $tables; Messages = $messages }
}
function Scalar([string]$db, [string]$sql) { (Invoke-Sql $db $sql).Tables[0].Rows[0][0] }
function Q([string]$s) { $s.Replace("'", "''") }

function Ef([string[]]$efArgs) {
    & dotnet ef @efArgs --project $Web | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "dotnet ef $($efArgs -join ' ') failed - send the text above to Claude." }
}

# ---------------------------------------------------------------- 0. the files from the zip must be here
# files that database v2.3 no longer has (extracting a zip cannot delete files; Git still has them)
foreach ($gone in @("$Web\Models\EmailOtp.cs", "$Web\Services\NotificationCleanupService.cs", "tests\Servisyo.Tests\NotificationCleanupTests.cs")) {
    if (Test-Path $gone) { Remove-Item $gone; Write-Host "Removed (not used in v2.3): $gone" }
}
foreach ($f in @("$Web\Data\DerivedDataSql.cs", "$Web\Data\DerivedQueries.cs", "docs\sql\DatabaseDesign-v2.sql", "docs\sql\check-before-derived-data.sql")) {
    if (-not (Test-Path $f)) { Stop-Here "MISSING $f - extract derived_data.zip in the repo root first." }
}
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Data\ServisyoDbContext.cs").Path)
if (-not $ctx.Contains("PAYMENTS_DTL")) { Stop-Here "ServisyoDbContext.cs has no PAYMENTS_DTL - extract derived_data.zip again (it replaces that file)." }
foreach ($m in @("ProfileCleanup", "AdminTargets")) {
    if (-not (Get-ChildItem $MigDir -Filter "*_$m.cs" -ErrorAction SilentlyContinue)) {
        Stop-Here "The $m migration is missing. It must come first (tools\add-$(if ($m -eq 'AdminTargets') { 'admin-targets' } else { 'profile-cleanup' }).ps1)."
    }
}
function Find-Migration { Get-ChildItem $MigDir -Filter "*_DerivedData.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration DerivedData"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "DerivedData")
    $mig = Find-Migration
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
if (-not $m.Contains("DerivedDataSql")) {
    $upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
    $downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
    $up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
    if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
    $upPart = $m.Substring($up, $down - $up)

    # which tables EF touches in Up(): only these may appear
    $allowed = @("NOTIFICATIONS", "EMAIL_OTPS", "BOOKING_REPORTS", "CASH_PAYMENT_DETAILS", "PAYMENTS", "PAYMENTS_DTL", "USERS")
    $ops = [regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1
    # EF may RE-USE one old PAYMENTS column (same type: NVARCHAR(50) NOT NULL) as METHOD_NAME instead of dropping it and
    # adding METHOD_NAME (and rename its index). That is harmless here: StagePayments saves every payment and empties
    # PAYMENTS before any EF step runs. Only exactly that rename is accepted; every other rename stops the script.
    $renamedFrom = $null
    Write-Host "EF operations in Up():"
    foreach ($op in $ops) {
        $name = ([regex]::Match($op, '^\w+')).Value
        $t = if ($name -in @("CreateTable", "DropTable")) { ([regex]::Match($op, 'name: "(\w+)"')).Groups[1].Value } else { ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value }
        $what = if ($name -in @("CreateTable", "DropTable", "InsertData", "DeleteData")) { "" } else { ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value }
        $to = ([regex]::Match($op, '\bnewName: "(\w+)"')).Groups[1].Value
        Write-Host ("  {0,-22} {1}{2}{3}" -f $name, $t, $(if ($what) { ".$what" } else { "" }), $(if ($to) { " -> $to" } else { "" }))
        if ($name -in @("RenameTable", "Sql")) { Stop-Here "Up() has a $name operation - send $($mig.Name) to Claude." }
        if ($name -eq "RenameColumn") {
            if ($t -ne "PAYMENTS" -or $to -ne "METHOD_NAME" -or -not ($what -in @("PAYMENT_METHOD", "PAYMENT_STATUS", "TRANSACTION_REFERENCE")) -or $renamedFrom) {
                Stop-Here "Up() renames $t.$what to $to - send $($mig.Name) to Claude."
            }
            $renamedFrom = $what
        }
        if ($name -eq "RenameIndex" -and ($t -ne "PAYMENTS" -or $to -ne "IX_PAYMENTS_METHOD_NAME")) {
            Stop-Here "Up() renames the index $what on $t to $to - send $($mig.Name) to Claude."
        }
        if ($t -and -not ($allowed -contains $t)) { Stop-Here "Up() changes the table $t ($name) - send $($mig.Name) to Claude." }
        if ($name -eq "AlterColumn" -and $what -eq "PAYMENT_ID") { Stop-Here "Up() alters PAYMENTS.PAYMENT_ID - send $($mig.Name) to Claude." }
    }
    if ($renamedFrom) { Write-Host "EF re-uses the (emptied) PAYMENTS.$renamedFrom column as METHOD_NAME: accepted, the payments are saved first." }
    # what EF must have generated
    $must = @(
        @('DropTable\(\s*name: "NOTIFICATIONS"', "drop NOTIFICATIONS"),
        @('DropTable\(\s*name: "EMAIL_OTPS"', "drop EMAIL_OTPS"),
        @('DropTable\(\s*name: "BOOKING_REPORTS"', "drop BOOKING_REPORTS"),
        @('DropTable\(\s*name: "CASH_PAYMENT_DETAILS"', "drop CASH_PAYMENT_DETAILS"),
        @('CreateTable\(\s*name: "PAYMENTS_DTL"', "create PAYMENTS_DTL"),
        @('AddColumn<DateTime>\(\s*name: "NOTIFICATIONS_SEEN_AT",\s*table: "USERS"', "add USERS.NOTIFICATIONS_SEEN_AT"),
        @('InsertData\(\s*table: "PAYMENTS"', "insert the three PAYMENTS rows"))
    if (-not $renamedFrom) {
        $must += ,@('AddColumn<string>\(\s*name: "METHOD_NAME",\s*table: "PAYMENTS"', "add PAYMENTS.METHOD_NAME")
    }
    foreach ($col in @("AMOUNT", "BOOKING_ID", "CREATED_AT", "PAID_AT", "PAYMENT_METHOD", "PAYMENT_STATUS", "TRANSACTION_REFERENCE")) {
        if ($col -eq $renamedFrom) { continue }      # re-used as METHOD_NAME (see above)
        $must += ,@(('DropColumn\(\s*name: "' + $col + '",\s*table: "PAYMENTS"'), "drop PAYMENTS.$col")
    }
    foreach ($x in $must) { if (-not ($upPart -match $x[0])) { Stop-Here "Up() does not $($x[1]) - send $($mig.Name) to Claude." } }

    # start of Up(): save the payments (stops, changing nothing, on a payment it cannot convert)
    $open = $m.IndexOf("{", $up)
    $m = $m.Insert($open + 1, "`r`n            // database v2.3: save every payment before EF changes PAYMENTS (docs\sql\DatabaseDesign-v2.sql)`r`n" +
                              "            migrationBuilder.Sql(Servisyo.Web.Data.DerivedDataSql.StagePayments);`r`n")
    # end of Up(): copy the payments into PAYMENTS_DTL, then the v2.3 view / procedures / triggers
    $down = $m.IndexOf($downSig)
    $upEnd = $m.LastIndexOf("}", $down)
    $m = $m.Insert($upEnd, "`r`n            // database v2.3: the saved payments go into PAYMENTS_DTL, then the procedures, view and triggers`r`n" +
                           "            migrationBuilder.Sql(Servisyo.Web.Data.DerivedDataSql.FillPaymentDetails);`r`n" +
                           "            foreach (var sql in Servisyo.Web.Data.DerivedDataSql.CreateAll)`r`n" +
                           "                migrationBuilder.Sql(sql);`r`n        ")
    # start of Down(): save PAYMENTS_DTL; end of Down(): the v2.2 PAYMENTS rows and objects back
    $down = $m.IndexOf($downSig)
    $open = $m.IndexOf("{", $down)
    $m = $m.Insert($open + 1, "`r`n            migrationBuilder.Sql(Servisyo.Web.Data.DerivedDataSql.StageOld);`r`n")
    $classEnd = $m.LastIndexOf("}", $m.LastIndexOf("}", $m.LastIndexOf("}") - 1) - 1)
    $m = $m.Insert($classEnd, "`r`n            migrationBuilder.Sql(Servisyo.Web.Data.DerivedDataSql.FillOld);`r`n" +
                              "            foreach (var sql in Servisyo.Web.Data.DerivedDataSql.RestoreV22)`r`n" +
                              "                migrationBuilder.Sql(sql);`r`n        ")
    [System.IO.File]::WriteAllText($mig.FullName, $m, $utf8)
    Write-Host "Edited: $($mig.Name) (the data-copy SQL is in it)"

    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed after editing the migration - send the errors above to Claude." }
} else { Write-Host "Already done: $($mig.Name)" }
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
$previous = (Get-ChildItem $MigDir -Filter "*.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" -and $_.Name -match '^\d{14}_' -and $_.Name -lt $mig.Name } |
             Sort-Object Name | Select-Object -Last 1).BaseName

# ---------------------------------------------------------------- 3. read-only check of SERVISYO
Say "3. Read-only check of $Main"
$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -gt 0) {
    Write-Host "$Main is already on database v2.3 ($migId). Nothing to do."
    exit 0
}
$check = Invoke-Sql $Main ([System.IO.File]::ReadAllText((Resolve-Path "docs\sql\check-before-derived-data.sql").Path))
$problems = $check.Tables[1]
$removed = $check.Tables[2]
$payments = $check.Tables[0].Rows.Count
Write-Host "Payments to convert: $payments"
$removed | Format-Table -AutoSize | Out-Host
if ($problems.Rows.Count -gt 0) {
    $problems | Format-Table -AutoSize | Out-Host
    Stop-Here "these payments cannot be converted safely - send this list to Claude."
}
$reports = ($removed.Rows | Where-Object { $_.TABLE_NAME -eq "BOOKING_REPORTS" }).ROWS_NOW
if ($reports -gt 0) { Write-Host "Note: BOOKING_REPORTS has $reports row(s) (sample data from docs\dev-sample-admin-data.sql). They stay only in the backup." -ForegroundColor Yellow }

# ---------------------------------------------------------------- 4. backup
Say "4. Backup of $Main"
$backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
New-Item -ItemType Directory -Force $backupDir | Out-Null
$bak = Join-Path $backupDir ("SERVISYO-before-v2.3-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
[void](Invoke-Sql "master" "BACKUP DATABASE [$Main] TO DISK = N'$(Q $bak)' WITH COPY_ONLY, INIT, CHECKSUM;")
Write-Host "Backup: $bak"

# ---------------------------------------------------------------- 5. the test copy
Say "5. Test on a copy ($Copy)"
if ((Scalar "master" "SELECT COUNT(*) FROM sys.databases WHERE name = N'$Copy'") -gt 0) {
    $answer = Read-Host "An older test copy $Copy exists. Type YES to replace it (only the test copy; $Main is not touched)"
    if ($answer -cne "YES") { Stop-Here "the old test copy was kept. Delete or rename $Copy in SSMS, then run this again." }
    [void](Invoke-Sql "master" "ALTER DATABASE [$Copy] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Copy];")
}
$dataDir = [string](Scalar "master" "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(400))")
if ([string]::IsNullOrWhiteSpace($dataDir)) { $dataDir = $backupDir }
$moves = foreach ($r in (Invoke-Sql "master" "RESTORE FILELISTONLY FROM DISK = N'$(Q $bak)';").Tables[0].Rows) {
    $target = Join-Path $dataDir ($Copy + $(if ($r.Type -eq "L") { "_log.ldf" } else { ".mdf" }))
    "MOVE N'$(Q $r.LogicalName)' TO N'$(Q $target)'"
}
[void](Invoke-Sql "master" "RESTORE DATABASE [$Copy] FROM DISK = N'$(Q $bak)' WITH $($moves -join ', '), RECOVERY;")
Write-Host "Copy restored."

$copyConn = Conn $Copy
Ef @("database", "update", "--connection", $copyConn)
$dtl = Scalar $Copy "SELECT COUNT(*) FROM dbo.PAYMENTS_DTL"
if ($dtl -ne $payments) { Stop-Here "the copy has $dtl payments in PAYMENTS_DTL, but $Main has $payments." }
Write-Host "Copy updated: $dtl payment(s) in PAYMENTS_DTL, $(Scalar $Copy 'SELECT COUNT(*) FROM dbo.PAYMENTS') payment methods."

Say "5b. Self-test on the copy (DatabaseDesign-v2.sql, everything is rolled back)"
$run = Invoke-Sql $Copy ([System.IO.File]::ReadAllText((Resolve-Path "docs\sql\DatabaseDesign-v2.sql").Path)) -ErrorsAsMessages
$grid = $run.Tables | Where-Object { $_.Columns.Contains("TEST") -and $_.Columns.Contains("RESULT") } | Select-Object -Last 1
$summary = $run.Messages | Where-Object { $_ -like "SELF-TEST:*" } | Select-Object -Last 1
if ($grid) { $grid | Where-Object { $_.RESULT -ne "PASS" } | Format-Table -AutoSize -Wrap | Out-Host }
Write-Host $summary
if (-not $grid -or -not ($summary -like "SELF-TEST: all *")) {
    $run.Messages | Select-Object -Last 15 | Out-Host
    Stop-Here "the self-test did not pass on the copy - send the text above to Claude."
}

Say "5c. Undo and redo the migration on the copy"
Ef @("database", "update", $previous, "--connection", $copyConn)
$old = Scalar $Copy "SELECT COUNT(*) FROM dbo.PAYMENTS"
$cash = Scalar $Copy "SELECT COUNT(*) FROM dbo.PAYMENTS p JOIN dbo.CASH_PAYMENT_DETAILS c ON c.PAYMENT_ID = p.PAYMENT_ID"
if ($old -ne $payments) { Stop-Here "after undoing, the copy has $old payments instead of $payments." }
Write-Host "Undo OK: $old payment(s) back in the v2.2 PAYMENTS table ($cash with cash details)."
Ef @("database", "update", "--connection", $copyConn)
if ((Scalar $Copy "SELECT COUNT(*) FROM dbo.PAYMENTS_DTL") -ne $payments) { Stop-Here "after redoing, PAYMENTS_DTL does not have $payments payments." }
Write-Host "Redo OK."

# ---------------------------------------------------------------- 6. SERVISYO
Say "6. Update $Main"
Write-Host "Everything passed on the copy. Your backup: $bak"
$answer = Read-Host "Type YES to update $Main now (anything else stops here)"
if ($answer -cne "YES") { Write-Host "Not updated. Run this script again when you are ready (the copy tests run again)."; exit 0 }
Ef @("database", "update")

$ok = $true
$checks = Invoke-Sql $Main @"
SELECT
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)') AS MIGRATION_APPLIED,
    (SELECT COUNT(*) FROM dbo.PAYMENTS) AS PAYMENT_METHODS,
    (SELECT COUNT(*) FROM dbo.PAYMENTS_DTL) AS BOOKING_PAYMENTS,
    CASE WHEN OBJECT_ID(N'dbo.NOTIFICATIONS') IS NULL AND OBJECT_ID(N'dbo.BOOKING_REPORTS') IS NULL
          AND OBJECT_ID(N'dbo.EMAIL_OTPS') IS NULL AND OBJECT_ID(N'dbo.CASH_PAYMENT_DETAILS') IS NULL THEN 1 ELSE 0 END AS OLD_TABLES_GONE,
    CASE WHEN OBJECT_ID(N'dbo.usp_GetNotifications') IS NOT NULL AND OBJECT_ID(N'dbo.usp_GetBookingsNeedingAttention') IS NOT NULL
          AND OBJECT_ID(N'dbo.trg_PaymentsDtl_Audit') IS NOT NULL THEN 1 ELSE 0 END AS NEW_OBJECTS_THERE
"@
$row = $checks.Tables[0].Rows[0]
$checks.Tables[0] | Format-Table -AutoSize | Out-Host
if ($row.MIGRATION_APPLIED -ne 1 -or $row.PAYMENT_METHODS -ne 3 -or $row.BOOKING_PAYMENTS -ne $payments -or $row.OLD_TABLES_GONE -ne 1 -or $row.NEW_OBJECTS_THERE -ne 1) {
    $ok = $false
}
if (-not $ok) {
    Write-Host "Something is not as expected - send the table above to Claude. Your backup: $bak" -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.3. Backup: $bak" -ForegroundColor Green
Write-Host "The test copy $Copy is still there; you can delete it in SSMS when you no longer need it."
Write-Host "Next: dotnet run --project $Web   (open the bell, Admin Dashboard and a payment to see it working)"
