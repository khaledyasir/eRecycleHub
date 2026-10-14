<#
.SYNOPSIS
  Migrates data from a LEGACY eRecycleHub SQL Server database into a NEW, EMPTY target database. Read-only on the source.

.DESCRIPTION
  Defaults to a DRY RUN: everything is read, converted and validated and a report is written, but nothing is saved.
  Pass -Execute to write to the target. The tool refuses a non-empty target database. Passwords stored as plaintext in the
  legacy data are hashed; v1 PBKDF2 hashes are kept and upgraded on first login. See docs/DATABASE-MAPPING.md and the
  import report for the conversions applied (Central time -> UTC, float -> decimal, duplicate e-mails, wallet reconciliation).

.PARAMETER Source
  Connection string of the legacy database (used read-only). Use a restored COPY, not the live production database.

.PARAMETER Target
  Connection string of the new, empty database (already migrated: ./scripts/setup.ps1 or `dotnet run -- migrate`).

.PARAMETER LegacyFiles
  Folder that CONTAINS the legacy `App_File` uploads folder (e.g. the backup made when the legacy project was removed), copied into the new file store.

.PARAMETER Storage
  Folder of the new file store (the API's Storage:Root).

.EXAMPLE
  ./scripts/import-legacy.ps1 -Source "Server=localhost;Database=ErecycleHubCopy;Integrated Security=True;TrustServerCertificate=True" `
     -Target "Server=localhost;Database=ERecycleHub_Import;Integrated Security=True;TrustServerCertificate=True" `
     -LegacyFiles C:\Users\khale\Desktop\sites\GIT\legacy-local-files-backup -Storage C:\data\erecyclehub\files -Report C:\data\import-report
#>
param(
  [Parameter(Mandatory)] [string]$Source,
  [Parameter(Mandatory)] [string]$Target,
  [string]$LegacyFiles,
  [string]$Storage,
  [string]$Report = "import-report",
  [switch]$Execute
)

$ErrorActionPreference = "Stop"
$tool = Join-Path (Split-Path -Parent $PSScriptRoot) "apps/api/tools/ERecycleHub.LegacyImport"
$importArgs = @("--source", $Source, "--target", $Target, "--report", $Report)
if ($LegacyFiles) { $importArgs += @("--files", $LegacyFiles) }
if ($Storage) { $importArgs += @("--storage", $Storage) }
if (-not $Execute) { $importArgs += "--dry-run"; Write-Host "DRY RUN - nothing will be written. Add -Execute to import." }
dotnet run --project $tool -c Release -- @importArgs
exit $LASTEXITCODE
