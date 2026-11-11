# SERVISYO database v2.5 ("OneDayOffPerDate"): only ONE provider can be off on any date.
#   A UNIQUE index on PROVIDER_UNAVAILABLE_DATES (UNAVAILABLE_DATE): the database itself refuses a second provider's
#   day off on a date that is already taken (first come, first served), even if two providers save at the same moment.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-one-day-off-per-date.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not changed until you type YES):
#   1. build + run all tests
#   2. create the EF migration "OneDayOffPerDate" and check that it ONLY adds that unique index
#   3. look for dates that more than one provider already has off (the index cannot be created while they exist)
#      and show them - for each such date the FIRST one saved is kept, the later ones would be removed
#   4. BACKUP of SERVISYO to Documents\SERVISYO-backups (COPY_ONLY: your normal backups are not affected)
#   5. only if you type YES: remove those later duplicates (if any) and update SERVISYO - in that order - then check it
#   If you would rather choose yourself which provider keeps a date, answer NO, remove the other day off in the app
#   (the provider's Availability page) and run the script again.
# Safe to run again: anything already done is skipped.
$ErrorActionPreference = "Stop"
$Server = "(localdb)\MSSQLLocalDB"
$Main = "SERVISYO"
$Web = "src\Servisyo.Web"
$MigDir = "$Web\Migrations"
$Table = "PROVIDER_UNAVAILABLE_DATES"
$Index = "IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE"

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

# ---------------------------------------------------------------- 0. the files from the zip must be here, v2.4 first
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Data\ServisyoDbContext.cs").Path)
if (-not $ctx.Contains("e.HasIndex(x => x.UnavailableDate).IsUnique();")) { Stop-Here "ServisyoDbContext.cs does not have the new unique index - extract the zip in the repo root first." }
if (-not (Get-ChildItem $MigDir -Filter "*_ExtraDayOff.cs" -ErrorAction SilentlyContinue)) {
    Stop-Here "The ExtraDayOff migration (database v2.4) is missing. Run tools\add-extra-day-off.ps1 first."
}
$v24 = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]ExtraDayOff'"
if ($v24 -ne 1) { Stop-Here "$Main is not on database v2.4 yet. Run tools\add-extra-day-off.ps1 first." }
function Find-Migration { Get-ChildItem $MigDir -Filter "*_OneDayOffPerDate.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration OneDayOffPerDate"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "OneDayOffPerDate")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the OneDayOffPerDate migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

$found = @()
Write-Host "EF operations in Up():"
foreach ($op in ([regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1)) {
    $name = ([regex]::Match($op, '^\w+')).Value
    $t = ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value
    $what = ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value
    $line = "$name $t $what"
    Write-Host "  $line"
    $found += $line
}
if ($found.Count -ne 1 -or $found[0] -ne "CreateIndex $Table $Index" -or -not $upPart.Contains("unique: true") -or -not $upPart.Contains('column: "UNAVAILABLE_DATE"')) {
    Stop-Here "the migration $($mig.Name) is not only the unique index on UNAVAILABLE_DATE - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId only adds the UNIQUE index $Index." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    # ------------------------------------------------------------ 3. dates more than one provider has off
    Say "3. Dates that more than one provider already has off"
    # for each date the FIRST row saved (the lowest UNAVAILABLE_ID) is kept: first come, first served
    $dupSql = @"
SELECT d.UNAVAILABLE_ID, CONVERT(char(10), d.UNAVAILABLE_DATE, 120) AS DAY_OFF,
       u.FIRST_NAME + N' ' + u.LAST_NAME AS PROVIDER, ISNULL(d.REASON, N'') AS REASON,
       CASE WHEN d.APPROVED_BY_ACTION_ID IS NULL THEN N'' ELSE N'admin-approved' END AS NOTE,
       CASE WHEN d.UNAVAILABLE_ID = MIN(d.UNAVAILABLE_ID) OVER (PARTITION BY d.UNAVAILABLE_DATE) THEN N'KEEP (first)' ELSE N'REMOVE' END AS PLAN_
FROM dbo.$Table d
JOIN dbo.SERVICE_PROVIDERS p ON p.PROVIDER_ID = d.PROVIDER_ID
JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
WHERE d.UNAVAILABLE_DATE IN (SELECT UNAVAILABLE_DATE FROM dbo.$Table GROUP BY UNAVAILABLE_DATE HAVING COUNT(*) > 1)
ORDER BY d.UNAVAILABLE_DATE, d.UNAVAILABLE_ID;
"@
    $dups = (Invoke-Sql $Main $dupSql).Tables[0]
    $toRemove = @($dups.Rows | Where-Object { $_.PLAN_ -eq "REMOVE" }).Count
    if ($dups.Rows.Count -eq 0) { Write-Host "None - every date belongs to at most one provider." -ForegroundColor Green }
    else {
        $dups | Format-Table -AutoSize | Out-Host
        Write-Host "These dates are off for more than one provider. The unique index cannot be added while they exist."
        Write-Host "With YES below, the $toRemove row(s) marked REMOVE are deleted (each date keeps the provider who saved it first)."
        Write-Host "Their admin log entries (ADMIN_ACTIONS) are not touched. If you want to choose differently, answer NO,"
        Write-Host "remove the day you do not want on that provider's Availability page, and run this script again."
    }

    # ------------------------------------------------------------ 4. backup
    Say "4. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.5-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
    [void](Invoke-Sql "master" "BACKUP DATABASE [$Main] TO DISK = N'$(Q $bak)' WITH COPY_ONLY, INIT, CHECKSUM;")
    Write-Host "Backup: $bak"

    # ------------------------------------------------------------ 5. SERVISYO
    Say "5. Update $Main"
    if ($toRemove -gt 0) { Write-Host "This deletes the $toRemove duplicate day(s) off marked REMOVE above, then adds the unique index." }
    else { Write-Host "This only adds the unique index (no data is changed)." }
    Write-Host "Your backup: $bak"
    $answer = Read-Host "Type YES to update $Main now (anything else stops here)"
    if ($answer -cne "YES") { Write-Host "Not updated. Run this script again when you are ready."; exit 0 }

    if ($toRemove -gt 0) {
        $deleted = Scalar $Main @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DELETE d FROM dbo.$Table d
WHERE EXISTS (SELECT 1 FROM dbo.$Table e WHERE e.UNAVAILABLE_DATE = d.UNAVAILABLE_DATE AND e.UNAVAILABLE_ID < d.UNAVAILABLE_ID);
DECLARE @n int = @@ROWCOUNT;
COMMIT;
SELECT @n;
"@
        Write-Host "Removed $deleted duplicate day(s) off."
    }
    Ef @("database", "update")
}

$check = (Invoke-Sql $Main @"
SELECT
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)') AS MIGRATION_APPLIED,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.$Table') AND name = N'$Index' AND is_unique = 1) AS UNIQUE_INDEX_THERE,
    (SELECT COUNT(*) FROM (SELECT UNAVAILABLE_DATE FROM dbo.$Table GROUP BY UNAVAILABLE_DATE HAVING COUNT(*) > 1) x) AS SHARED_DATES_LEFT,
    (SELECT COUNT(*) FROM dbo.$Table) AS DAYS_OFF_ROWS
"@).Tables[0]
$check | Format-Table -AutoSize | Out-Host
$row = $check.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.UNIQUE_INDEX_THERE -ne 1 -or $row.SHARED_DATES_LEFT -ne 0) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.5 - only one provider can be off on any date." -ForegroundColor Green
Write-Host "Next: dotnet run --project $Web"
