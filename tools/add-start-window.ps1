# SERVISYO database v2.12 ("StartWindow"): "Mark In Progress" only inside the start window (Philippine time).
#   - BOOKINGS.STARTED_AT (DATETIME2, NULL) and LATE_REASON (NVARCHAR(500), NULL): when the job started, and why it was late
#   - BOOKINGS.MINUTES_LATE (INT, NULL) and IS_LATE (BIT, never NULL): CALCULATED from STARTED_AT and the booking's date
#     and time (not stored, rule 2)
#   - CHECK CK_BOOKINGS_START: a start only from the scheduled time to 2 hours after it, a reason exactly when it was
#     more than 15 minutes late
#   - trg_Bookings_StatusFlow v2.12: STARTED_AT is "now" and written once; Accepted -> Cancelled only for a provider
#     no-show (not started within 2 hours, test e-wallet payment refunded)
#   - trg_Bookings_Audit v2.12: "(late start: N minutes late, reason '...')", "(started on time)" and the no-show line
#   - usp_UpdateBookingStatus, usp_GetBookingsNeedingAttention and usp_GetNotifications v2.12
#   (Servisyo.Web.Data.StartWindowSql, added to the migration by this script)
#   Existing bookings are not changed by the update. After it, the website cancels every Accepted booking that was paid
#   (or cash promised) and is more than 2 hours past its time without a start: step 2 below shows how many there are.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-start-window.ps1
#
# What it does, stopping at the FIRST problem (SERVISYO is not changed until you type YES):
#   1. build + run all tests
#   2. create the EF migration "StartWindow", check that EF only adds the four columns and the CHECK, and add the
#      StartWindowSql steps to it
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

# ---------------------------------------------------------------- 0. the files from the zip must be here, v2.11 first
if (-not (Test-Path "$Web\Data\StartWindowSql.cs")) { Stop-Here "$Web\Data\StartWindowSql.cs is missing - extract the zip in the repo root first." }
$ent = [System.IO.File]::ReadAllText((Resolve-Path "$Web\Models\Entities.cs").Path)
if (-not $ent.Contains("LateReason")) { Stop-Here "Models\Entities.cs does not have LateReason - extract the zip in the repo root first." }
$v211 = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%[_]BookingCompletionProof'"
if ($v211 -ne 1) { Stop-Here "$Main is not on database v2.11 yet. Run tools\add-booking-completion.ps1 first." }
function Find-Migration { Get-ChildItem $MigDir -Filter "*_StartWindow.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

# ---------------------------------------------------------------- 2. the migration
Say "2. The EF migration StartWindow"
$mig = Find-Migration
if (-not $mig) {
    Ef @("migrations", "add", "StartWindow")
    $mig = Find-Migration
    if (-not $mig) { Stop-Here "dotnet ef did not create the StartWindow migration - send the text above to Claude." }
}
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
$downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
$up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
if ($up -lt 0 -or $down -lt 0) { Stop-Here "unexpected migration file $($mig.Name) - send it to Claude." }
$upPart = $m.Substring($up, $down - $up)

# EF must only add the four columns and the CHECK (our Sql steps are skipped)
$found = @()
Write-Host "EF operations in Up():"
foreach ($op in ([regex]::Split($upPart, 'migrationBuilder\.') | Select-Object -Skip 1)) {
    $name = ([regex]::Match($op, '^\w+')).Value
    if ($name -eq "Sql" -and ($op.Contains("StartWindowSql.") -or $op.StartsWith("Sql(sql)"))) { continue }
    $what = ([regex]::Match($op, '\bname: "(\w+)"')).Groups[1].Value
    $t = ([regex]::Match($op, '\btable: "(\w+)"')).Groups[1].Value
    $line = "$name $t $what"
    Write-Host "  $line"
    $found += $line
}
$expected = @(
    "AddColumn BOOKINGS IS_LATE",
    "AddColumn BOOKINGS LATE_REASON",
    "AddColumn BOOKINGS MINUTES_LATE",
    "AddColumn BOOKINGS STARTED_AT",
    "AddCheckConstraint BOOKINGS CK_BOOKINGS_START")
$missing = @($expected | Where-Object { $_ -notin $found })
$extra = @($found | Where-Object { $_ -notin $expected })
$inside = @("maxLength: 500", "computedColumnSql:", "LEN([LATE_REASON]) > 0", "DATEADD(HOUR, 8, [STARTED_AT])")
$notInside = @($inside | Where-Object { -not $upPart.Contains($_) })
if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $notInside.Count -gt 0) {
    if ($missing.Count -gt 0) { Write-Host "Missing: $($missing -join '; ')" -ForegroundColor Red }
    if ($extra.Count -gt 0) { Write-Host "Not expected: $($extra -join '; ')" -ForegroundColor Red }
    if ($notInside.Count -gt 0) { Write-Host "Not in the migration: $($notInside -join '; ')" -ForegroundColor Red }
    Stop-Here "the migration $($mig.Name) is not only the start window columns - send that file to Claude. (To undo it: dotnet ef migrations remove --project $Web)"
}

