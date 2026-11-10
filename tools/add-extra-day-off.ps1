# SERVISYO database v2.4 ("ExtraDayOff"): an extra day off approved by an administrator.
#   PROVIDER_UNAVAILABLE_DATES gets ONE new nullable column, APPROVED_BY_ACTION_ID, with a FOREIGN KEY to
#   ADMIN_ACTIONS (the row that says who approved the day, when and why). Nothing else changes; no data is changed.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-extra-day-off.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not touched until the very end, and only if you type YES):
#   1. build + run all tests
#   2. create the EF migration "ExtraDayOff" and check that it ONLY adds that column, its index and its foreign key
#   3. BACKUP of SERVISYO to Documents\SERVISYO-backups (COPY_ONLY: your normal backups are not affected)
#   4. only if you type YES: update SERVISYO, then check it
# Safe to run again: anything already done is skipped.
$ErrorActionPreference = "Stop"
$Server = "(localdb)\MSSQLLocalDB"
$Main = "SERVISYO"
$Web = "src\Servisyo.Web"
$MigDir = "$Web\Migrations"
$Column = "APPROVED_BY_ACTION_ID"
$Table = "PROVIDER_UNAVAILABLE_DATES"

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

# ---------------------------------------------------------------- 0. the files from the zip must be here
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Data\ServisyoDbContext.cs").Path)
if (-not $ctx.Contains("ApprovedByActionId")) { Stop-Here "ServisyoDbContext.cs has no ApprovedByActionId - extract the zip in the repo root first." }
if (-not (Get-ChildItem $MigDir -Filter "*_DerivedData.cs" -ErrorAction SilentlyContinue)) {
    Stop-Here "The DerivedData migration (database v2.3) is missing. It must come first (tools\add-derived-data.ps1)."
}
function Find-Migration { Get-ChildItem $MigDir -Filter "*_ExtraDayOff.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration ExtraDayOff"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "ExtraDayOff")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the ExtraDayOff migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

# exactly these three operations, all on PROVIDER_UNAVAILABLE_DATES; anything else means the model and the
# database disagree about something else, and that must be looked at first
$expected = @(
    "AddColumn $Table $Column",
    "CreateIndex $Table IX_$($Table)_$Column",
    "AddForeignKey $Table FK_$($Table)_ADMIN_ACTIONS_$Column"
)
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
$extra = $found | Where-Object { $_ -notin $expected }
$missing = $expected | Where-Object { $_ -notin $found }
if ($extra -or $missing) {
    if ($extra) { Write-Host "Not expected: $($extra -join '; ')" -ForegroundColor Red }
    if ($missing) { Write-Host "Missing: $($missing -join '; ')" -ForegroundColor Red }
    Stop-Here "the migration $($mig.Name) is not only the new column - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}
if (-not $upPart.Contains("nullable: true")) { Stop-Here "the new column is not nullable in $($mig.Name) - send that file to Claude." }
if ($upPart.Contains("ReferentialAction.Cascade")) { Stop-Here "the new foreign key would cascade deletes - send $($mig.Name) to Claude." }
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId only adds $Table.$Column (nullable), its index and its foreign key to ADMIN_ACTIONS." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    # ------------------------------------------------------------ 3. backup
    Say "3. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.4-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
    [void](Invoke-Sql "master" "BACKUP DATABASE [$Main] TO DISK = N'$(Q $bak)' WITH COPY_ONLY, INIT, CHECKSUM;")
    Write-Host "Backup: $bak"

    # ------------------------------------------------------------ 4. SERVISYO
    Say "4. Update $Main"
    Write-Host "This only adds one empty column (no data is changed). Your backup: $bak"
    $answer = Read-Host "Type YES to update $Main now (anything else stops here)"
    if ($answer -cne "YES") { Write-Host "Not updated. Run this script again when you are ready."; exit 0 }
    Ef @("database", "update")
}

$check = (Invoke-Sql $Main @"
SELECT
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)') AS MIGRATION_APPLIED,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.$Table') AND name = N'$Column' AND is_nullable = 1) AS COLUMN_THERE,
    (SELECT COUNT(*) FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = fk.object_id
      JOIN sys.columns c ON c.object_id = fc.parent_object_id AND c.column_id = fc.parent_column_id
     WHERE fk.parent_object_id = OBJECT_ID(N'dbo.$Table') AND fk.referenced_object_id = OBJECT_ID(N'dbo.ADMIN_ACTIONS')
       AND c.name = N'$Column' AND fk.delete_referential_action = 0) AS FOREIGN_KEY_THERE,
    (SELECT COUNT(*) FROM dbo.$Table) AS DAYS_OFF_ROWS
"@).Tables[0]
$check | Format-Table -AutoSize | Out-Host
$row = $check.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.COLUMN_THERE -ne 1 -or $row.FOREIGN_KEY_THERE -ne 1) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.4 (extra days off approved by an administrator)." -ForegroundColor Green
Write-Host "Next: dotnet run --project $Web"
