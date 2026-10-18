param([string]$PrivateRoot,[string]$ApprovedCommit,[switch]$Create,[switch]$TrustQaCa,[switch]$SelfTest)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Certificates.psm1') -Force
try {
    if ($SelfTest) { Invoke-QaCertificateMaterial '' '' -SelfTest; return }
    if (-not $Create -and -not $TrustQaCa) { Write-Output 'PREPARED ONLY: no certificate/private directory/trust store changed. Creation and QA CA trust each require separate operator approval.'; return }
    $PrivateRoot=Assert-QaPrivateRoot $PrivateRoot; Assert-QaPrivateTree $PrivateRoot
    Assert-QaCondition ($ApprovedCommit -cmatch '^[0-9a-f]{40}$') 'certificate-approved-source'
    if ($Create) { Invoke-QaCertificateMaterial $PrivateRoot $ApprovedCommit }
    if ($TrustQaCa) {
        $metadata=Get-Content -LiteralPath (Join-Path $PrivateRoot 'certificate-metadata.json') -Raw | ConvertFrom-Json
        $ca=New-Object Security.Cryptography.X509Certificates.X509Certificate2((Join-Path $PrivateRoot 'pg-root.crt'))
        try { Assert-QaCondition ($metadata.SourceCommit -ceq $ApprovedCommit -and $ca.Thumbprint -ceq $metadata.CaThumbprint) 'qa-ca-source-or-thumbprint' } finally { $ca.Dispose() }
        Import-Certificate -FilePath (Join-Path $PrivateRoot 'pg-root.crt') -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
        Write-Output 'PASS: only the recorded QA CA was added to the current-user trust store; no existing trust entry changed or removed.'
    }
} catch { throw (Get-QaFailureCode $_ 'certificates') }
