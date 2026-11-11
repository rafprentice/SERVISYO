# SERVISYO database v2.6 ("MaxJobsPerDay20"): a provider accepts at most 20 jobs a day (was 50).
#   - CK_PROVIDERS_LIMITS: MAX_JOBS_PER_DAY BETWEEN 1 AND 20 (EF changes this by itself)
#   - usp_RegisterProvider: "1 to 20" (Servisyo.Web.Data.MaxJobsSql, added to the migration by this script)
#   - providers saved with more than 20 are set to 20 (the new CHECK cannot be added otherwise)
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\set-max-jobs-20.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not changed until you type YES):
#   1. build + run all tests
#   2. create the EF migration "MaxJobsPerDay20", check that EF only changes CK_PROVIDERS_LIMITS, and add the
#      MaxJobsSql steps to it
#   3. show the providers that are above 20 now (they will become 20)
#   4. BACKUP of SERVISYO to Documents\SERVISYO-backups (COPY_ONLY)
#   5. only if you type YES: update SERVISYO, then check it
# Safe to run again: anything already done is skipped.
$ErrorActionPreference = "Stop"
$Server = "(localdb)\MSSQLLocalDB"
$Main = "SERVISYO"
$Web = "src\Servisyo.Web"
$MigDir = "$Web\Migrations"
$Check = "CK_PROVIDERS_LIMITS"

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

# ---------------------------------------------------------------- 0. the files from the zip must be here, v2.5 first
if (-not (Test-Path "$Web\Data\MaxJobsSql.cs")) { Stop-Here "$Web\Data\MaxJobsSql.cs is missing - extract the zip in the repo root first." }
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Data\ServisyoDbContext.cs").Path)
if (-not $ctx.Contains("BETWEEN 1 AND 20")) { Stop-Here "ServisyoDbContext.cs does not have the 20-job CHECK - extract the zip in the repo root first." }
$v25 = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]OneDayOffPerDate'"
if ($v25 -ne 1) { Stop-Here "$Main is not on database v2.5 yet. Run tools\add-extra-day-off.ps1 and tools\add-one-day-off-per-date.ps1 first." }
function Find-Migration { Get-ChildItem $MigDir -Filter "*_MaxJobsPerDay20.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration MaxJobsPerDay20"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "MaxJobsPerDay20")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the MaxJobsPerDay20 migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

# EF must only replace CK_PROVIDERS_LIMITS (our own Sql steps are recognised and skipped)
$found = @()
Write-Host "EF operations in Up():"
foreach ($op in ([regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1)) {
    $name = ([regex]::Match($op, '^\w+')).Value
    if ($name -eq "Sql" -and $op.Contains("MaxJobsSql.")) { continue }
    $t = ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value
    $what = ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value
    $line = "$name $t $what"
    Write-Host "  $line"
    $found += $line
}
$expected = @("DropCheckConstraint SERVICE_PROVIDERS $Check", "AddCheckConstraint SERVICE_PROVIDERS $Check")
if ($found.Count -ne 2 -or ($found | Where-Object { $_ -notin $expected }) -or -not $upPart.Contains("BETWEEN 1 AND 20")) {
    Stop-Here "the migration $($mig.Name) is not only the new $Check - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}

if (-not $m.Contains("MaxJobsSql.CapExisting")) {
    $nl = "`r`n"
    # Up: lower the providers above 20 BEFORE the new CHECK, then the procedure
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $m = $m.Insert($upOpen + 1, "$nl            // database v2.6: providers above 20 become 20 first, otherwise the new CHECK cannot be added$nl" +
                                "            migrationBuilder.Sql(Servisyo.Web.Data.MaxJobsSql.CapExisting);$nl")
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $upEnd = Block-End $m $upOpen
    $m = $m.Insert($upEnd, "$nl            // database v2.6: usp_RegisterProvider allows 1 to 20 jobs a day$nl" +
                           "            migrationBuilder.Sql(Servisyo.Web.Data.MaxJobsSql.RegisterProvider20);$nl        ")
    # Down: the v2.3 procedure (1 to 50) after EF has put the old CHECK back
    $downOpen = $m.IndexOf("{", $m.IndexOf($downSig))
    $downEnd = Block-End $m $downOpen
    $m = $m.Insert($downEnd, "$nl            migrationBuilder.Sql(Servisyo.Web.Data.MaxJobsSql.RegisterProviderV23);$nl        ")
    [System.IO.File]::WriteAllText($mig.FullName, $m)
    Write-Host "Added the MaxJobsSql steps to $($mig.Name)."
    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed after adding the SQL steps - send $($mig.Name) to Claude." }
}
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId changes $Check to 1-20 and updates usp_RegisterProvider." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    # ------------------------------------------------------------ 3. providers above 20
    Say "3. Providers that accept more than 20 jobs a day now"
    $above = (Invoke-Sql $Main @"
SELECT p.PROVIDER_ID, u.FIRST_NAME + N' ' + u.LAST_NAME AS PROVIDER, p.BUSINESS_NAME, p.MAX_JOBS_PER_DAY AS NOW_, 20 AS AFTER_
FROM dbo.SERVICE_PROVIDERS p JOIN dbo.USERS u ON u.USER_ID = p.USER_ID
WHERE p.MAX_JOBS_PER_DAY > 20
ORDER BY p.PROVIDER_ID;
"@).Tables[0]
    if ($above.Rows.Count -eq 0) { Write-Host "None." -ForegroundColor Green }
    else { $above | Format-Table -AutoSize | Out-Host; Write-Host "These $($above.Rows.Count) provider(s) will be set to 20 jobs a day." }

    # ------------------------------------------------------------ 4. backup
    Say "4. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.6-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
    [void](Invoke-Sql "master" "BACKUP DATABASE [$Main] TO DISK = N'$(Q $bak)' WITH COPY_ONLY, INIT, CHECKSUM;")
    Write-Host "Backup: $bak"

    # ------------------------------------------------------------ 5. SERVISYO
    Say "5. Update $Main"
    $answer = Read-Host "Type YES to update $Main now (anything else stops here)"
    if ($answer -cne "YES") { Write-Host "Not updated. Run this script again when you are ready."; exit 0 }
    Ef @("database", "update")
}

$result = (Invoke-Sql $Main @"
SELECT
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)') AS MIGRATION_APPLIED,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'$Check' AND definition LIKE N'%(20)%') AS CHECK_IS_20,
    (SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_RegisterProvider')) LIKE N'%jobs per day from 1 to 20.%' THEN 1 ELSE 0 END) AS PROCEDURE_IS_20,
    (SELECT COUNT(*) FROM dbo.SERVICE_PROVIDERS WHERE MAX_JOBS_PER_DAY > 20) AS PROVIDERS_ABOVE_20
"@).Tables[0]
$result | Format-Table -AutoSize | Out-Host
$row = $result.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.CHECK_IS_20 -ne 1 -or $row.PROCEDURE_IS_20 -ne 1 -or $row.PROVIDERS_ABOVE_20 -ne 0) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.6 - at most 20 jobs a day." -ForegroundColor Green
Write-Host "Next: dotnet run --project $Web"
