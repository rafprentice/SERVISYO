# System Logs (audit triggers) + Admin PDF report: one-time setup.
# Run from the repo root:   powershell -ExecutionPolicy Bypass -File tools\add-system-logs.ps1
# Safe to run again: anything already done is skipped. It does NOT touch the database; you run "dotnet ef database update" yourself.
$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Text($file)  { [System.IO.File]::ReadAllText((Resolve-Path $file).Path) }
function Write-Text($file, $text) { [System.IO.File]::WriteAllText((Resolve-Path $file).Path, $text, $utf8); Write-Host "Edited: $file" }

# ---------------------------------------------------------------- 1. ServisyoDbContext.cs
$ctxFile = "src\Servisyo.Web\Data\ServisyoDbContext.cs"
$c = Read-Text $ctxFile
$before = $c

if (-not $c.Contains("DbSet<SystemLog>")) {
    $a = "public DbSet<Notification> Notifications => Set<Notification>();"
    if (-not $c.Contains($a)) { throw "COULD NOT FIND the Notifications DbSet in $ctxFile - tell Claude" }
    $c = $c.Replace($a, $a + "`r`n    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();")
}

if (-not $c.Contains("mb.Entity<SystemLog>")) {
    $a = "        mb.Entity<Notification>(e =>"
    $i = $c.IndexOf($a)
    if ($i -lt 0) { throw "COULD NOT FIND the Notification block in $ctxFile - tell Claude" }
    $block = @"
        mb.Entity<SystemLog>(e =>
        {
            e.ToTable("SYSTEM_LOGS");
            e.Property(x => x.UserRole).HasMaxLength(50);
            e.Property(x => x.ActionType).HasMaxLength(20);
            e.Property(x => x.EntityName).HasMaxLength(50);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.ActionType, x.UserRole });
        });

"@
    $c = $c.Insert($i, $block)
}

# EF must know these tables have triggers (otherwise its INSERT/UPDATE ... OUTPUT statements are refused by SQL Server)
foreach ($pair in @(@("USERS", "trg_Users_Audit"), @("BOOKINGS", "trg_Bookings_Audit"), @("SERVICES", "trg_Services_Audit"))) {
    $table = $pair[0]; $trigger = $pair[1]
    if ($c.Contains("HasTrigger(""$trigger"")")) { continue }
    $rx = [regex]("e\.ToTable\(""" + $table + """, t =>\s*\{")
    if (-not $rx.IsMatch($c)) { throw "COULD NOT FIND e.ToTable(""$table"", t => { in $ctxFile - tell Claude" }
    $c = $rx.Replace($c, '$0' + "`r`n                t.HasTrigger(""$trigger"");", 1)
}

if ($c -ne $before) { Write-Text $ctxFile $c } else { Write-Host "Already done: $ctxFile" }

# ---------------------------------------------------------------- 2. Program.cs
$progFile = "src\Servisyo.Web\Program.cs"
$p = Read-Text $progFile
$before = $p

$lines = @(
    "builder.Services.AddHttpContextAccessor();",
    "builder.Services.AddScoped<ISystemLogService, SystemLogService>();",
    "builder.Services.AddScoped<IAdminReportService, AdminReportService>();"
)
$anchor = "builder.Services.AddScoped<IAuthService, AuthService>();"
if (-not $p.Contains($anchor)) { throw "COULD NOT FIND '$anchor' in Program.cs - tell Claude" }
$missing = $lines | Where-Object { -not $p.Contains($_) }
if ($missing) { $p = $p.Replace($anchor, $anchor + "`r`n" + ($missing -join "`r`n")) }

# tell SQL Server who is signed in, on every connection (the triggers read it)
if (-not $p.Contains("AuditSessionInterceptor")) {
    $use = 'options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))'
    $i = $p.IndexOf($use)
    if ($i -lt 0) { throw "COULD NOT FIND $use in Program.cs - tell Claude" }
    $p = $p.Insert($i + $use.Length, "`r`n           .AddInterceptors(new Servisyo.Web.Data.AuditSessionInterceptor(new Microsoft.AspNetCore.Http.HttpContextAccessor()))")
}

if ($p -ne $before) { Write-Text $progFile $p } else { Write-Host "Already done: $progFile" }

# ---------------------------------------------------------------- 3. build + migration with the triggers inside
Write-Host ""
Write-Host "Building..."
dotnet build src\Servisyo.Web | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed - fix the errors above (or send them to Claude), then run this script again." }

$migDir = "src\Servisyo.Web\Migrations"
$mig = Get-ChildItem $migDir -Filter "*_AddSystemLogs.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1
if (-not $mig) {
    Write-Host "Creating the AddSystemLogs migration..."
    dotnet ef migrations add AddSystemLogs --project src\Servisyo.Web | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations add failed - see above." }
    $mig = Get-ChildItem $migDir -Filter "*_AddSystemLogs.cs" | Where-Object { $_.Name -notlike "*.Designer.cs" } | Sort-Object Name | Select-Object -Last 1
}

$m = [System.IO.File]::ReadAllText($mig.FullName)
if (-not $m.Contains("AuditTriggersSql")) {
    $down = $m.IndexOf("protected override void Down(")
    if ($down -lt 0) { throw "Unexpected migration file $($mig.Name) - tell Claude" }
    # end of Up(): the last "}" before Down(
    $upEnd = $m.LastIndexOf("}", $down)
    $create = "`r`n            // audit triggers (after the SYSTEM_LOGS table exists)`r`n" +
              "            migrationBuilder.Sql(Servisyo.Web.Data.AuditTriggersSql.CreateBookings);`r`n" +
              "            migrationBuilder.Sql(Servisyo.Web.Data.AuditTriggersSql.CreateUsers);`r`n" +
              "            migrationBuilder.Sql(Servisyo.Web.Data.AuditTriggersSql.CreateServices);`r`n        "
    $m = $m.Insert($upEnd, $create)
    # start of Down(): drop the triggers before the table
    $down = $m.IndexOf("protected override void Down(")
    $open = $m.IndexOf("{", $down)
    $m = $m.Insert($open + 1, "`r`n            migrationBuilder.Sql(Servisyo.Web.Data.AuditTriggersSql.DropAll);")
    [System.IO.File]::WriteAllText($mig.FullName, $m, $utf8)
    Write-Host "Edited: $($mig.Name) (triggers added)"
} else { Write-Host "Already done: $($mig.Name)" }

Write-Host ""
Write-Host "Done. Now open $($mig.Name): it should only CreateTable SYSTEM_LOGS (+ indexes) and add the three triggers."
Write-Host "Then run:  dotnet ef database update --project src\Servisyo.Web"
