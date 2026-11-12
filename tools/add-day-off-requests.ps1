# SERVISYO database v2.7 ("DayOffRequests"): providers REQUEST days off, an administrator approves or rejects.
#   - new table PROVIDER_DAY_OFF_REQUESTS and column PROVIDER_UNAVAILABLE_DATES.REQUEST_ID (EF makes them by itself)
#   - fn_IsProviderAvailable, usp_CreateBooking: a day of a PENDING request does not stop bookings yet
#   - usp_GetNotifications: the provider sees the answer, the administrator sees the waiting requests
#   (Servisyo.Web.Data.DayOffRequestsSql, added to the migration by this script)
#   No data is changed: the days off saved so far have no request, so they stay days off.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-day-off-requests.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not changed until you type YES):
#   1. build + run all tests
#   2. create the EF migration "DayOffRequests", check that EF only makes the table, the column and their keys,
#      indexes and CHECKs, and add the DayOffRequestsSql steps to it
#   3. BACKUP of SERVISYO to Documents\SERVISYO-backups (COPY_ONLY)
#   4. only if you type YES: update SERVISYO, then check it
# Safe to run again: anything already done is skipped.
$ErrorActionPreference = "Stop"
$Server = "(localdb)\MSSQLLocalDB"
$Main = "SERVISYO"
$Web = "src\Servisyo.Web"
$MigDir = "$Web\Migrations"

function Conn([string]$db) { "Server=$Server;Database=$db;Trusted_Connection=True;TrustServerCertificate=True" }
function Say([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Stop-Here([string]$text) { Write-Host ""; Write-Host "STOPPED: $text" -ForegroundColor Red; Write-Host "Nothing was changed in $Main."; exit 1 }
function Q([string]$s) { $s.Replace("'", "''") }

function Invoke-Sql([string]$db, [string]$sql) {
    $c = New-Object System.Data.SqlClient.SqlConnection (Conn $db)
    $c.Open()
    try {
        $cmd = $c.CreateCommand()
        $cmd.CommandText = $sql
        $cmd.CommandTimeout = 600
        $ds = New-Object System.Data.DataSet
        [void](New-Object System.Data.SqlClient.SqlDataAdapter $cmd).Fill($ds)
        return $ds
    } finally { $c.Close() }
}
function Scalar([string]$db, [string]$sql) { (Invoke-Sql $db $sql).Tables[0].Rows[0][0] }

function Ef([string[]]$efArgs) {
    & dotnet ef @efArgs --project $Web | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "dotnet ef $($efArgs -join ' ') failed - send the text above to Claude." }
}

# the index of the "}" that closes the block opened by the "{" at $open
function Block-End([string]$text, [int]$open) {
    $depth = 0
    for ($i = $open; $i -lt $text.Length; $i++) {
        if ($text[$i] -eq '{') { $depth++ }
        elseif ($text[$i] -eq '}') { $depth--; if ($depth -eq 0) { return $i } }
    }
    return -1
}

# ---------------------------------------------------------------- 0. the files from the zip must be here, v2.6 first
if (-not (Test-Path "$Web\Data\DayOffRequestsSql.cs")) { Stop-Here "$Web\Data\DayOffRequestsSql.cs is missing - extract the zip in the repo root first." }
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Data\ServisyoDbContext.cs").Path)
if (-not $ctx.Contains("PROVIDER_DAY_OFF_REQUESTS")) { Stop-Here "ServisyoDbContext.cs does not have the day-off requests - extract the zip in the repo root first." }
$v26 = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]MaxJobsPerDay20'"
if ($v26 -ne 1) { Stop-Here "$Main is not on database v2.6 yet. Run tools\set-max-jobs-20.ps1 first." }
function Find-Migration { Get-ChildItem $MigDir -Filter "*_DayOffRequests.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration DayOffRequests"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "DayOffRequests")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the DayOffRequests migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

