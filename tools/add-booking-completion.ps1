# SERVISYO database v2.11 ("BookingCompletionProof"): a provider completes a job WITH proof.
#   - BOOKINGS.COMPLETION_MESSAGE (NVARCHAR(1000), NULL), COMPLETION_PHOTO_URL (NVARCHAR(500), NULL), COMPLETED_AT (DATETIME2, NULL)
#   - CHECK CK_BOOKINGS_COMPLETION: the proof is all or nothing, only on a Completed booking, with a message that is not blank
#   - trg_Bookings_Audit v2.11: completing WITH proof writes one System Logs line ("... (proof photo and completion message saved).")
#   (Servisyo.Web.Data.BookingCompletionSql, added to the migration by this script)
#   Existing bookings are not changed: jobs completed before v2.11 simply have no proof.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-booking-completion.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not changed until you type YES):
#   1. build + run all tests
#   2. create the EF migration "BookingCompletionProof", check that EF only adds the three columns and the CHECK, and
#      add the BookingCompletionSql steps to it
#   3. BACKUP of SERVISYO to Documents\SERVISYO-backups (COPY_ONLY)
#   4. only if you type YES: update SERVISYO, then check it - including a live test inside a transaction that is
#      always ROLLED BACK (nothing is kept)
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

# ---------------------------------------------------------------- 0. the files from the zip must be here, v2.10 first
if (-not (Test-Path "$Web\Data\BookingCompletionSql.cs")) { Stop-Here "$Web\Data\BookingCompletionSql.cs is missing - extract the zip in the repo root first." }
$ent = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Models\Entities.cs").Path)
if (-not $ent.Contains("CompletionPhotoUrl")) { Stop-Here "Models\Entities.cs does not have CompletionPhotoUrl - extract the zip in the repo root first." }
$v210 = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]AuditStandard'"
if ($v210 -ne 1) { Stop-Here "$Main is not on database v2.10 yet. Run tools\add-audit-standard.ps1 first." }
function Find-Migration { Get-ChildItem $MigDir -Filter "*_BookingCompletionProof.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration BookingCompletionProof"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "BookingCompletionProof")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the BookingCompletionProof migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

# EF must only add the three columns and the CHECK (our Sql steps are skipped)
$found = @()
Write-Host "EF operations in Up():"
foreach ($op in ([regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1)) {
    $name = ([regex]::Match($op, '^\w+')).Value
    if ($name -eq "Sql" -and ($op.Contains("BookingCompletionSql.") -or $op.StartsWith("Sql(sql)"))) { continue }
    $what = ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value
    $t = ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value
    $line = "$name $t $what"
    Write-Host "  $line"
    $found += $line
}
$expected = @(
    "AddColumn BOOKINGS COMPLETED_AT",
    "AddColumn BOOKINGS COMPLETION_MESSAGE",
    "AddColumn BOOKINGS COMPLETION_PHOTO_URL",
    "AddCheckConstraint BOOKINGS CK_BOOKINGS_COMPLETION")
$missing = @($expected | Where-Object { $_ -notin $found })
$extra = @($found | Where-Object { $_ -notin $expected })
$inside = @("maxLength: 1000", "maxLength: 500", "LEN([COMPLETION_MESSAGE]) > 0")
$notInside = @($inside | Where-Object { -not $upPart.Contains($_) })
if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $notInside.Count -gt 0) {
    if ($missing.Count -gt 0) { Write-Host "Missing: $($missing -join '; ')" -ForegroundColor Red }
    if ($extra.Count -gt 0) { Write-Host "Not expected: $($extra -join '; ')" -ForegroundColor Red }
    if ($notInside.Count -gt 0) { Write-Host "Not in the migration: $($notInside -join '; ')" -ForegroundColor Red }
    Stop-Here "the migration $($mig.Name) is not only the completion proof columns - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}

if (-not $m.Contains("BookingCompletionSql.CreateAll")) {
    $nl = "`r`n"
    # Up: LAST the v2.11 audit trigger (it reads the new columns)
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $upEnd = Block-End $m $upOpen
    $m = $m.Insert($upEnd, "$nl            // database v2.11: trg_Bookings_Audit also knows the job's proof$nl" +
                           "            foreach (var sql in Servisyo.Web.Data.BookingCompletionSql.CreateAll) migrationBuilder.Sql(sql);$nl        ")
    # Down: FIRST the v2.10 trigger back (before EF removes the columns)
    $downOpen = $m.IndexOf("{", $m.IndexOf($downSig))
    $m = $m.Insert($downOpen + 1, "$nl            // database v2.11 undone: trg_Bookings_Audit as it was in v2.10, then EF removes the columns$nl" +
                                  "            foreach (var sql in Servisyo.Web.Data.BookingCompletionSql.RestoreOld) migrationBuilder.Sql(sql);$nl")
    [System.IO.File]::WriteAllText($mig.FullName, $m)
    Write-Host "Added the BookingCompletionSql steps to $($mig.Name)."
    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed after adding the SQL steps - send $($mig.Name) to Claude." }
}
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId adds the completion proof to BOOKINGS." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    Write-Host "No data is changed: three empty columns are added to BOOKINGS."

    # ------------------------------------------------------------ 3. backup
    Say "3. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.11-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
    [void](Invoke-Sql "master" "BACKUP DATABASE [$Main] TO DISK = N'$(Q $bak)' WITH COPY_ONLY, INIT, CHECKSUM;")
    Write-Host "Backup: $bak"

    # ------------------------------------------------------------ 4. SERVISYO
    Say "4. Update $Main"
    $answer = Read-Host "Type YES to update $Main now (anything else stops here)"
    if ($answer -cne "YES") { Write-Host "Not updated. Run this script again when you are ready."; exit 0 }
    Ef @("database", "update")
}

