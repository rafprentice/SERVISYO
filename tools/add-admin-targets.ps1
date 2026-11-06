# SERVISYO database v2.2 (AdminTargets): one-time setup of the EF migration.
#   ADMIN_ACTIONS: TARGET_ID (one id for four different things, no foreign key) is replaced by
#   TARGET_CUSTOMER_ID / TARGET_PROVIDER_ID / TARGET_REVIEW_ID / TARGET_CATEGORY_ID, each a FOREIGN KEY;
#   TARGET_TYPE becomes CUSTOMER / SERVICE_PROVIDER / REVIEW / CATEGORY (no more USER). Existing rows are converted.
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-admin-targets.ps1
# Safe to run again: anything already done is skipped.
# It does NOT touch the database; you run "dotnet ef database update" yourself afterwards.
$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)

# ---------------------------------------------------------------- 0. the files from the zip must be here
foreach ($f in @("src\Servisyo.Web\Data\AdminTargetsSql.cs", "docs\sql\DatabaseDesign-v2.sql", "docs\sql\check-before-admin-targets.sql")) {
    if (-not (Test-Path $f)) { throw "MISSING $f - extract admin_targets.zip in the repo root first." }
}
$ctx = [System.IO.File]::ReadAllText((Resolve-Path "src\Servisyo.Web\Data\ServisyoDbContext.cs").Path)
if (-not $ctx.Contains("CK_ADMIN_ACTIONS_TARGET")) {
    throw "ServisyoDbContext.cs does not contain CK_ADMIN_ACTIONS_TARGET - extract admin_targets.zip again (it replaces that file)."
}
$migDir = "src\Servisyo.Web\Migrations"
if (-not (Get-ChildItem $migDir -Filter "*_ProfileCleanup.cs" -ErrorAction SilentlyContinue)) {
    throw "The ProfileCleanup migration (database v2.1) is missing. It must come first."
}

# ---------------------------------------------------------------- 1. build
Write-Host "Building..."
dotnet build src\Servisyo.Web | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed - send the errors above to Claude, then run this script again." }

