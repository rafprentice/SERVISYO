# SERVISYO: prepare an update of the ONLINE site (MonsterASP): the database update + the new website files.
#
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\make-monster-deploy.ps1
#
# It changes NOTHING online and nothing in your SERVISYO database. It only makes a folder
# Documents\SERVISYO-deploy\<date-time> with:
#   deploy-latest.sql      the database update     (Monster: Databases > your database > Import)
#   servisyo-publish.zip   the website files       (Monster: File Manager > wwwroot > upload + unzip)
# plus a copy of docs\sql\check-monster-before-deploy.sql (read-only: import it FIRST, it must end with READY).
# Then follow docs\deploy-monster-notes.md. The order matters: database first, then the files.
#
# Safety added to deploy-latest.sql (EF makes the steps, this script adds the checks):
#   - every step (migration) runs in one transaction with XACT_ABORT ON: a step that fails changes nothing
#   - a step only runs when every earlier step is already there, so nothing is put on top of a failed step
#   - OneDayOffPerDate (v2.5) stops with a clear message if two providers already have the same day off
#   - ANSI_NULLS and QUOTED_IDENTIFIER are ON first (the filtered UNIQUE indexes of v2.7 / v2.8 need them)
#   - the last check says whether the database is complete
#   - safe to import again: steps that are already there are skipped
# The zip leaves out appsettings.Development.json, appsettings.Production.json (Monster keeps its own settings)
# and wwwroot\uploads (the profile photos on Monster are not touched).
$ErrorActionPreference = "Stop"
$Server = "(localdb)\MSSQLLocalDB"
$Main = "SERVISYO"
$Web = "src\Servisyo.Web"
$MigDir = "$Web\Migrations"
$Latest = "NameChangeRequests"   # database v2.8 (tools\add-name-change-requests.ps1)

