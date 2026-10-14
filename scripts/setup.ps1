<#
.SYNOPSIS
  First-time setup of a DEVELOPMENT machine: restores packages, creates the development database and (optionally) loads sample data.

.DESCRIPTION
  * Installs web dependencies (npm ci) and restores .NET packages.
  * Applies EF Core migrations to the database in apps/api/src/ERecycleHub.Api/appsettings.Development.json
    (default: Server=localhost;Database=ERecycleHub_Dev;Integrated Security=True).
  * With -Seed, loads the well-known sample accounts and lots (refused by the API outside Development/Testing,
    and refused on a database that already has users).
  It never touches any other database and never reads production settings.

.EXAMPLE
  ./scripts/setup.ps1 -Seed
#>
param([switch]$Seed)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root "apps/api/src/ERecycleHub.Api"
$web = Join-Path $root "apps/web"

foreach ($tool in "dotnet", "node", "npm") {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH." }
}
Write-Host "dotnet $(dotnet --version), node $(node --version)"

Write-Host "`n== Web dependencies"
Push-Location $web; try { npm ci } finally { Pop-Location }

Write-Host "`n== .NET packages and build"
dotnet build (Join-Path $root "apps/api/ERecycleHub.slnx") --nologo -v q
if ($LASTEXITCODE) { throw "dotnet build failed" }

$env:ASPNETCORE_ENVIRONMENT = "Development"
Write-Host "`n== Database migrations (Development database)"
dotnet run --project $api --no-build -- migrate
if ($LASTEXITCODE) { throw "migrate failed" }

if ($Seed) {
  Write-Host "`n== Sample data"
  dotnet run --project $api --no-build -- seed-dev
  if ($LASTEXITCODE) { Write-Warning "seed-dev did not run (the database probably already has users). That is fine." }
}

Write-Host "`nDone. Start everything with ./scripts/dev.ps1"
