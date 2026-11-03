# SERVISYO database v2.1 (ProfileCleanup): one-time setup of the EF migration.
#   - removes CITY (Cebu City only), EMAIL 256, names 100, PHONE_NUMBER 11 (09XXXXXXXXX)
#   - adds ACCOUNT_STATUS copies to CUSTOMERS / SERVICE_PROVIDERS (kept equal to USERS by triggers)
#   - new v2.1 procedures, view and triggers
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-profile-cleanup.ps1
# Safe to run again: anything already done is skipped.
# It does NOT touch the database; you run "dotnet ef database update" yourself afterwards.
$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)

# ---------------------------------------------------------------- 0. the files from the zip must be here
foreach ($f in @("src\Servisyo.Web\Data\ProfileCleanupSql.cs", "docs\sql\DatabaseDesign-v2.sql", "docs\sql\check-before-profile-cleanup.sql")) {
    if (-not (Test-Path $f)) { throw "MISSING $f - extract profile_cleanup.zip in the repo root first." }
}
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "src\Servisyo.Web\Data\ServisyoDbContext.cs").Path)
foreach ($needle in @("trg_Users_StatusSync", "trg_Customers_StatusCopy", "trg_Providers_StatusCopy", "CK_CUSTOMERS_STATUS")) {
    if (-not $ctx.Contains($needle)) { throw "ServisyoDbContext.cs does not contain $needle - extract profile_cleanup.zip again (it replaces that file)." }
}
$migDir = "src\Servisyo.Web\Migrations"
if (-not (Get-ChildItem $migDir -Filter "*_DatabaseV2.cs" -ErrorAction SilentlyContinue)) {
    throw "The DatabaseV2 migration is missing. Run git pull first."
}

# ---------------------------------------------------------------- 1. build
Write-Host "Building..."
dotnet build src\Servisyo.Web | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed - send the errors above to Claude, then run this script again." }

# ---------------------------------------------------------------- 2. the migration
$mig = Get-ChildItem $migDir -Filter "*_ProfileCleanup.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1
if (-not $mig) {
    Write-Host "Creating the ProfileCleanup migration..."
    dotnet ef migrations add ProfileCleanup --project src\Servisyo.Web | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations add failed - see above." }
    $mig = Get-ChildItem $migDir -Filter "*_ProfileCleanup.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1
}

# ---------------------------------------------------------------- 3. put the SQL around the EF operations
$m = [System.IO.File]::ReadAllText($mig.FullName)
if (-not $m.Contains("ProfileCleanupSql")) {
    $upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
    $downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
    $up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
    if ($up -lt 0 -or $down -lt 0) { throw "Unexpected migration file $($mig.Name) - tell Claude" }

    # start of Up(): stop (changing nothing) on a name clash or on data the smaller columns cannot hold
    $open = $m.IndexOf("{", $up)
    $m = $m.Insert($open + 1, "`r`n            // database v2.1: stop here (nothing changed) on a trigger-name clash, or if a phone number / name does not fit`r`n" +
                              "            migrationBuilder.Sql(Servisyo.Web.Data.ProfileCleanupSql.CheckNoClash);`r`n" +
                              "            migrationBuilder.Sql(Servisyo.Web.Data.ProfileCleanupSql.PrepareData);`r`n")

    # end of Up(): copy the real statuses, then the v2.1 view / procedures / triggers
    $down = $m.IndexOf($downSig)
    $upEnd = $m.LastIndexOf("}", $down)
    $m = $m.Insert($upEnd, "`r`n            // database v2.1: copy USERS.ACCOUNT_STATUS into the new columns, then the v2.1 objects (docs\sql\DatabaseDesign-v2.sql)`r`n" +
                           "            migrationBuilder.Sql(Servisyo.Web.Data.ProfileCleanupSql.CopyAccountStatus);`r`n" +
                           "            foreach (var sql in Servisyo.Web.Data.ProfileCleanupSql.CreateAll)`r`n" +
                           "                migrationBuilder.Sql(sql);`r`n        ")

    # start of Down(): remove the new triggers first; end of Down(): the v2 object texts (CITY is back by then)
    $down = $m.IndexOf($downSig)
    $open = $m.IndexOf("{", $down)
    $m = $m.Insert($open + 1, "`r`n            migrationBuilder.Sql(Servisyo.Web.Data.ProfileCleanupSql.DropNew);`r`n")
    $classEnd = $m.LastIndexOf("}", $m.LastIndexOf("}", $m.LastIndexOf("}") - 1) - 1)
    $m = $m.Insert($classEnd, "`r`n            foreach (var sql in Servisyo.Web.Data.ProfileCleanupSql.RestoreV2)`r`n" +
                              "                migrationBuilder.Sql(sql);`r`n        ")

    [System.IO.File]::WriteAllText($mig.FullName, $m, $utf8)
    Write-Host "Edited: $($mig.Name) (database SQL added)"
} else { Write-Host "Already done: $($mig.Name)" }

# ---------------------------------------------------------------- 4. build again (the migration must compile)
dotnet build src\Servisyo.Web | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed after editing the migration - send the errors above to Claude." }

# ---------------------------------------------------------------- 5. show what the migration does
$m = [System.IO.File]::ReadAllText($mig.FullName)
$upPart = $m.Substring(0, $m.IndexOf("protected override void Down"))
Write-Host ""
Write-Host "Done. The EF operations in Up() of $($mig.Name):"
[regex]::Matches($upPart, 'migrationBuilder\.(\w+)\(\s*name: "(\w+)",\s*table: "(\w+)"') | ForEach-Object {
    Write-Host ("  {0,-22} {1}.{2}" -f $_.Groups[1].Value, $_.Groups[3].Value, $_.Groups[2].Value)
}
Write-Host ""
Write-Host "It should only: DropColumn CITY (CUSTOMERS, SERVICE_PROVIDERS); AddColumn ACCOUNT_STATUS (CUSTOMERS, SERVICE_PROVIDERS);"
Write-Host "AlterColumn FIRST_NAME / MIDDLE_NAME / LAST_NAME / EMAIL / PHONE_NUMBER (USERS); AddCheckConstraint CK_CUSTOMERS_STATUS / CK_PROVIDERS_STATUS"
Write-Host "(EF may also drop and re-create IX_USERS_EMAIL around the EMAIL change - that is normal)."
Write-Host "If it DROPS A TABLE or any other column, STOP and send the file to Claude."
Write-Host ""
Write-Host "Next: run docs\sql\check-before-profile-cleanup.sql in SSMS on (localdb)\MSSQLLocalDB (SERVISYO). If every grid is empty"
Write-Host "except the CITY list, run:  dotnet ef database update --project src\Servisyo.Web"
