# SERVISYO database v2.10 ("AuditStandard"): ONE format for every System Logs line.
#   - SYSTEM_LOGS.ACTION_TYPE: only INSERT, UPDATE, DELETE, LOGIN (CHECK CK_SYSTEM_LOGS_ACTION_TYPE)
#   - indexes IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT and IX_SYSTEM_LOGS_USER_ID_CREATED_AT (replace ACTION_TYPE + USER_ROLE)
#   - functions fn_AuditValue, fn_AuditActor; the audit triggers rewritten (users, services, bookings, payments) and
#     new ones (customers, providers, reviews, days off, day-off requests, name change requests): one line per changed
#     field, "{Role} {Name} updated {Entity} #{Id}: {field} 'old' -> 'new'."
#   (Servisyo.Web.Data.AuditStandardSql, added to the migration by this script)
#   The v2.9 REPORT lines are removed (the dashboard shows that number live now). Nothing else is deleted.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-audit-standard.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not changed until you type YES):
#   1. build + run all tests
#   2. create the EF migration "AuditStandard", check that EF only changes the SYSTEM_LOGS indexes and CHECK, and add
#      the AuditStandardSql steps to it
#   3. check SYSTEM_LOGS (only known action types) and show what changes
#   4. BACKUP of SERVISYO to Documents\SERVISYO-backups (COPY_ONLY)
#   5. only if you type YES: update SERVISYO, then check it - including a live test of the triggers inside a
#      transaction that is always ROLLED BACK (nothing is kept)
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

$Objects = @("fn_AuditValue", "fn_AuditActor", "trg_Users_Audit", "trg_Customers_Audit", "trg_Providers_Audit", "trg_Services_Audit",
             "trg_Bookings_Audit", "trg_PaymentsDtl_Audit", "trg_Reviews_Audit", "trg_UnavailableDates_Audit", "trg_DayOffRequests_Audit",
             "trg_NameChangeRequests_Audit")
$ObjectList = ($Objects | ForEach-Object { "N'$_'" }) -join ", "

# ---------------------------------------------------------------- 0. the files from the zip must be here, v2.9 first
if (-not (Test-Path "$Web\Data\AuditStandardSql.cs")) { Stop-Here "$Web\Data\AuditStandardSql.cs is missing - extract the zip in the repo root first." }
if (-not (Test-Path "$Web\Services\AuditLogService.cs")) { Stop-Here "$Web\Services\AuditLogService.cs is missing - extract the zip in the repo root first." }
if (Test-Path "$Web\Services\SystemLogService.cs") {
    Stop-Here "the old $Web\Services\SystemLogService.cs is still there (AuditLogService.cs replaces it). Delete it with: Remove-Item $Web\Services\SystemLogService.cs - then run this script again."
}
$v29 = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]InactiveAccounts'"
if ($v29 -ne 1) {
    if (Get-ChildItem $MigDir -Filter "*_InactiveAccounts.cs" -ErrorAction SilentlyContinue) {
        Stop-Here "$Main is not on database v2.9 yet. Run tools\add-inactive-accounts.ps1 first (it is safe with these files), then this script again."
    }
    # EF would build the v2.9 migration from the v2.10 model: v2.9 must be made from the v2.9 files first
    Stop-Here "$Main is not on database v2.9 yet, and the v2.9 migration was never made. Do step 1 first (the v2.9 zip and tools\add-inactive-accounts.ps1), BEFORE this zip - send this message to Claude for the exact commands."
}
function Find-Migration { Get-ChildItem $MigDir -Filter "*_AuditStandard.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration AuditStandard"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "AuditStandard")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the AuditStandard migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