# EF must only make the new table, the new column and their keys, indexes and CHECKs (our Sql steps are skipped)
$found = @()
Write-Host "EF operations in Up():"
foreach ($op in ([regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1)) {
    $name = ([regex]::Match($op, '^\w+')).Value
    if ($name -eq "Sql" -and $op.Contains("DayOffRequestsSql.")) { continue }
    $what = ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value
    $t = ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value
    if ($name -eq "CreateTable") { $t = $what }
    $line = "$name $t $what"
    Write-Host "  $line"
    $found += $line
}
$expected = @(
    "AddColumn PROVIDER_UNAVAILABLE_DATES REQUEST_ID",
    "CreateTable PROVIDER_DAY_OFF_REQUESTS PROVIDER_DAY_OFF_REQUESTS",
    "CreateIndex PROVIDER_UNAVAILABLE_DATES IX_PROVIDER_UNAVAILABLE_DATES_REQUEST_ID",
    "AddCheckConstraint PROVIDER_UNAVAILABLE_DATES CK_PROVIDER_UNAVAILABLE_DATES_SOURCE",
    "CreateIndex PROVIDER_DAY_OFF_REQUESTS IX_PROVIDER_DAY_OFF_REQUESTS_DECIDED_BY_ACTION_ID",
    "CreateIndex PROVIDER_DAY_OFF_REQUESTS IX_PROVIDER_DAY_OFF_REQUESTS_PROVIDER_ID_STATUS",
    "AddForeignKey PROVIDER_UNAVAILABLE_DATES FK_PROVIDER_UNAVAILABLE_DATES_PROVIDER_DAY_OFF_REQUESTS_REQUEST_ID")
$missing = @($expected | Where-Object { $_ -notin $found })
$extra = @($found | Where-Object { $_ -notin $expected })
$inside = @("CK_PROVIDER_DAY_OFF_REQUESTS_STATUS", "CK_PROVIDER_DAY_OFF_REQUESTS_DECISION",
            "FK_PROVIDER_DAY_OFF_REQUESTS_ADMIN_ACTIONS_DECIDED_BY_ACTION_ID", "FK_PROVIDER_DAY_OFF_REQUESTS_SERVICE_PROVIDERS_PROVIDER_ID")
$notInside = @($inside | Where-Object { -not $upPart.Contains($_) })
if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $notInside.Count -gt 0) {
    if ($missing.Count -gt 0) { Write-Host "Missing: $($missing -join '; ')" -ForegroundColor Red }
    if ($extra.Count -gt 0) { Write-Host "Not expected: $($extra -join '; ')" -ForegroundColor Red }
    if ($notInside.Count -gt 0) { Write-Host "Not in the new table: $($notInside -join '; ')" -ForegroundColor Red }
    Stop-Here "the migration $($mig.Name) is not only the day-off requests - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}

if (-not $m.Contains("DayOffRequestsSql.CreateAll")) {
    $nl = "`r`n"
    # Up: AFTER EF made the table and the column, the three objects that read them
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $upEnd = Block-End $m $upOpen
    $m = $m.Insert($upEnd, "$nl            // database v2.7: a Pending day-off request does not stop bookings; the bell shows requests and answers$nl" +
                           "            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.CreateAll[0]);   // fn_IsProviderAvailable$nl" +
                           "            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.CreateAll[1]);   // usp_CreateBooking$nl" +
                           "            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.CreateAll[2]);   // usp_GetNotifications$nl        ")
    # Down: FIRST the objects as they were (they must not read the table and column EF removes next)
    $downOpen = $m.IndexOf("{", $m.IndexOf($downSig))
    $m = $m.Insert($downOpen + 1, "$nl            // database v2.7 undone: the objects as they were before, then EF removes the table and the column$nl" +
                                  "            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.RestoreOld[0]);$nl" +
                                  "            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.RestoreOld[1]);$nl" +
                                  "            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.RestoreOld[2]);$nl")
    [System.IO.File]::WriteAllText($mig.FullName, $m)
    Write-Host "Added the DayOffRequestsSql steps to $($mig.Name)."
    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed after adding the SQL steps - send $($mig.Name) to Claude." }
}
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId makes the day-off requests and updates 3 database objects." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    $days = Scalar $Main "SELECT COUNT(*) FROM dbo.PROVIDER_UNAVAILABLE_DATES"
    Write-Host "Days off saved now: $days. They have no request, so they stay days off (nothing is changed)."

    # ------------------------------------------------------------ 3. backup
    Say "3. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.7-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
    [void](Invoke-Sql "master" "BACKUP DATABASE [$Main] TO DISK = N'$(Q $bak)' WITH COPY_ONLY, INIT, CHECKSUM;")
    Write-Host "Backup: $bak"

    # ------------------------------------------------------------ 4. SERVISYO
    Say "4. Update $Main"
    $answer = Read-Host "Type YES to update $Main now (anything else stops here)"
    if ($answer -cne "YES") { Write-Host "Not updated. Run this script again when you are ready."; exit 0 }
    Ef @("database", "update")
}

$result = (Invoke-Sql $Main @"
SELECT
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)') AS MIGRATION_APPLIED,
    (SELECT COUNT(*) FROM sys.tables WHERE name = N'PROVIDER_DAY_OFF_REQUESTS') AS REQUESTS_TABLE,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PROVIDER_UNAVAILABLE_DATES') AND name = N'REQUEST_ID') AS REQUEST_COLUMN,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N'CK_PROVIDER_DAY_OFF_REQUESTS_STATUS', N'CK_PROVIDER_DAY_OFF_REQUESTS_DECISION', N'CK_PROVIDER_UNAVAILABLE_DATES_SOURCE')) AS CHECKS,
    (SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.fn_IsProviderAvailable')) LIKE N'%PROVIDER_DAY_OFF_REQUESTS%' THEN 1 ELSE 0 END) AS FUNCTION_V27,
    (SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_CreateBooking')) LIKE N'%PROVIDER_DAY_OFF_REQUESTS%' THEN 1 ELSE 0 END) AS BOOKING_V27,
    (SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_GetNotifications')) LIKE N'%day-off-new%' THEN 1 ELSE 0 END) AS BELL_V27
"@).Tables[0]
$result | Format-Table -AutoSize | Out-Host
$row = $result.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.REQUESTS_TABLE -ne 1 -or $row.REQUEST_COLUMN -ne 1 -or $row.CHECKS -ne 3 -or
    $row.FUNCTION_V27 -ne 1 -or $row.BOOKING_V27 -ne 1 -or $row.BELL_V27 -ne 1) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.7 - providers request days off, an administrator approves or rejects." -ForegroundColor Green
Write-Host "Next: dotnet run --project $Web"
