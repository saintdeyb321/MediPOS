# .NET Framework BCL only, compatible with Windows PowerShell5.1. No external OpenSSL/install needed.
Set-StrictMode -Version 2.0
function New-QaEphemeralRsa {
    # RSACng creates an exportable ephemeral key. Passing $null as a CngKey name via
    # Windows PowerShell's binder becomes an empty persisted name instead.
    $rsa=New-Object Security.Cryptography.RSACng(3072)
    Assert-QaCondition $rsa.Key.IsEphemeral 'certificate-key-must-be-ephemeral'
    return [pscustomobject]@{Key=$rsa.Key;Rsa=$rsa}
}
function New-QaCertificateRequest([string]$Subject,$Rsa,[bool]$Ca) {
    $request=New-Object Security.Cryptography.X509Certificates.CertificateRequest($Subject,$Rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($Ca,$false,0,$true)))
    $usage=if ($Ca) { [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign } else { [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment }
    $request.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509KeyUsageExtension($usage,$true)))
    if (-not $Ca) {
        $san=New-Object Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder; $san.AddDnsName('localhost'); $request.CertificateExtensions.Add($san.Build())
        $oids=New-Object Security.Cryptography.OidCollection; $null=$oids.Add((New-Object Security.Cryptography.Oid('1.3.6.1.5.5.7.3.1')))
        $request.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($oids,$false)))
    }
    return $request
}
function Get-QaRandomSecret {
    $bytes=New-Object byte[] 48; $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_') } finally { $rng.Dispose() }
}
function ConvertTo-QaPem([string]$Kind,[byte[]]$Bytes) {
    return ('-----BEGIN '+$Kind+'-----' + "`n" + [Convert]::ToBase64String($Bytes,[Base64FormattingOptions]::InsertLineBreaks).Replace("`r`n","`n") + "`n" + '-----END '+$Kind+'-----' + "`n")
}
function Invoke-QaCertificateMaterial([string]$Root,[string]$Commit,[switch]$SelfTest) {
    if (-not $SelfTest) {
        $Root=Assert-QaPrivateRoot $Root; Assert-QaPrivateTree $Root
        Assert-QaCondition ($Commit -cmatch '^[0-9a-f]{40}$') 'certificate-approved-source'
        foreach ($file in @('pg-root.crt','postgres-server.crt','postgres-server.key','tls.pfx','dp.pfx','certificate-secrets.json','certificate-metadata.json')) { Assert-QaCondition (-not (Test-Path -LiteralPath (Join-Path $Root $file))) 'certificate-material-already-exists' }
    }
    $keys=@(); $certs=@(); $pgPrivate=$null; $start=[DateTimeOffset]::UtcNow.AddMinutes(-5); $end=[DateTimeOffset]::UtcNow.AddDays(90)
    try {
        $caKey=New-QaEphemeralRsa; $keys+= $caKey
        $caRequest=New-QaCertificateRequest 'CN=MediPOS Disposable QA CA' $caKey.Rsa $true
        $ca=$caRequest.CreateSelfSigned($start,$end.AddDays(1)); $certs+=$ca
        $created=@{}
        foreach ($kind in @('postgres','tls','dp')) {
            $key=New-QaEphemeralRsa; $keys+=$key
            $request=New-QaCertificateRequest 'CN=localhost' $key.Rsa $false
            $serial=New-Object byte[] 16; $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($serial) } finally { $rng.Dispose() }
            $serial[0]=$serial[0] -band 127; $serial[15]=$serial[15] -bor 1
            $public=$request.Create($ca,$start,$end,$serial)
            try { $certificate=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey($public,$key.Rsa) } finally { $public.Dispose() }
            $certs+=$certificate; $created[$kind]=$certificate
            # CNG has a standard PKCS#8 export on .NET Framework; no custom ASN.1/crypto implementation.
            if ($kind -eq 'postgres') { $pgPrivate=$key.Key.Export([Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob) }
        }
        $tlsPassword=Get-QaRandomSecret; $dpPassword=Get-QaRandomSecret
        $tlsBytes=$created['tls'].Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$tlsPassword)
        $dpBytes=$created['dp'].Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$dpPassword)
        if ($SelfTest) {
            $test=New-Object Security.Cryptography.X509Certificates.X509Certificate2($tlsBytes,$tlsPassword,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
            try { Assert-QaCondition ($test.HasPrivateKey -and $test.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::DnsName,$false) -ceq 'localhost' -and $created['tls'].Thumbprint -cne $created['dp'].Thumbprint -and $pgPrivate.Length -gt 100) 'certificate-memory-roundtrip' } finally { $test.Dispose() }
            Write-Output 'PASS: RSA3072/CA/SAN/PFX/PKCS8 memory roundtrip on PowerShell5.1; no files, certificate store, listener or QA credential created.'
            return
        }
        $utf8=New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText((Join-Path $Root 'pg-root.crt'),(ConvertTo-QaPem 'CERTIFICATE' $ca.RawData),$utf8)
        [IO.File]::WriteAllText((Join-Path $Root 'postgres-server.crt'),(ConvertTo-QaPem 'CERTIFICATE' $created['postgres'].RawData),$utf8)
        [IO.File]::WriteAllText((Join-Path $Root 'postgres-server.key'),(ConvertTo-QaPem 'PRIVATE KEY' $pgPrivate),$utf8)
        [IO.File]::WriteAllBytes((Join-Path $Root 'tls.pfx'),$tlsBytes); [IO.File]::WriteAllBytes((Join-Path $Root 'dp.pfx'),$dpBytes)
        [IO.File]::WriteAllText((Join-Path $Root 'certificate-secrets.json'),(@{TlsPassword=$tlsPassword;DataProtectionPassword=$dpPassword} | ConvertTo-Json),$utf8)
        [IO.File]::WriteAllText((Join-Path $Root 'certificate-metadata.json'),(@{SourceCommit=$Commit;CaThumbprint=$ca.Thumbprint;TlsThumbprint=$created['tls'].Thumbprint;DataProtectionThumbprint=$created['dp'].Thumbprint} | ConvertTo-Json),$utf8)
        Assert-QaPrivateTree $Root
        Write-Output 'PASS: new independent QA certificate material written only in the protected QA directory. Not trusted or started; CA signing key was not persisted.'
    } finally {
        foreach ($certificate in $certs) { $certificate.Dispose() }
        foreach ($key in $keys) { $key.Rsa.Dispose(); $key.Key.Dispose() }
        if ($pgPrivate) { [Array]::Clear($pgPrivate,0,$pgPrivate.Length) }
    }
}
Export-ModuleMember -Function Invoke-QaCertificateMaterial