# ---------------------------------------------------------------- 2. the migration
function Find-Migration { Get-ChildItem $migDir -Filter "*_AdminTargets.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1 }
$mig = Find-Migration
if (-not $mig) {
    Write-Host "Creating the AdminTargets migration..."
    dotnet ef migrations add AdminTargets --project src\Servisyo.Web | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations add failed - see above." }
    $mig = Find-Migration
}

# ---------------------------------------------------------------- 3. put the SQL around the EF operations
$m = [System.IO.File]::ReadAllText($mig.FullName)
if (-not $m.Contains("AdminTargetsSql")) {
    $upSig = "protected override void Up(MigrationBuilder migrationBuilder)"
    $downSig = "protected override void Down(MigrationBuilder migrationBuilder)"
    $up = $m.IndexOf($upSig); $down = $m.IndexOf($downSig)
    if ($up -lt 0 -or $down -lt 0) { throw "Unexpected migration file $($mig.Name) - tell Claude" }

    # what EF must have generated in Up(): TARGET_ID removed, the four new columns added, then the FOREIGN KEYs / CHECKs
    $upPart = $m.Substring($up, $down - $up)
    if (-not ($upPart -match 'DropColumn\(\s*name: "TARGET_ID",\s*table: "ADMIN_ACTIONS"')) { throw "Up() does not drop ADMIN_ACTIONS.TARGET_ID - send $($mig.Name) to Claude." }
    foreach ($col in @("TARGET_CUSTOMER_ID", "TARGET_PROVIDER_ID", "TARGET_REVIEW_ID", "TARGET_CATEGORY_ID")) {
        if (-not ($upPart -match ('AddColumn<int>\(\s*name: "' + $col + '",\s*table: "ADMIN_ACTIONS"'))) { throw "Up() does not add ADMIN_ACTIONS.$col - send $($mig.Name) to Claude." }
    }
    $lastAdd = $upPart.LastIndexOf("migrationBuilder.AddColumn<")
    $firstFk = $upPart.IndexOf("migrationBuilder.AddForeignKey(")
    $firstCk = $upPart.IndexOf("migrationBuilder.AddCheckConstraint(")
    if ($firstFk -lt $lastAdd -or $firstCk -lt $lastAdd) { throw "Unexpected order of operations in $($mig.Name) - send it to Claude." }

    # start of Up(): save every row's new target while TARGET_ID still exists (stops, changing nothing, on a row it cannot convert)
    $open = $m.IndexOf("{", $up)
    $m = $m.Insert($open + 1, "`r`n            // database v2.2: save each ADMIN_ACTIONS row's new target before EF removes TARGET_ID`r`n" +
                              "            migrationBuilder.Sql(Servisyo.Web.Data.AdminTargetsSql.StageTargets);`r`n")

    # right after the last AddColumn (before the FOREIGN KEYs and CHECKs): fill the new columns
    $down = $m.IndexOf($downSig)
    $upPart = $m.Substring($up, $down - $up)
    $lastAdd = $up + $upPart.LastIndexOf("migrationBuilder.AddColumn<")
    $endOfAdd = $m.IndexOf(");", $lastAdd) + 2
    $m = $m.Insert($endOfAdd, "`r`n`r`n            // database v2.2: fill the new target columns (the FOREIGN KEYs and CHECKs below need them)`r`n" +
                              "            migrationBuilder.Sql(Servisyo.Web.Data.AdminTargetsSql.FillTargets);")

    # end of Up(): the procedures that log administrator actions, writing the new columns
    $down = $m.IndexOf($downSig)
    $upEnd = $m.LastIndexOf("}", $down)
    $m = $m.Insert($upEnd, "`r`n            // database v2.2: usp_ApproveProvider / usp_UpdateAccountStatus / usp_ModerateReview (docs\sql\DatabaseDesign-v2.sql)`r`n" +
                           "            foreach (var sql in Servisyo.Web.Data.AdminTargetsSql.CreateAll)`r`n" +
                           "                migrationBuilder.Sql(sql);`r`n        ")

    # start of Down(): save the v2.1 targets; end of Down(): put them back (TARGET_ID is back by then) and the v2.1 procedures
    $down = $m.IndexOf($downSig)
    $open = $m.IndexOf("{", $down)
    $m = $m.Insert($open + 1, "`r`n            migrationBuilder.Sql(Servisyo.Web.Data.AdminTargetsSql.StageOld);`r`n")
    $classEnd = $m.LastIndexOf("}", $m.LastIndexOf("}", $m.LastIndexOf("}") - 1) - 1)
    $m = $m.Insert($classEnd, "`r`n            migrationBuilder.Sql(Servisyo.Web.Data.AdminTargetsSql.FillOld);`r`n" +
                              "            foreach (var sql in Servisyo.Web.Data.AdminTargetsSql.RestoreV21)`r`n" +
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
[regex]::Matches($upPart, 'migrationBuilder\.(\w+)(?:<\w+>)?\(\s*name: "(\w+)",\s*table: "(\w+)"') | ForEach-Object {
    Write-Host ("  {0,-22} {1}.{2}" -f $_.Groups[1].Value, $_.Groups[3].Value, $_.Groups[2].Value)
}
Write-Host ""
Write-Host "It should only touch ADMIN_ACTIONS: DropColumn TARGET_ID; AddColumn TARGET_CATEGORY_ID / TARGET_CUSTOMER_ID /"
Write-Host "TARGET_PROVIDER_ID / TARGET_REVIEW_ID; CreateIndex + AddForeignKey for each; AddCheckConstraint CK_ADMIN_ACTIONS_TARGET(_TYPE)."
Write-Host "If it touches ANY other table, STOP and send the file to Claude."
Write-Host ""
Write-Host "Next: run docs\sql\check-before-admin-targets.sql in SSMS on (localdb)\MSSQLLocalDB (SERVISYO)."
Write-Host "If the SECOND grid is empty, run:  dotnet ef database update --project src\Servisyo.Web"
