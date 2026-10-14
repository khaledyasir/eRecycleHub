<#
.SYNOPSIS
  Runs the API (http://localhost:5080) and the web app (http://localhost:3000) for development, in two background jobs.
  Press Ctrl+C to stop both.

.PARAMETER Production
  Serve a production build of the web app (next build + next start) instead of the dev server.

.PARAMETER RelaxedRateLimits
  Raises the API rate limits so automated browser tests can sign in many times. Never use outside development.
#>
param([switch]$Production, [switch]$RelaxedRateLimits)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root "apps/api/src/ERecycleHub.Api"
$web = Join-Path $root "apps/web"

$apiEnv = @{ ASPNETCORE_ENVIRONMENT = "Development" }
if ($RelaxedRateLimits) { $apiEnv += @{ RateLimits__AuthPerMinute = "1000"; RateLimits__FormsPerMinute = "1000"; RateLimits__BidsPerMinute = "1000" } }

$jobs = @()
try {
  $jobs += Start-Job -Name api -ArgumentList $api, $apiEnv -ScriptBlock {
    param($dir, $envVars)
    foreach ($k in $envVars.Keys) { Set-Item "env:$k" $envVars[$k] }
    dotnet run --project $dir --launch-profile http
  }
  $jobs += Start-Job -Name web -ArgumentList $web, $Production.IsPresent -ScriptBlock {
    param($dir, $prod)
    Set-Location $dir
    if ($prod) { npm run build; npm run start } else { npm run dev }
  }
  Write-Host "API  http://localhost:5080/api/health"
  Write-Host "Web  http://localhost:3000"
  Write-Host "Dev e-mails are written to apps/api/src/ERecycleHub.Api/App_Data/mail. Ctrl+C stops both.`n"
  while ($true) { $jobs | Receive-Job; Start-Sleep -Seconds 2 }
} finally {
  $jobs | Stop-Job -ErrorAction SilentlyContinue
  $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
}
