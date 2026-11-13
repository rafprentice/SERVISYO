# SERVISYO database v2.9 ("InactiveAccounts"): the 1-year inactive-account clean-up.
#   - USERS.LAST_ACTIVITY_AT (DATETIME2, NOT NULL, default SYSUTCDATETIME()): the last sign-in (registration counts)
#   - USERS.INACTIVITY_WARNED_AT (DATETIME2, NULL): when the "will be deleted" email was sent
#   - index IX_USERS_LAST_ACTIVITY_AT (the daily search)
#   Existing accounts start from their last sign-in in SYSTEM_LOGS (LOGIN lines), or from their registration
#   (Servisyo.Web.Data.InactiveAccountsSql, added to the migration by this script). Nothing is deleted by this script.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-inactive-accounts.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not changed until you type YES):
#   1. build + run all tests
#   2. create the EF migration "InactiveAccounts", check that EF only adds the two columns and the index, and add
#      the InactiveAccountsSql step to it
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

# ---------------------------------------------------------------- 0. the files from the zip must be here, v2.8 first
if (-not (Test-Path "$Web\Data\InactiveAccountsSql.cs")) { Stop-Here "$Web\Data\InactiveAccountsSql.cs is missing - extract the zip in the repo root first." }
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Models\Entities.cs").Path)
if (-not $ctx.Contains("LastActivityAt")) { Stop-Here "Models\Entities.cs does not have LastActivityAt - extract the zip in the repo root first." }
$v28 = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]NameChangeRequests'"
if ($v28 -ne 1) { Stop-Here "$Main is not on database v2.8 yet. Run tools\add-name-change-requests.ps1 first." }
function Find-Migration { Get-ChildItem $MigDir -Filter "*_InactiveAccounts.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration InactiveAccounts"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "InactiveAccounts")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the InactiveAccounts migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

# EF must only add the two columns and the index (our Sql step is skipped)
$found = @()
Write-Host "EF operations in Up():"
foreach ($op in ([regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1)) {
    $name = ([regex]::Match($op, '^\w+')).Value
    if ($name -eq "Sql" -and $op.Contains("InactiveAccountsSql.")) { continue }
    $what = ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value
    $t = ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value
    $line = "$name $t $what"
    Write-Host "  $line"
    $found += $line
}
$expected = @(
    "AddColumn USERS LAST_ACTIVITY_AT",
    "AddColumn USERS INACTIVITY_WARNED_AT",
    "CreateIndex USERS IX_USERS_LAST_ACTIVITY_AT")
$missing = @($expected | Where-Object { $_ -notin $found })
$extra = @($found | Where-Object { $_ -notin $expected })
$inside = @("defaultValueSql: `"SYSUTCDATETIME()`"")
$notInside = @($inside | Where-Object { -not $upPart.Contains($_) })
if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $notInside.Count -gt 0) {
    if ($missing.Count -gt 0) { Write-Host "Missing: $($missing -join '; ')" -ForegroundColor Red }
    if ($extra.Count -gt 0) { Write-Host "Not expected: $($extra -join '; ')" -ForegroundColor Red }
    if ($notInside.Count -gt 0) { Write-Host "Not in the migration: $($notInside -join '; ')" -ForegroundColor Red }
    Stop-Here "the migration $($mig.Name) is not only the inactive-account columns - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}

if (-not $m.Contains("InactiveAccountsSql.InitLastActivity")) {
    $nl = "`r`n"
    # Up: AFTER EF added the column, each account starts from its real last sign-in
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $upEnd = Block-End $m $upOpen
    $m = $m.Insert($upEnd, "$nl            // database v2.9: each account starts from its last sign-in (SYSTEM_LOGS LOGIN), or its registration$nl" +
                           "            migrationBuilder.Sql(Servisyo.Web.Data.InactiveAccountsSql.InitLastActivity);$nl        ")
    [System.IO.File]::WriteAllText($mig.FullName, $m)
    Write-Host "Added the InactiveAccountsSql step to $($mig.Name)."
    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed after adding the SQL step - send $($mig.Name) to Claude." }
}
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId adds USERS.LAST_ACTIVITY_AT, USERS.INACTIVITY_WARNED_AT and their index." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    Write-Host "No data is deleted: two columns are added, and each account gets its last sign-in date."

    # ------------------------------------------------------------ 3. backup
    Say "3. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.9-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
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
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.USERS') AND name = N'LAST_ACTIVITY_AT' AND is_nullable = 0) AS LAST_ACTIVITY_COLUMN,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.USERS') AND name = N'INACTIVITY_WARNED_AT' AND is_nullable = 1) AS WARNED_COLUMN,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.USERS') AND name = N'IX_USERS_LAST_ACTIVITY_AT') AS ACTIVITY_INDEX,
    (SELECT COUNT(*) FROM dbo.USERS WHERE LAST_ACTIVITY_AT < CREATED_AT) AS BEFORE_REGISTRATION,
    (SELECT COUNT(*) FROM dbo.USERS WHERE ROLE <> N'Administrator' AND LAST_ACTIVITY_AT <= DATEADD(DAY, -335, SYSUTCDATETIME())) AS INACTIVE_335_DAYS
"@).Tables[0]
$result | Format-Table -AutoSize | Out-Host
$row = $result.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.LAST_ACTIVITY_COLUMN -ne 1 -or $row.WARNED_COLUMN -ne 1 -or $row.ACTIVITY_INDEX -ne 1 -or $row.BEFORE_REGISTRATION -ne 0) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.9. The daily clean-up starts in REPORT-ONLY mode (it changes nothing)." -ForegroundColor Green
Write-Host "INACTIVE_335_DAYS = accounts that would get the warning email once deleting is switched on."
Write-Host "Next: dotnet run --project $Web"
