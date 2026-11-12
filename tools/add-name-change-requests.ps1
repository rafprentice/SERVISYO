# SERVISYO database v2.8 ("NameChangeRequests"): an approved provider ASKS an administrator to change their name.
#   - new table PROVIDER_NAME_CHANGE_REQUESTS (EF makes it by itself), with:
#       CHECKs on STATUS (Pending / Approved / Rejected) and on the decision (none while Pending, required after),
#       a FOREIGN KEY to the ADMIN_ACTIONS row of the decision (no cascade, UNIQUE),
#       a filtered UNIQUE index: only ONE Pending request per provider
#   - usp_GetNotifications: the provider sees the answer, the administrator sees the waiting requests
#   (Servisyo.Web.Data.NameChangeRequestsSql, added to the migration by this script)
#   No data is changed: the table starts empty.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-name-change-requests.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not changed until you type YES):
#   1. build + run all tests
#   2. create the EF migration "NameChangeRequests", check that EF only makes the table and its keys, indexes and
#      CHECKs, and add the NameChangeRequestsSql step to it
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

# ---------------------------------------------------------------- 0. the files from the zip must be here, v2.7 first
if (-not (Test-Path "$Web\Data\NameChangeRequestsSql.cs")) { Stop-Here "$Web\Data\NameChangeRequestsSql.cs is missing - extract the zip in the repo root first." }
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Data\ServisyoDbContext.cs").Path)
if (-not $ctx.Contains("PROVIDER_NAME_CHANGE_REQUESTS")) { Stop-Here "ServisyoDbContext.cs does not have the name change requests - extract the zip in the repo root first." }
$v27 = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]DayOffRequests'"
if ($v27 -ne 1) { Stop-Here "$Main is not on database v2.7 yet. Run tools\add-day-off-requests.ps1 first." }
function Find-Migration { Get-ChildItem $MigDir -Filter "*_NameChangeRequests.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration NameChangeRequests"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "NameChangeRequests")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the NameChangeRequests migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

# EF must only make the new table and its keys, indexes and CHECKs (our Sql step is skipped)
$found = @()
Write-Host "EF operations in Up():"
foreach ($op in ([regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1)) {
    $name = ([regex]::Match($op, '^\w+')).Value
    if ($name -eq "Sql" -and $op.Contains("NameChangeRequestsSql.")) { continue }
    $what = ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value
    $t = ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value
    if ($name -eq "CreateTable") { $t = $what }
    $line = "$name $t $what"
    Write-Host "  $line"
    $found += $line
}
$expected = @(
    "CreateTable PROVIDER_NAME_CHANGE_REQUESTS PROVIDER_NAME_CHANGE_REQUESTS",
    "CreateIndex PROVIDER_NAME_CHANGE_REQUESTS IX_PROVIDER_NAME_CHANGE_REQUESTS_DECIDED_BY_ACTION_ID",
    "CreateIndex PROVIDER_NAME_CHANGE_REQUESTS IX_PROVIDER_NAME_CHANGE_REQUESTS_PROVIDER_ID_STATUS",
    "CreateIndex PROVIDER_NAME_CHANGE_REQUESTS UX_PROVIDER_NAME_CHANGE_REQUESTS_ONE_PENDING")
$missing = @($expected | Where-Object { $_ -notin $found })
$extra = @($found | Where-Object { $_ -notin $expected })
$inside = @("CK_PROVIDER_NAME_CHANGE_REQUESTS_STATUS", "CK_PROVIDER_NAME_CHANGE_REQUESTS_DECISION",
            "FK_PROVIDER_NAME_CHANGE_REQUESTS_ADMIN_ACTIONS_DECIDED_BY_ACTION_ID", "FK_PROVIDER_NAME_CHANGE_REQUESTS_SERVICE_PROVIDERS_PROVIDER_ID",
            "filter: `"[STATUS] = 'Pending'`"")
$notInside = @($inside | Where-Object { -not $upPart.Contains($_) })
if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $notInside.Count -gt 0) {
    if ($missing.Count -gt 0) { Write-Host "Missing: $($missing -join '; ')" -ForegroundColor Red }
    if ($extra.Count -gt 0) { Write-Host "Not expected: $($extra -join '; ')" -ForegroundColor Red }
    if ($notInside.Count -gt 0) { Write-Host "Not in the migration: $($notInside -join '; ')" -ForegroundColor Red }
    Stop-Here "the migration $($mig.Name) is not only the name change requests - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}

if (-not $m.Contains("NameChangeRequestsSql.CreateAll")) {
    $nl = "`r`n"
    # Up: AFTER EF made the table, the procedure that reads it
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $upEnd = Block-End $m $upOpen
    $m = $m.Insert($upEnd, "$nl            // database v2.8: the bell shows name change requests (administrators) and their answers (providers)$nl" +
                           "            migrationBuilder.Sql(Servisyo.Web.Data.NameChangeRequestsSql.CreateAll[0]);   // usp_GetNotifications$nl        ")
    # Down: FIRST the procedure as it was (it must not read the table EF removes next)
    $downOpen = $m.IndexOf("{", $m.IndexOf($downSig))
    $m = $m.Insert($downOpen + 1, "$nl            // database v2.8 undone: usp_GetNotifications as it was in v2.7, then EF removes the table$nl" +
                                  "            migrationBuilder.Sql(Servisyo.Web.Data.NameChangeRequestsSql.RestoreOld[0]);$nl")
    [System.IO.File]::WriteAllText($mig.FullName, $m)
    Write-Host "Added the NameChangeRequestsSql step to $($mig.Name)."
    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed after adding the SQL step - send $($mig.Name) to Claude." }
}
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId makes the name change requests table and updates usp_GetNotifications." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    Write-Host "The new table starts empty; no existing data is changed."

    # ------------------------------------------------------------ 3. backup
    Say "3. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.8-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
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
    (SELECT COUNT(*) FROM sys.tables WHERE name = N'PROVIDER_NAME_CHANGE_REQUESTS') AS REQUESTS_TABLE,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N'CK_PROVIDER_NAME_CHANGE_REQUESTS_STATUS', N'CK_PROVIDER_NAME_CHANGE_REQUESTS_DECISION')) AS CHECKS,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N'UX_PROVIDER_NAME_CHANGE_REQUESTS_ONE_PENDING' AND is_unique = 1 AND has_filter = 1) AS ONE_PENDING_INDEX,
    (SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_GetNotifications')) LIKE N'%name-change-new%' THEN 1 ELSE 0 END) AS BELL_V28
"@).Tables[0]
$result | Format-Table -AutoSize | Out-Host
$row = $result.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.REQUESTS_TABLE -ne 1 -or $row.CHECKS -ne 2 -or $row.ONE_PENDING_INDEX -ne 1 -or $row.BELL_V28 -ne 1) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.8 - approved providers request a name change, an administrator approves or rejects." -ForegroundColor Green
Write-Host "Next: dotnet run --project $Web"