if (-not $m.Contains("StartWindowSql.CreateAll")) {
    $nl = "`r`n"
    # Up: LAST the five v2.12 objects (they read the new columns)
    $upOpen = $m.IndexOf("{", $m.IndexOf($upSig))
    $upEnd = Block-End $m $upOpen
    $m = $m.Insert($upEnd, "$nl            // database v2.12: the start window, the late reason and the provider no-show in the triggers and procedures$nl" +
                           "            foreach (var sql in Servisyo.Web.Data.StartWindowSql.CreateAll) migrationBuilder.Sql(sql);$nl        ")
    # Down: FIRST the objects as they were before (before EF removes the columns)
    $downOpen = $m.IndexOf("{", $m.IndexOf($downSig))
    $m = $m.Insert($downOpen + 1, "$nl            // database v2.12 undone: the five objects as they were before, then EF removes the columns$nl" +
                                  "            foreach (var sql in Servisyo.Web.Data.StartWindowSql.RestoreOld) migrationBuilder.Sql(sql);$nl")
    [System.IO.File]::WriteAllText($mig.FullName, $m)
    Write-Host "Added the StartWindowSql steps to $($mig.Name)."
    dotnet build | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed after adding the SQL steps - send $($mig.Name) to Claude." }
}
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
Write-Host "OK: $migId adds the start window to BOOKINGS." -ForegroundColor Green

$applied = Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$(Q $migId)'"
if ($applied -eq 1) { Write-Host "Already applied to $Main." }
else {
    Write-Host "The update itself changes no data: four empty / calculated columns are added to BOOKINGS."
    # what the website will do after the update: paid (or cash) bookings already more than 2 hours past their time
    $late = Scalar $Main @"
SELECT COUNT(*) FROM dbo.BOOKINGS b
WHERE b.STATUS = N'Accepted' AND EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL p WHERE p.BOOKING_ID = b.BOOKING_ID)
  AND DATEDIFF(MINUTE, CAST(b.BOOKING_DATE AS DATETIME) + CAST(b.BOOKING_TIME AS DATETIME), DATEADD(HOUR, 8, SYSUTCDATETIME())) > 121
"@
    if ($late -gt 0) {
        Write-Host "NOTE: $late Accepted booking(s) with a payment are already more than 2 hours past their scheduled time." -ForegroundColor Yellow
        Write-Host "      About a minute after the website starts, it cancels them as provider no-shows (a test e-wallet payment" -ForegroundColor Yellow
        Write-Host "      is marked Refunded; System Logs records each one). The backup below keeps them as they are now." -ForegroundColor Yellow
    }

    # ------------------------------------------------------------ 3. backup
    Say "3. Backup of $Main"
    $backupDir = Join-Path $env:USERPROFILE "Documents\SERVISYO-backups"
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $bak = Join-Path $backupDir ("SERVISYO-before-v2.12-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
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
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.BOOKINGS') AND is_nullable = 1 AND is_computed = 0 AND (
        (name = N'STARTED_AT' AND system_type_id = TYPE_ID(N'datetime2')) OR (name = N'LATE_REASON' AND max_length = 1000))) AS START_COLUMNS,
    (SELECT COUNT(*) FROM sys.computed_columns WHERE object_id = OBJECT_ID(N'dbo.BOOKINGS') AND is_persisted = 0
        AND name IN (N'MINUTES_LATE', N'IS_LATE')) AS CALCULATED_COLUMNS,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_BOOKINGS_START') AS START_CHECK,
    (SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.trg_Bookings_StatusFlow')) LIKE N'%50164%'
                  AND OBJECT_DEFINITION(OBJECT_ID(N'dbo.trg_Bookings_Audit')) LIKE N'%late start:%'
                  AND OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_UpdateBookingStatus')) LIKE N'%@LateReason%'
                  AND OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_GetBookingsNeedingAttention')) LIKE N'%NoShowCount%'
                  AND OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_GetNotifications')) LIKE N'%Provider No-Show%'
                 THEN 1 ELSE 0 END) AS OBJECTS_V212
