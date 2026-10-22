# One-time ordinary QA configuration only. No API/database startup or platform authority.
# Invoke in NORMAL Windows PowerShell after the approved host-copy installation.
param([Parameter(Mandatory=$true)][string]$PrivateRoot,
      [Parameter(Mandatory=$true)][string]$ApprovedCommit,
      [Parameter(Mandatory=$true)][string]$GoogleJsonPath)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
$phase='google-private-preflight'
try {
 $PrivateRoot=Assert-QaPrivateRoot $PrivateRoot;Assert-QaPrivateTree $PrivateRoot
 $null=Assert-QaRelease (Join-Path $PrivateRoot 'release') $ApprovedCommit
 foreach ($name in @('settings.json','process.json','admin-process.json','.settings.prepared.json')) { Assert-QaCondition (-not (Test-Path -LiteralPath (Join-Path $PrivateRoot $name))) 'qa-settings-or-host-already-present'
 }
 Assert-QaCondition (@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq 7443).Count -eq 0) 'qa-api-already-listening'
 $inputPath=[IO.Path]::GetFullPath($GoogleJsonPath)
 $repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\')
 Assert-QaCondition (-not $inputPath.StartsWith($repoRoot+'\',[StringComparison]::OrdinalIgnoreCase)) 'oauth-json-must-be-outside-repository'
 $oauthFile=Get-Item -LiteralPath $inputPath -Force
 Assert-QaCondition (-not $oauthFile.PSIsContainer -and -not ($oauthFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -and $oauthFile.Length -gt 0 -and $oauthFile.Length -le 65536) 'oauth-json-input-shape'
 $phase='google-web-client'
 $oauth=Get-Content -LiteralPath $inputPath -Raw | ConvertFrom-Json
 Assert-QaGoogleWebClient $oauth
 $settings=@{}
 foreach ($property in (Get-Content -LiteralPath (Join-Path $PrivateRoot 'settings.pending.json') -Raw | ConvertFrom-Json).PSObject.Properties) {
  Assert-QaCondition ($property.Value -is [string]) 'flat-string-settings-required'
  $settings[$property.Name]=[string]$property.Value
 }
 $settings['Authentication:Google:ClientId']=[string]$oauth.web.client_id
 $settings['Authentication:Google:ClientSecret']=[string]$oauth.web.client_secret
 $null=Assert-QaSettings $settings $PrivateRoot;Assert-QaCertificates $settings
 $phase='save-private-ordinary-settings'
 $target=Assert-QaPrivateMaterialPath (Join-Path $PrivateRoot 'settings.json') $PrivateRoot
 $temp=Assert-QaPrivateMaterialPath (Join-Path $PrivateRoot '.settings.prepared.json') $PrivateRoot
 $bytes=(New-Object Text.UTF8Encoding($false)).GetBytes(($settings | ConvertTo-Json))
 $stream=[IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
 try { $stream.Write($bytes,0,$bytes.Length);$stream.Flush() } finally { $stream.Dispose();[Array]::Clear($bytes,0,$bytes.Length) }
 Move-Item -LiteralPath $temp -Destination $target
 Assert-QaPrivateTree $PrivateRoot
 $oauth=$null;$settings.Clear()
 Write-Output 'PASS: private ordinary Google QA settings saved; exact localhost7443 origin/callback and both platform flags=false. No secret printed, service started or login verified. Do not rerun for daily startup.'
} catch { throw (Get-QaFailureCode $_ $phase) }