function Conn([string]$db) { "Server=$Server;Database=$db;Trusted_Connection=True;TrustServerCertificate=True" }
function Say([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Stop-Here([string]$text) { Write-Host ""; Write-Host "STOPPED: $text" -ForegroundColor Red; Write-Host "Nothing was changed online."; exit 1 }
function Scalar([string]$db, [string]$sql) {
    $c = New-Object System.Data.SqlClient.SqlConnection (Conn $db)
    $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; return $cmd.ExecuteScalar() } finally { $c.Close() }
}

# Adds the safety checks to the idempotent EF script (see the top of this file). Throws if the script does not look
# exactly like EF's usual layout: one "BEGIN TRANSACTION;" ... "COMMIT;" block per migration, in order.
function Add-DeployGuards([string]$sql) {
    $nl = if ($sql.Contains("`r`n")) { "`r`n" } else { "`n" }
    $begins = [regex]::Matches($sql, '(?m)^BEGIN TRANSACTION;\r?\n')
    $commits = [regex]::Matches($sql, '(?m)^COMMIT;\r?$')
    if ($begins.Count -eq 0 -or $begins.Count -ne $commits.Count) { throw "the EF script does not have one BEGIN TRANSACTION / COMMIT per step" }

    $ids = @()
    for ($i = 0; $i -lt $begins.Count; $i++) {
        $start = $begins[$i].Index; $end = $commits[$i].Index
        if ($end -lt $start -or ($i + 1 -lt $begins.Count -and $begins[$i + 1].Index -lt $end)) { throw "the steps of the EF script are not in the expected order" }
        $inStep = @([regex]::Matches($sql.Substring($start, $end - $start), "\[MigrationId\] = N'(\d{14}_\w+)'") |
                    ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
        if ($inStep.Count -ne 1) { throw "step $($i + 1) of the EF script is not exactly one migration" }
        if ($i -gt 0 -and [string]::CompareOrdinal($inStep[0], $ids[$i - 1]) -le 0) { throw "the migrations in the EF script are not in date order" }
        $ids += $inStep[0]
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("/* SERVISYO database update for MonsterASP, made $(Get-Date -Format 'yyyy-MM-dd HH:mm') by tools\make-monster-deploy.ps1.$nl")
    [void]$sb.Append("   Steps already in the database are skipped, so it is safe to import again. A step that fails changes nothing,$nl")
    [void]$sb.Append("   and the steps after it wait. Errors that start with SERVISYO: say what to do. */$nl")
    # the same settings as the website's own connection: the filtered UNIQUE indexes (v2.7, v2.8) need them, and some
    # import tools start with QUOTED_IDENTIFIER OFF
    [void]$sb.Append("SET ANSI_NULLS ON;$nl")
    [void]$sb.Append("SET QUOTED_IDENTIFIER ON;$nl")
    [void]$sb.Append("GO$nl")
    $pos = 0
    for ($i = 0; $i -lt $begins.Count; $i++) {
        $id = $ids[$i]
        [void]$sb.Append($sql.Substring($pos, $begins[$i].Index - $pos))
        [void]$sb.Append("SET XACT_ABORT ON;   -- SERVISYO: if anything in this step fails, the whole step is undone$nl")
        [void]$sb.Append("BEGIN TRANSACTION;$nl")
        if ($i -gt 0) {
            $earlier = ($ids[0..($i - 1)] | ForEach-Object { "N'$_'" }) -join ", "
            [void]$sb.Append("IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'$id')$nl")
            [void]$sb.Append("   AND (SELECT COUNT(*) FROM [__EFMigrationsHistory] WHERE [MigrationId] IN ($earlier)) < $i$nl")
            [void]$sb.Append("BEGIN$nl")
            [void]$sb.Append("    THROW 50001, N'SERVISYO: step $id was not applied because an earlier step is missing (see the first SERVISYO error). This step changed nothing.', 1;$nl")
            [void]$sb.Append("END;$nl")
        }
        if ($id -like "*_OneDayOffPerDate") {
            [void]$sb.Append("IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'$id')$nl")
            [void]$sb.Append("   AND EXISTS (SELECT 1 FROM [PROVIDER_UNAVAILABLE_DATES] GROUP BY [UNAVAILABLE_DATE] HAVING COUNT(*) > 1)$nl")
            [void]$sb.Append("BEGIN$nl")
            [void]$sb.Append("    THROW 50002, N'SERVISYO: two or more providers already have the same day off, but only one provider may be off per date. Remove the extra day off on that provider''s Availability page, then import this file again. This step changed nothing.', 1;$nl")
            [void]$sb.Append("END;$nl")
        }
        $pos = $begins[$i].Index + $begins[$i].Length
    }
    [void]$sb.Append($sql.Substring($pos).TrimEnd())
    $all = ($ids | ForEach-Object { "N'$_'" }) -join ", "
    [void]$sb.Append("$nl$nl-- SERVISYO: is every step there?$nl")
    [void]$sb.Append("IF (SELECT COUNT(*) FROM [__EFMigrationsHistory] WHERE [MigrationId] IN ($all)) < $($ids.Count)$nl")
    [void]$sb.Append("BEGIN$nl")
    [void]$sb.Append("    THROW 50003, N'SERVISYO: the database update is NOT complete. Read the first SERVISYO error above, fix it, and import this file again. Do not upload the new website files yet.', 1;$nl")
    [void]$sb.Append("END$nl")
    [void]$sb.Append("ELSE$nl")
    [void]$sb.Append("BEGIN$nl")
    [void]$sb.Append("    PRINT N'SERVISYO: the database update is complete (last step: $($ids[-1])).';$nl")
    [void]$sb.Append("END;$nl")
    [void]$sb.Append("GO$nl")
    return @{ Sql = $sb.ToString(); Ids = $ids }
}

# ---------------------------------------------------------------- 0. the laptop must be on the latest database first
if (-not (Test-Path "$Web\Servisyo.Web.csproj")) { Stop-Here "run this from the SERVISYO folder (the one with src and tools)." }
$mig = Get-ChildItem $MigDir -Filter "*_$Latest.cs" -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike "*.Designer.cs" } | Select-Object -Last 1
if (-not $mig) { Stop-Here "the $Latest migration is missing. Run tools\add-name-change-requests.ps1 first." }
$migId = [System.IO.Path]::GetFileNameWithoutExtension($mig.Name)
if ((Scalar $Main "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'$migId'") -ne 1) {
    Stop-Here "your local SERVISYO database is not on $migId yet. Run tools\add-name-change-requests.ps1 first and test the site."
}
Say "Code: branch $(git rev-parse --abbrev-ref HEAD), last commit $(git log -1 --format='%h %s')"
$dirty = git status --porcelain
if ($dirty) { Write-Host "Note: you have changes that are not committed yet. They are included in this deploy." -ForegroundColor Yellow }

# ---------------------------------------------------------------- 1. build + tests
Say "1. Build and tests"
dotnet build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "the build failed - send the errors above to Claude." }
dotnet test --no-build | Out-Host
if ($LASTEXITCODE -ne 0) { Stop-Here "some tests failed - send the result above to Claude." }

$out = Join-Path $env:USERPROFILE ("Documents\SERVISYO-deploy\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $out | Out-Null
$temp = Join-Path $env:TEMP ("servisyo-deploy-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $temp | Out-Null

try {
    # ------------------------------------------------------------ 2. the database update
    Say "2. The database update (deploy-latest.sql)"
    $raw = Join-Path $temp "ef-script.sql"
    & dotnet ef migrations script --idempotent -o $raw --project $Web | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "dotnet ef migrations script failed - send the text above to Claude." }
    try { $guarded = Add-DeployGuards ([System.IO.File]::ReadAllText($raw)) }
    catch { Stop-Here "the EF script has an unexpected layout ($($_.Exception.Message)) - send $raw to Claude." }
    if ($guarded.Ids[-1] -ne $migId) { Stop-Here "the last step of the EF script is $($guarded.Ids[-1]), not $migId - send the text above to Claude." }
    [System.IO.File]::WriteAllText((Join-Path $out "deploy-latest.sql"), $guarded.Sql, (New-Object System.Text.UTF8Encoding $true))
    Write-Host "Steps in the file ($($guarded.Ids.Count)); Monster skips the ones it already has:"
    $guarded.Ids | ForEach-Object { Write-Host "  $_" }
    Copy-Item "docs\sql\check-monster-before-deploy.sql" $out   # read-only check to import FIRST

    # ------------------------------------------------------------ 3. the website files
    Say "3. The website files (servisyo-publish.zip)"
    $pub = Join-Path $temp "publish"
    dotnet publish $Web -c Release -o $pub | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Here "dotnet publish failed - send the text above to Claude." }
    foreach ($leaveOut in @("appsettings.Development.json", "appsettings.Production.json", "wwwroot\uploads")) {
        $p = Join-Path $pub $leaveOut
        if (Test-Path $p) { Remove-Item $p -Recurse -Force; Write-Host "Left out: $leaveOut" }
    }
    $zip = Join-Path $out "servisyo-publish.zip"
    Compress-Archive -Path (Join-Path $pub "*") -DestinationPath $zip -Force

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try { $names = @($z.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }) } finally { $z.Dispose() }
    $bad = @($names | Where-Object { $_ -match '^appsettings\.(Development|Production)\.json$' -or $_ -like 'wwwroot/uploads/*' })
    if ($bad.Count -gt 0 -or $names -notcontains "Servisyo.Web.dll" -or $names -notcontains "web.config") {
        Stop-Here "the zip is not as expected ($($bad -join ', ')) - send this text to Claude."
    }
    Write-Host "OK: $($names.Count) files, $([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB."
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "READY: $out" -ForegroundColor Green
Write-Host "  check-monster-before-deploy.sql  1st: import it (read-only) - the last line must say READY"
Write-Host "  deploy-latest.sql                2nd: backup, Stop, then Monster > Databases > your database > Import"
Write-Host "  servisyo-publish.zip             3rd: Monster > File Manager > wwwroot > upload, unzip, overwrite"
Write-Host "Follow docs\deploy-monster-notes.md."
Invoke-Item $out
