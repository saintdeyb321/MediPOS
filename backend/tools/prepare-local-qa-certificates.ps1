param([string]$PrivateRoot,[string]$ApprovedCommit,[switch]$Create,[switch]$TrustQaCa,[switch]$RemoveQaCa,[string]$ExpectedCaThumbprint,[switch]$SelfTest)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalQa.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LocalQa.Certificates.psm1') -Force
try {
    Assert-QaCondition (-not ($RemoveQaCa -and ($Create -or $TrustQaCa -or $SelfTest)) -and -not ($SelfTest -and ($Create -or $TrustQaCa))) 'certificate-actions-conflict'
    if ($SelfTest) { Invoke-QaCertificateMaterial '' '' -SelfTest; return }
    if (-not $Create -and -not $TrustQaCa -and -not $RemoveQaCa) { Write-Output 'PREPARED ONLY: no certificate/private directory/trust store changed. Creation, QA CA trust and exact-thumbprint removal require explicit approval.'; return }
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
    if ($RemoveQaCa) {
        # Separate cleanup approval is required; never enumerate/delete other roots or use wildcards.
        foreach ($name in @('process.json','admin-process.json','cluster-process.json')) { Assert-QaCondition (-not (Test-Path -LiteralPath (Join-Path $PrivateRoot $name))) 'qa-ca-removal-requires-stopped-resources' }
        Assert-QaCondition (@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -in @(7443,55432) }).Count -eq 0) 'qa-ca-removal-ports-occupied'
        $metadata=Get-Content -LiteralPath (Join-Path $PrivateRoot 'certificate-metadata.json') -Raw | ConvertFrom-Json
        $ca=New-Object Security.Cryptography.X509Certificates.X509Certificate2((Join-Path $PrivateRoot 'pg-root.crt'))
        try { $target=Get-QaCaRemovalPath $metadata $ApprovedCommit $ExpectedCaThumbprint $ca.Thumbprint } finally { $ca.Dispose() }
        if (Test-Path -LiteralPath $target) {
            $stored=Get-Item -LiteralPath $target
            try { Assert-QaCondition ($stored.Thumbprint -ieq $ExpectedCaThumbprint) 'qa-ca-removal-stored-thumbprint' } finally { $stored.Dispose() }
            Remove-Item -LiteralPath $target
        }
        Assert-QaCondition (-not (Test-Path -LiteralPath $target)) 'qa-ca-removal-incomplete'
        Write-Output 'PASS: the exact recorded QA CA is absent from CurrentUser Root; no other certificate or private file changed.'
    }
} catch { throw (Get-QaFailureCode $_ 'certificates') }
