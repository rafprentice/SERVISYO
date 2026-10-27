# SERVISYO database v2 (functions, views, stored procedures, triggers, indexes): one-time setup.
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-database-v2.ps1
# Safe to run again: anything already done is skipped.
# It does NOT touch the database; you run "dotnet ef database update" yourself afterwards.
$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)

# ---------------------------------------------------------------- 0. the files from the zip must be here
foreach ($f in @("src\Servisyo.Web\Data\DatabaseV2Sql.cs", "docs\sql\DatabaseDesign-v2.sql")) {
    if (-not (Test-Path $f)) { throw "MISSING $f - extract database_v2.zip in the repo root first." }
}
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "src\Servisyo.Web\Data\ServisyoDbContext.cs").Path)
foreach ($needle in @("trg_Reviews_RatingSync", "trg_Payments_Audit", "trg_Bookings_StatusFlow", "IX_NOTIFICATIONS_USER_UNREAD")) {
    if (-not $ctx.Contains($needle)) { throw "ServisyoDbContext.cs does not contain $needle - extract database_v2.zip again (it replaces that file)." }
}
$migDir = "src\Servisyo.Web\Migrations"
if (-not (Get-ChildItem $migDir -Filter "*_AddPayments.cs" -ErrorAction SilentlyContinue)) {
    throw "The AddPayments migration is missing. Install the payments change first (tools\add-payments.ps1)."
}

# ---------------------------------------------------------------- 1. build
Write-Host "Building..."
dotnet build src\Servisyo.Web | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed - send the errors above to Claude, then run this script again." }

# ---------------------------------------------------------------- 2. the migration
$mig = Get-ChildItem $migDir -Filter "*_DatabaseV2.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1
if (-not $mig) {
    Write-Host "Creating the DatabaseV2 migration..."
    dotnet ef migrations add DatabaseV2 --project src\Servisyo.Web | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations add failed - see above." }
    $mig = Get-ChildItem $migDir -Filter "*_DatabaseV2.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1
}

# ---------------------------------------------------------------- 3. put the SQL objects into the migration
$m = [System.IO.File]::ReadAllText($mig.FullName)
if (-not $m.Contains("DatabaseV2Sql")) {
    $upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
    $downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
    $up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
    if ($up -lt 0 -or $down -lt 0) { throw "Unexpected migration file $($mig.Name) - tell Claude" }

    # start of Up(): stop (changing nothing) if a different object with one of our names already exists
    $open = $m.IndexOf("{", $up)
    $m = $m.Insert($open + 1, "`r`n            // database v2: stop here (nothing changed) if an object with one of the new names already exists`r`n" +
                              "            migrationBuilder.Sql(Servisyo.Web.Data.DatabaseV2Sql.CheckNoClash);`r`n")

    # end of Up(): the functions, views, procedures and triggers (after the EF index / check changes)
    $down = $m.IndexOf($downSig)
    $upEnd = $m.LastIndexOf("}", $down)
    $m = $m.Insert($upEnd, "`r`n            // database v2: functions, views, stored procedures and triggers (docs\sql\DatabaseDesign-v2.sql)`r`n" +
                           "            foreach (var sql in Servisyo.Web.Data.DatabaseV2Sql.CreateAll)`r`n" +
                           "                migrationBuilder.Sql(sql);`r`n        ")

    # start of Down(): remove exactly those objects first
    $down = $m.IndexOf($downSig)
    $open = $m.IndexOf("{", $down)
    $m = $m.Insert($open + 1, "`r`n            migrationBuilder.Sql(Servisyo.Web.Data.DatabaseV2Sql.DropAll);`r`n")

    [System.IO.File]::WriteAllText($mig.FullName, $m, $utf8)
    Write-Host "Edited: $($mig.Name) (database objects added)"
} else { Write-Host "Already done: $($mig.Name)" }

# ---------------------------------------------------------------- 4. build again (the migration must compile)
dotnet build src\Servisyo.Web | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed after editing the migration - send the errors above to Claude." }

Write-Host ""
Write-Host "Done. Open $($mig.Name) and check it. It should only:"
Write-Host "  - CreateIndex IX_BOOKINGS_PROVIDER_STATUS, IX_BOOKINGS_DATE_PAYMENT, IX_NOTIFICATIONS_USER_UNREAD, IX_MESSAGES_RECEIVER_UNREAD"
Write-Host "    (and DropIndex IX_NOTIFICATIONS_USER_ID / IX_MESSAGES_RECEIVER_ID, which the new indexes replace)"
Write-Host "  - AddCheckConstraint CK_BOOKINGS_FEE and CK_PROVIDERS_COUNTS"
Write-Host "  - run DatabaseV2Sql.CheckNoClash first and DatabaseV2Sql.CreateAll last"
Write-Host "If it DROPS A TABLE or a column, STOP and send it to Claude."
Write-Host "Then run:  dotnet ef database update --project src\Servisyo.Web"