# EF must only change the SYSTEM_LOGS indexes and CHECK (our Sql steps are skipped; HasTrigger adds no operation)
$found = @()
Write-Host "EF operations in Up():"
foreach ($op in ([regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1)) {
    $name = ([regex]::Match($op, '^\w+')).Value
    if ($name -eq "Sql" -and ($op.Contains("AuditStandardSql.") -or $op.StartsWith("Sql(sql)"))) { continue }
    $what = ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value
    $t = ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value
    $line = "$name $t $what"
    Write-Host "  $line"
    $found += $line
}
$expected = @(
    "DropIndex SYSTEM_LOGS IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE",
    "CreateIndex SYSTEM_LOGS IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT",
    "CreateIndex SYSTEM_LOGS IX_SYSTEM_LOGS_USER_ID_CREATED_AT",
    "AddCheckConstraint SYSTEM_LOGS CK_SYSTEM_LOGS_ACTION_TYPE")
$missing = @($expected | Where-Object { $_ -notin $found })
$extra = @($found | Where-Object { $_ -notin $expected })
$inside = @("[ACTION_TYPE] IN ('INSERT','UPDATE','DELETE','LOGIN')")
$notInside = @($inside | Where-Object { -not $upPart.Contains($_) })
if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $notInside.Count -gt 0) {
    if ($missing.Count -gt 0) { Write-Host "Missing: $($missing -join '; ')" -ForegroundColor Red }
    if ($extra.Count -gt 0) { Write-Host "Not expected: $($extra -join '; ')" -ForegroundColor Red }
    if ($notInside.Count -gt 0) { Write-Host "Not in the migration: $($notInside -join '; ')" -ForegroundColor Red }
    Stop-Here "the migration $($mig.Name) is not only the SYSTEM_LOGS indexes and CHECK - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}

if (-not $m.Contains("AuditStandardSql.CreateAll")) {
    $nl = "`r`n"
    # Up: FIRST the checks (and the v2.9 REPORT lines go) - the CHECK would refuse them
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $m = $m.Insert($upOpen + 1, "$nl            // database v2.10: stop if another object has one of the audit names; the v2.9 REPORT lines go$nl" +
                                "            migrationBuilder.Sql(Servisyo.Web.Data.AuditStandardSql.CheckBefore);$nl")
    # Up: LAST the functions and the triggers
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $upEnd = Block-End $m $upOpen
    $m = $m.Insert($upEnd, "$nl            // database v2.10: fn_AuditValue, fn_AuditActor and the audit triggers in the standard format$nl" +
                           "            foreach (var sql in Servisyo.Web.Data.AuditStandardSql.CreateAll) migrationBuilder.Sql(sql);$nl        ")
    # Down: FIRST the v2.9 triggers back (the new ones and the functions go)
    $downOpen = $m.IndexOf("{", $m.IndexOf($downSig))
    $m = $m.Insert($downOpen + 1, "$nl            // database v2.10 undone: the v2.9 audit triggers back, the new triggers and the functions removed$nl" +
                                  "            foreach (var sql in Servisyo.Web.Data.AuditStandardSql.RestoreOld) migrationBuilder.Sql(sql);$nl")
    [System.IO.File]::WriteAllText($mig.FullName, $m)
    Write-Host "Added the AuditStandardSql steps to $($mig.Name)."
    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed after adding the SQL steps - send $($mig.Name) to Claude." }
}
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId changes the SYSTEM_LOGS indexes and CHECK and installs the standard audit triggers." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    # ------------------------------------------------------------ 3. what changes
    Say "3. SYSTEM_LOGS before the update"
    $types = (Invoke-Sql $Main "SELECT ACTION_TYPE, COUNT(*) AS LINES FROM dbo.SYSTEM_LOGS GROUP BY ACTION_TYPE ORDER BY ACTION_TYPE").Tables[0]
    $types | Format-Table -AutoSize | Out-Host
    $unknown = @($types.Rows | Where-Object { $_.ACTION_TYPE -notin @("INSERT", "UPDATE", "DELETE", "LOGIN", "REPORT") })
    if ($unknown.Count -gt 0) { Stop-Here "SYSTEM_LOGS has action types other than INSERT, UPDATE, DELETE, LOGIN (and the v2.9 REPORT) - send the table above to Claude." }
    $report = @($types.Rows | Where-Object { $_.ACTION_TYPE -eq "REPORT" } | ForEach-Object { $_.LINES })
    if ($report.Count -gt 0) {
        Write-Host "$($report[0]) REPORT line(s) of the v2.9 inactive-account report will be REMOVED (the admin dashboard shows that number live now)." -ForegroundColor Yellow
    }
    $clash = (Invoke-Sql $Main @"
SELECT o.name, o.type_desc FROM sys.objects o
WHERE o.schema_id = SCHEMA_ID(N'dbo') AND o.name IN ($ObjectList)
  AND ISNULL(OBJECT_DEFINITION(o.object_id), N'') NOT LIKE N'%SERVISYO-DB-V2%'
  AND NOT (o.name IN (N'trg_Users_Audit', N'trg_Bookings_Audit', N'trg_Services_Audit')
           AND ISNULL(OBJECT_DEFINITION(o.object_id), N'') LIKE N'%servisyo[_]user[_]id%')
"@).Tables[0]
    if ($clash.Rows.Count -gt 0) {
        $clash | Format-Table -AutoSize | Out-Host
        Stop-Here "these objects already exist and were not made by SERVISYO - rename or drop them first (or send this to Claude)."
    }
    Write-Host "No other data is changed: existing log lines stay as they are; new lines use the standard format."

    # ------------------------------------------------------------ 4. backup
    Say "4. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.10-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
    [void](Invoke-Sql "master" "BACKUP DATABASE [$Main] TO DISK = N'$(Q $bak)' WITH COPY_ONLY, INIT, CHECKSUM;")
    Write-Host "Backup: $bak"

    # ------------------------------------------------------------ 5. SERVISYO
    Say "5. Update $Main"
    $answer = Read-Host "Type YES to update $Main now (anything else stops here)"
    if ($answer -cne "YES") { Write-Host "Not updated. Run this script again when you are ready."; exit 0 }
    Ef @("database", "update")
}

# ---------------------------------------------------------------- the checks
$result = (Invoke-Sql $Main @"
SELECT
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)') AS MIGRATION_APPLIED,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_SYSTEM_LOGS_ACTION_TYPE') AS ACTION_CHECK,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SYSTEM_LOGS')
        AND name IN (N'IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT', N'IX_SYSTEM_LOGS_USER_ID_CREATED_AT')) AS NEW_INDEXES,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SYSTEM_LOGS') AND name = N'IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE') AS OLD_INDEX,
    (SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ($ObjectList)
        AND OBJECT_DEFINITION(object_id) LIKE N'%SERVISYO-DB-V2 (v2.10%') AS V210_OBJECTS,
    (SELECT COUNT(*) FROM sys.triggers WHERE name IN ($ObjectList) AND is_disabled = 0) AS ENABLED_TRIGGERS,
    (SELECT COUNT(*) FROM dbo.SYSTEM_LOGS WHERE ACTION_TYPE NOT IN (N'INSERT', N'UPDATE', N'DELETE', N'LOGIN')) AS OTHER_LINES
