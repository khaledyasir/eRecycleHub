<#
.SYNOPSIS
  Runs every automated check. Stop the running API first (it locks the DLLs the .NET tests rebuild).

.PARAMETER E2E
  Also run the Playwright suite. Needs the API (started with -RelaxedRateLimits) and the web app already running on
  :5080 / :3000 against a seeded DEVELOPMENT database; see ./scripts/dev.ps1.

.NOTES
  .NET integration tests create and drop their own throw-away database named ERecycleHub_Test_<guid> on the local
  SQL Server. They never use the development or any other database.
#>
param([switch]$E2E)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root "apps/web"
$results = [ordered]@{}

function Step($name, [scriptblock]$body) {
  Write-Host "`n=== $name"
  & $body
  $results[$name] = if ($LASTEXITCODE) { "FAILED" } else { "passed" }
  $global:LASTEXITCODE = 0
}

Step ".NET unit + integration tests" { dotnet test (Join-Path $root "apps/api/ERecycleHub.slnx") --nologo }
Push-Location $web
try {
  Step "web typecheck" { npm run typecheck }
  Step "web lint" { npm run lint }
  Step "web unit/component tests (Vitest)" { npm test }
  if ($E2E) { Step "end-to-end tests (Playwright)" { npx playwright test } }
} finally { Pop-Location }

Write-Host "`n=== Summary"
$results.GetEnumerator() | ForEach-Object { "{0,-40} {1}" -f $_.Key, $_.Value }
if ($results.Values -contains "FAILED") { exit 1 }