"@).Tables[0]
$result | Format-Table -AutoSize | Out-Host
$row = $result.Rows[0]
if ($row.MIGRATION_APPLIED -ne 1 -or $row.START_COLUMNS -ne 2 -or $row.CALCULATED_COLUMNS -ne 2 -or $row.START_CHECK -ne 1 -or $row.OBJECTS_V212 -ne 1) {
    Write-Host "Something is not as expected - send the table above to Claude." -ForegroundColor Red
    exit 1
}

# a live test on your own data, inside ONE transaction that is always rolled back (nothing is kept):
#   1. a start time on a booking that has not started is refused (the CHECK)
#   2. the newest scheduled booking (Accepted, paid or cash chosen), moved to 30 minutes from now: starting is refused
#      (too early); moved to 30 minutes ago: it starts late with a reason, MINUTES_LATE = 30 (31 if the minute turned
#      during the test), ONE System Logs line
#   3. the same booking moved to 3 hours ago: cancelled as a no-show (the trigger allows it), ONE status line
Say "Live test (rolled back, nothing is kept)"
$live = (Invoke-Sql $Main @"
SET NOCOUNT ON;
DECLARE @refused INT = NULL, @early INT = NULL, @bid INT = NULL, @pu INT = NULL, @minutes INT = NULL, @isLate BIT = NULL,
        @lines INT = NULL, @line NVARCHAR(500) = NULL, @noShow NVARCHAR(500) = NULL, @before INT, @open INT, @at DATETIME2(0);
DECLARE @base DATETIME2(0) = '20000101';
BEGIN TRANSACTION;
BEGIN TRY
    SET @open = (SELECT TOP (1) BOOKING_ID FROM dbo.BOOKINGS WHERE STATUS IN (N'Pending', N'Accepted') ORDER BY BOOKING_ID DESC);
    IF @open IS NOT NULL
    BEGIN
        BEGIN TRY
            UPDATE dbo.BOOKINGS SET STARTED_AT = SYSUTCDATETIME() WHERE BOOKING_ID = @open;
            SET @refused = 0;
        END TRY
        BEGIN CATCH
            SET @refused = CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END;
        END CATCH
    END
    SELECT TOP (1) @bid = b.BOOKING_ID, @pu = sp.USER_ID
    FROM dbo.BOOKINGS b
    JOIN dbo.SERVICE_PROVIDERS sp ON sp.PROVIDER_ID = b.PROVIDER_ID
    WHERE b.STATUS = N'Accepted' AND EXISTS (SELECT 1 FROM dbo.PAYMENTS_DTL p WHERE p.BOOKING_ID = b.BOOKING_ID)
    ORDER BY b.BOOKING_ID DESC;
    IF @bid IS NOT NULL
    BEGIN
        SAVE TRANSACTION live_start;
        -- 30 minutes from now (whole minutes, Cebu time): too early, the CHECK refuses the start
        SET @at = DATEADD(MINUTE, DATEDIFF(MINUTE, @base, DATEADD(HOUR, 8, SYSUTCDATETIME())) + 30, @base);
        UPDATE dbo.BOOKINGS SET BOOKING_DATE = CAST(@at AS DATE), BOOKING_TIME = CAST(@at AS TIME(0)) WHERE BOOKING_ID = @bid;
        BEGIN TRY
            UPDATE dbo.BOOKINGS SET STATUS = N'In Progress', STARTED_AT = SYSUTCDATETIME() WHERE BOOKING_ID = @bid;
            SET @early = 0;
        END TRY
        BEGIN CATCH
            SET @early = CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END;
        END CATCH
        -- 30 minutes ago: a late start with a reason, as the provider
        SET @at = DATEADD(MINUTE, DATEDIFF(MINUTE, @base, DATEADD(HOUR, 8, SYSUTCDATETIME())) - 30, @base);
        UPDATE dbo.BOOKINGS SET BOOKING_DATE = CAST(@at AS DATE), BOOKING_TIME = CAST(@at AS TIME(0)) WHERE BOOKING_ID = @bid;
        SET @before = ISNULL((SELECT MAX(SYSTEM_LOG_ID) FROM dbo.SYSTEM_LOGS), 0);
        EXEC sp_set_session_context @key = N'servisyo_user_id', @value = @pu;
        EXEC sp_set_session_context @key = N'servisyo_user_role', @value = N'Service Provider';
        UPDATE dbo.BOOKINGS SET STATUS = N'In Progress', STARTED_AT = SYSUTCDATETIME(), LATE_REASON = N'Live test: heavy traffic',
               UPDATED_AT = SYSUTCDATETIME() WHERE BOOKING_ID = @bid;
        EXEC sp_set_session_context @key = N'servisyo_user_id', @value = NULL;
        EXEC sp_set_session_context @key = N'servisyo_user_role', @value = NULL;
        SELECT @minutes = MINUTES_LATE, @isLate = IS_LATE FROM dbo.BOOKINGS WHERE BOOKING_ID = @bid;
        -- only this booking's lines (the running website may write a sign-in line at the same moment)
        SET @lines = (SELECT COUNT(*) FROM dbo.SYSTEM_LOGS WHERE SYSTEM_LOG_ID > @before AND ENTITY_NAME = N'Booking' AND ENTITY_ID = @bid);
        SET @line = (SELECT TOP (1) DESCRIPTION FROM dbo.SYSTEM_LOGS WHERE SYSTEM_LOG_ID > @before AND ENTITY_NAME = N'Booking' AND ENTITY_ID = @bid ORDER BY SYSTEM_LOG_ID);
        ROLLBACK TRANSACTION live_start;
        -- 3 hours ago, never started: the no-show job's change (the trigger allows it; "System" did it)
        SET @at = DATEADD(MINUTE, DATEDIFF(MINUTE, @base, DATEADD(HOUR, 8, SYSUTCDATETIME())) - 180, @base);
        UPDATE dbo.BOOKINGS SET BOOKING_DATE = CAST(@at AS DATE), BOOKING_TIME = CAST(@at AS TIME(0)) WHERE BOOKING_ID = @bid;
        SET @before = ISNULL((SELECT MAX(SYSTEM_LOG_ID) FROM dbo.SYSTEM_LOGS), 0);
        UPDATE dbo.BOOKINGS SET STATUS = N'Cancelled', UPDATED_AT = SYSUTCDATETIME(),
               PAYMENT_STATUS = CASE WHEN PAYMENT_STATUS = N'Paid' THEN N'Refunded' ELSE PAYMENT_STATUS END
        WHERE BOOKING_ID = @bid;
        SET @noShow = (SELECT TOP (1) DESCRIPTION FROM dbo.SYSTEM_LOGS WHERE SYSTEM_LOG_ID > @before AND ENTITY_NAME = N'Booking'
                       AND ENTITY_ID = @bid AND DESCRIPTION LIKE N'%: status %' ORDER BY SYSTEM_LOG_ID);
    END
END TRY
BEGIN CATCH
    SET @line = CONCAT(N'ERROR ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
EXEC sp_set_session_context @key = N'servisyo_user_id', @value = NULL;
EXEC sp_set_session_context @key = N'servisyo_user_role', @value = NULL;
SELECT @refused AS CHECK_REFUSED, @bid AS SCHEDULED_BOOKING, @early AS TOO_EARLY_REFUSED, @minutes AS MINUTES_LATE, @isLate AS IS_LATE,
       @lines AS LOG_LINES, @line AS LOGGED_LINE, @noShow AS NO_SHOW_LINE,
       CASE WHEN @line LIKE N'Service Provider % updated Booking #%: status ''Accepted'' -> ''In Progress'' (late start: ' + CAST(@minutes AS NVARCHAR(10)) + N' minutes late, reason ''Live test: heavy traffic'').' THEN 1 ELSE 0 END AS LINE_OK,
       CASE WHEN @noShow LIKE N'System updated Booking #%: status ''Accepted'' -> ''Cancelled'' (Provider No-Show ' + NCHAR(8212) + N' missed scheduled start window).' THEN 1 ELSE 0 END AS NO_SHOW_OK;
"@).Tables[0]
$live | Format-List | Out-Host
$l = $live.Rows[0]
$bad = $false
if ($l.CHECK_REFUSED -is [DBNull]) { Write-Host "(no Pending or Accepted booking: the CHECK part was skipped)" }
elseif ($l.CHECK_REFUSED -ne 1) { $bad = $true }
if ($l.SCHEDULED_BOOKING -is [DBNull]) { Write-Host "(no Accepted booking with a payment: the start and no-show parts were skipped)" }
elseif ($l.TOO_EARLY_REFUSED -ne 1 -or ($l.MINUTES_LATE -ne 30 -and $l.MINUTES_LATE -ne 31) -or $l.IS_LATE -ne $true -or $l.LOG_LINES -ne 1 -or $l.LINE_OK -ne 1 -or $l.NO_SHOW_OK -ne 1) { $bad = $true }
if ($bad) {
    Write-Host "The live test did not give the expected result - send the text above to Claude. (It was rolled back: nothing was kept.)" -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "DONE: $Main is on database v2.12 - Mark In Progress follows the start window (Philippine time)." -ForegroundColor Green
Write-Host "Next: dotnet run --project $Web, then Provider > Booking Requests > a scheduled (Accepted, paid) booking."