"@).Tables[0]
$result | Format-Table -AutoSize | Out-Host
$row = $result.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.ACTION_CHECK -ne 1 -or $row.NEW_INDEXES -ne 2 -or $row.OLD_INDEX -ne 0 -or
    $row.V210_OBJECTS -ne 12 -or $row.ENABLED_TRIGGERS -ne 10 -or $row.OTHER_LINES -ne 0) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}

# a live test of the triggers: change one phone number, check the line, switch the audit off, change it again,
# check that nothing was written - all inside ONE transaction that is always rolled back (nothing is kept)
Say "Live test of the audit triggers (rolled back, nothing is kept)"
$live = (Invoke-Sql $Main @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
DECLARE @id INT = (SELECT TOP (1) USER_ID FROM dbo.USERS ORDER BY USER_ID);
DECLARE @before INT = ISNULL((SELECT MAX(SYSTEM_LOG_ID) FROM dbo.SYSTEM_LOGS), 0);
DECLARE @old NVARCHAR(11) = (SELECT PHONE_NUMBER FROM dbo.USERS WHERE USER_ID = @id);
DECLARE @new NVARCHAR(11) = CASE WHEN @old = N'09990000000' THEN N'09990000001' ELSE N'09990000000' END;
UPDATE dbo.USERS SET PHONE_NUMBER = @new WHERE USER_ID = @id;
DECLARE @line NVARCHAR(500) = (SELECT TOP (1) DESCRIPTION FROM dbo.SYSTEM_LOGS
                               WHERE SYSTEM_LOG_ID > @before AND ACTION_TYPE = N'UPDATE' AND ENTITY_NAME = N'User' AND ENTITY_ID = @id
                               ORDER BY SYSTEM_LOG_ID DESC);
DECLARE @expected NVARCHAR(500) = CONCAT(N'System updated User #', @id, N': phone ''', dbo.fn_AuditValue(@old), N''' -> ''', @new, N'''.');
EXEC sp_set_session_context @key = N'servisyo_audit_off', @value = 1;
UPDATE dbo.USERS SET PHONE_NUMBER = @old WHERE USER_ID = @id;
DECLARE @silent INT = (SELECT COUNT(*) FROM dbo.SYSTEM_LOGS WHERE SYSTEM_LOG_ID > @before);
EXEC sp_set_session_context @key = N'servisyo_audit_off', @value = NULL;
ROLLBACK TRANSACTION;
SELECT @line AS LOGGED_LINE,
       CASE WHEN @line = @expected THEN 1 ELSE 0 END AS FORMAT_OK,
       CASE WHEN @silent = 1 THEN 1 ELSE 0 END AS AUDIT_OFF_OK;
"@).Tables[0]
$live | Format-List | Out-Host
if ($live.Rows[0].FORMAT_OK -ne 1 -or $live.Rows[0].AUDIT_OFF_OK -ne 1) {
    Write-Host "The live test did not give the expected line - send the text above to Claude. (It was rolled back: nothing was kept.)" -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.10 - every System Logs line has the standard format, one line per changed field." -ForegroundColor Green
Write-Host "Next: dotnet run --project $Web, change a phone number or an address, then open Admin > System Logs (UPDATE pill)."
