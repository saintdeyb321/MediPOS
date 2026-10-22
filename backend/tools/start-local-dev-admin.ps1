# No privilege/configuration writes by default. -Activate requires independent explicit approval.
param([Parameter(Mandatory=$true)][Guid]$ActorId,
 [string]$PrivateRoot=(Join-Path $env:USERPROFILE 'AppData\Roaming\MediPOS\DevAdministration\session-7243'),
 [string]$CandidateRoot=(Join-Path $PSScriptRoot '..\obj\local-dev-admin\candidate'),
 [string]$ApprovedManifestSha256,[string]$TlsThumbprint,[switch]$Activate)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalDev.Admin.psm1') -Force
try {
 if (-not $Activate) { Get-DevInspection $ActorId | ConvertTo-Json -Depth 4;Write-Output 'READ ONLY: no privilege/process/configuration/migration change. Pending migrations require separate approval.';return }
 Invoke-DevAdminWindow $PrivateRoot ([IO.Path]::GetFullPath($CandidateRoot)) $ApprovedManifestSha256 $ActorId $TlsThumbprint
} catch { throw (Get-DevFailure $_) }
