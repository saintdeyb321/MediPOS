# Revocation only; cannot create or renew administrative authority.
param([string]$PrivateRoot=(Join-Path $env:USERPROFILE 'AppData\Roaming\MediPOS\DevAdministration\session-7243'),[switch]$Close)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalDev.Admin.psm1') -Force
try {
 if (-not $Close) { Write-Output 'No action. -Close revokes only the recorded DEV child and verifies/restores ordinary hosting.';return }
 Close-DevAdminWindow $PrivateRoot
} catch { throw (Get-DevFailure $_) }
