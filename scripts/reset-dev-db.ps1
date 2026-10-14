<#
.SYNOPSIS
  Drops and recreates the DEVELOPMENT database with sample data, for a clean slate before an end-to-end run.

.DESCRIPTION
  Deliberately narrow: it only ever acts on a database whose name is exactly ERecycleHub_Dev on the local SQL Server
  instance, and only when you pass -Yes. Anything else is refused. There is no way to point it at another server or name.
#>
param([switch]$Yes)

$ErrorActionPreference = "Stop"
if (-not $Yes) { throw "This DROPS the local database ERecycleHub_Dev. Re-run with -Yes to confirm." }

$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root "apps/api/src/ERecycleHub.Api"
$db = "ERecycleHub_Dev"   # fixed on purpose
if ($db -ne "ERecycleHub_Dev") { throw "refusing to touch $db" }

$cn = "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True"
$sql = "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END"
$conn = New-Object System.Data.SqlClient.SqlConnection $cn
$conn.Open()
try { $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; [void]$cmd.ExecuteNonQuery() } finally { $conn.Dispose() }
Write-Host "Dropped $db."

$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project $api -- migrate
if ($LASTEXITCODE) { throw "migrate failed" }
dotnet run --project $api --no-build -- seed-dev
if ($LASTEXITCODE) { throw "seed-dev failed" }
Write-Host "Fresh $db with sample data."