# ---------------------------------------------------------------- the checks
$result = (Invoke-Sql $Main @"
SELECT
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)') AS MIGRATION_APPLIED,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.BOOKINGS') AND is_nullable = 1 AND (
        (name = N'COMPLETION_MESSAGE' AND max_length = 2000) OR (name = N'COMPLETION_PHOTO_URL' AND max_length = 1000)
        OR (name = N'COMPLETED_AT' AND system_type_id = TYPE_ID(N'datetime2')))) AS PROOF_COLUMNS,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_BOOKINGS_COMPLETION') AS PROOF_CHECK,
    (SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.trg_Bookings_Audit')) LIKE N'%WITH[_]PROOF%' THEN 1 ELSE 0 END) AS AUDIT_V211
"@).Tables[0]
$result | Format-Table -AutoSize | Out-Host
$row = $result.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.PROOF_COLUMNS -ne 3 -or $row.PROOF_CHECK -ne 1 -or $row.AUDIT_V211 -ne 1) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}

# a live test on your own data, inside ONE transaction that is always rolled back (nothing is kept):
#   1. a booking that is not Completed is refused proof (the CHECK)
#   2. the newest In Progress booking, completed with proof, writes exactly ONE System Logs line
Say "Live test (rolled back, nothing is kept)"
$live = (Invoke-Sql $Main @"
SET NOCOUNT ON;
DECLARE @refused INT = NULL, @bid INT = NULL, @lines INT = NULL, @line NVARCHAR(500) = NULL, @before INT, @open INT;
BEGIN TRANSACTION;
BEGIN TRY
    SET @open = (SELECT TOP (1) BOOKING_ID FROM dbo.BOOKINGS WHERE STATUS <> N'Completed' ORDER BY BOOKING_ID DESC);
    IF @open IS NOT NULL
    BEGIN
        BEGIN TRY
            UPDATE dbo.BOOKINGS SET COMPLETION_MESSAGE = N'Live test', COMPLETION_PHOTO_URL = N'/uploads/completions/live-test.jpg',
                   COMPLETED_AT = SYSUTCDATETIME() WHERE BOOKING_ID = @open;
            SET @refused = 0;
        END TRY
        BEGIN CATCH
            SET @refused = CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END;
        END CATCH
    END
    SET @bid = (SELECT TOP (1) BOOKING_ID FROM dbo.BOOKINGS WHERE STATUS = N'In Progress' ORDER BY BOOKING_ID DESC);
    IF @bid IS NOT NULL
    BEGIN
        SET @before = ISNULL((SELECT MAX(SYSTEM_LOG_ID) FROM dbo.SYSTEM_LOGS), 0);
        UPDATE dbo.BOOKINGS SET STATUS = N'Completed', COMPLETION_MESSAGE = N'Live test', COMPLETION_PHOTO_URL = N'/uploads/completions/live-test.jpg',
               COMPLETED_AT = SYSUTCDATETIME() WHERE BOOKING_ID = @bid;
        -- only this booking's lines (the running website may write a sign-in line at the same moment)
        SET @lines = (SELECT COUNT(*) FROM dbo.SYSTEM_LOGS WHERE SYSTEM_LOG_ID > @before AND ENTITY_NAME = N'Booking' AND ENTITY_ID = @bid);
        SET @line = (SELECT TOP (1) DESCRIPTION FROM dbo.SYSTEM_LOGS WHERE SYSTEM_LOG_ID > @before AND ENTITY_NAME = N'Booking' AND ENTITY_ID = @bid ORDER BY SYSTEM_LOG_ID);
    END
END TRY
BEGIN CATCH
    SET @line = CONCAT(N'ERROR ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
SELECT @refused AS CHECK_REFUSED, @bid AS IN_PROGRESS_BOOKING, @lines AS LOG_LINES, @line AS LOGGED_LINE,
       CASE WHEN @line LIKE N'System updated Booking #%: status ''In Progress'' -> ''Completed'' (proof photo and completion message saved).' THEN 1 ELSE 0 END AS LINE_OK;
"@).Tables[0]
$live | Format-List | Out-Host
$l = $live.Rows[0]
$bad = $false
if ($l.CHECK_REFUSED -is [DBNull]) { Write-Host "(no booking that is not Completed: the CHECK part was skipped)" }
elseif ($l.CHECK_REFUSED -ne 1) { $bad = $true }
if ($l.IN_PROGRESS_BOOKING -is [DBNull]) { Write-Host "(no In Progress booking: the trigger part was skipped)" }
elseif ($l.LOG_LINES -ne 1 -or $l.LINE_OK -ne 1) { $bad = $true }
if ($bad) {
    Write-Host "The live test did not give the expected result - send the text above to Claude. (It was rolled back: nothing was kept.)" -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.11 - completing a job needs a completion message and a proof photo." -ForegroundColor Green
Write-Host "Next: dotnet run --project $Web, then Provider > Booking Requests > an In Progress booking > Mark Completed."
