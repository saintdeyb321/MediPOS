# Windows PowerShell 5.1. Preparation/guards only; importing this module has no side effects.
Set-StrictMode -Version 2.0

function Assert-QaCondition([bool]$Condition, [string]$Code) {
    if (-not $Condition) { throw ('QA NO-GO: ' + $Code) }
}

function Assert-QaChildPath([string]$Path, [string]$Root) {
    Assert-QaCondition ([IO.Path]::IsPathRooted($Path)) 'absolute-path-required'
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    Assert-QaCondition ($full.StartsWith($parent + '\', [StringComparison]::OrdinalIgnoreCase)) 'path-outside-approved-root'
    return $full
}

function Assert-QaPrivateMaterialPath([string]$Path, [string]$Root) {
    $full = Assert-QaChildPath $Path $Root
    $release = Join-Path ([IO.Path]::GetFullPath($Root)) 'release'
    Assert-QaCondition ($full -ine $release -and -not $full.StartsWith($release + '\', [StringComparison]::OrdinalIgnoreCase)) 'private-material-in-release'
    return $full
}

function Assert-QaPrivateRoot([string]$Path) {
    $base = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'MediPOS\QA'))
    $root = Assert-QaChildPath $Path $base
    Assert-QaCondition ($root.Substring($base.Length+1) -cmatch '^[a-z0-9][a-z0-9_-]{0,31}$') 'qa-root-run-name'
    $ancestor = Get-Item -LiteralPath $root -Force
    while ($ancestor -and $ancestor.FullName.Length -ge $env:LOCALAPPDATA.Length) {
        Assert-QaCondition (-not ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'private-ancestor-reparse-point'
        $ancestor = $ancestor.Parent
    }
    return $root
}

function Assert-QaSettings([hashtable]$Settings, [string]$PrivateRoot) {
    $required = @{
        'Authentication:Google:Enabled' = 'true'; 'TenantApi:Enabled' = 'true'
        'PlatformAdministration:Enabled' = 'false'; 'PlatformApi:Enabled' = 'false'
        'Authentication:Google:PublicOrigin' = 'https://localhost:7443'; 'Authentication:Google:HttpsMode' = 'Direct'
        'Authentication:Google:SessionMinutes' = '30'; 'AllowedHosts' = 'localhost'
        'Kestrel:Endpoints:Qa:Url' = 'https://127.0.0.1:7443'
        'LocalFrontend:Enabled' = 'false'; 'PublishedFrontend:Enabled' = 'true'
        'Notifications:ExpirationAlerts:Enabled' = 'false'
    }
    $extra = @('ConnectionStrings:MediPosDatabase', 'Authentication:Google:ClientId', 'Authentication:Google:ClientSecret',
        'Authentication:Google:DataProtection:KeyRingPath', 'Authentication:Google:DataProtection:CertificatePath',
        'Authentication:Google:DataProtection:CertificatePassword', 'Kestrel:Endpoints:Qa:Certificate:Path',
        'Kestrel:Endpoints:Qa:Certificate:Password', 'PublishedFrontend:RootPath')
    foreach ($key in $Settings.Keys) { Assert-QaCondition ($required.ContainsKey($key) -or $key -cin $extra) 'unreviewed-setting' }
    foreach ($key in $required.Keys) { Assert-QaCondition ($Settings[$key] -ceq $required[$key]) 'host-origin-or-authority-setting' }
    foreach ($key in $extra) { Assert-QaCondition (-not [string]::IsNullOrWhiteSpace($Settings[$key])) 'incomplete-private-settings' }
    Assert-QaCondition ($Settings['Authentication:Google:ClientId'] -cmatch '^[A-Za-z0-9.-]+\.apps\.googleusercontent\.com$') 'google-client-id'
    Assert-QaCondition ($Settings['Authentication:Google:ClientSecret'].Length -ge 16 -and $Settings['Authentication:Google:ClientSecret'] -notmatch '\s') 'google-client-secret'
    foreach ($key in @('Authentication:Google:DataProtection:CertificatePassword', 'Kestrel:Endpoints:Qa:Certificate:Password')) {
        Assert-QaCondition ($Settings[$key].Length -ge 32) 'certificate-password'
    }
    foreach ($key in @('Authentication:Google:DataProtection:KeyRingPath', 'Authentication:Google:DataProtection:CertificatePath', 'Kestrel:Endpoints:Qa:Certificate:Path')) {
        $null = Assert-QaPrivateMaterialPath $Settings[$key] $PrivateRoot
    }
    Assert-QaCondition ($Settings['PublishedFrontend:RootPath'] -ieq (Join-Path $PrivateRoot 'release\web')) 'static-build-directory'
    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    try { $connection.set_ConnectionString($Settings['ConnectionStrings:MediPosDatabase']) } catch { throw 'QA NO-GO: connection-format' }
    foreach ($key in $connection.Keys) { Assert-QaCondition ($key -in @('Host', 'Port', 'Database', 'Username', 'Passfile', 'SSL Mode', 'Root Certificate', 'Pooling')) 'connection-option' }
    Assert-QaCondition ($connection['Host'] -eq 'localhost' -and [string]$connection['Port'] -eq '55432') 'independent-loopback-postgresql-required'
    Assert-QaCondition ([string]$connection['Database'] -cmatch '^medipos_qa_[a-z0-9_]{1,25}$') 'qa-database-required'
    Assert-QaCondition ($connection['Username'] -ceq ($connection['Database'] + '_runtime')) 'qa-runtime-required'
    Assert-QaCondition ($connection['SSL Mode'] -ceq 'VerifyFull') 'postgresql-verified-tls-required'
    foreach ($key in @('Passfile', 'Root Certificate')) { $null = Assert-QaPrivateMaterialPath ([string]$connection[$key]) $PrivateRoot }
    return $connection
}

function Assert-QaPrivateTree([string]$Root) {
    $allowed = @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18')
    $items = @((Get-Item -LiteralPath $Root -Force)) + @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    foreach ($item in $items) {
        Assert-QaCondition (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'private-reparse-point'
        $acl = Get-Acl -LiteralPath $item.FullName
        foreach ($rule in $acl.Access) {
            if ($rule.AccessControlType -eq 'Allow') {
                Assert-QaCondition ($rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -in $allowed) 'private-acl-too-broad'
            }
        }
    }
}

function Get-QaFileManifest([string]$Root) {
    $rows = @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    Assert-QaCondition (@($rows | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -eq 0) 'release-reparse-point'
    return @($rows | Where-Object { -not $_.PSIsContainer -and $_.FullName -ne (Join-Path $Root 'manifest.json') } | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path = $_.FullName.Substring($Root.Length + 1).Replace('\', '/'); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}

function Get-QaRlsInventory([string]$Sql, [string[]]$Tables) {
    $names = @([regex]::Matches($Sql, '(?i)ALTER TABLE\s+(?:public\.)?"?([a-z_][a-z0-9_]+)"?\s+FORCE ROW LEVEL SECURITY') | ForEach-Object { $_.Groups[1].Value })
    # UBL/CPE migrations use two reviewed static FOREACH target arrays, not literal ALTER TABLE names.
    foreach ($loop in [regex]::Matches($Sql, '(?is)FOREACH target IN ARRAY\s+ARRAY\[(?<tables>[^\]]+)\]\s+LOOP(?<body>.*?)END LOOP')) {
        if ($loop.Groups['body'].Value.Contains('FORCE ROW LEVEL SECURITY')) {
            $names += @([regex]::Matches($loop.Groups['tables'].Value, "'([a-z_][a-z0-9_]+)'") | ForEach-Object { $_.Groups[1].Value })
        }
    }
    $names = @($names | Sort-Object -Unique)
    # These five roots are deliberately global. Every other existing model table is protected, including global official-catalog administration.
    $expected = @($Tables | Where-Object { $_ -notin @('tenants','users','categories','global_products','medicine_profiles') } | Sort-Object -Unique)
    Assert-QaCondition (($names -join ',') -ceq ($expected -join ',')) 'model-sql-rls-inventory-mismatch'
    return $names
}

function Get-QaDatabaseGuards([string]$Sql, [string]$Snapshot) {
    $state = @{}
    foreach ($item in [regex]::Matches($Sql,'(?im)\b(?<op>CREATE(?:\s+OR\s+REPLACE)?|DROP)\s+(?:CONSTRAINT\s+)?(?<kind>TRIGGER|FUNCTION)\s+(?:IF\s+EXISTS\s+)?(?:public\.)?"?(?<name>[A-Za-z_][A-Za-z0-9_]*)"?')) {
        $state[$item.Groups['kind'].Value.ToUpperInvariant()+':'+$item.Groups['name'].Value] = $item.Groups['op'].Value.StartsWith('CREATE',[StringComparison]::OrdinalIgnoreCase)
    }
    $constraints = @([regex]::Matches($Snapshot,'(?:HasName|HasConstraintName|HasCheckConstraint)\("([A-Za-z_][A-Za-z0-9_]*)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    return [pscustomobject]@{
        Constraints=$constraints
        Triggers=@($state.Keys | Where-Object { $_.StartsWith('TRIGGER:') -and $state[$_] } | ForEach-Object { $_.Substring(8) } | Sort-Object)
        Functions=@($state.Keys | Where-Object { $_.StartsWith('FUNCTION:') -and $state[$_] } | ForEach-Object { $_.Substring(9) } | Sort-Object)
    }
}

function Assert-QaRelease([string]$Root, [string]$ApprovedCommit) {
    Assert-QaCondition ($ApprovedCommit -cmatch '^[0-9a-f]{40}$') 'exact-approved-commit-required'
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $Root 'manifest.json') | ConvertFrom-Json
    Assert-QaCondition ($manifest.Format -eq 2) 'qa-package-format-review-required'
    Assert-QaCondition ($manifest.SourceCommit -ceq $ApprovedCommit -and $manifest.FrontendCommit -ceq $ApprovedCommit) 'mixed-source-commits'
    $actual = @(Get-QaFileManifest $Root); $expected = @($manifest.Files | Sort-Object Path)
    Assert-QaCondition (@($actual | Where-Object { $_.Path -match '(?i)(\.(pfx|pem|key)$|(^|/)(secrets\.json|pgpass\.conf)$)' }).Count -eq 0) 'private-file-in-release'
    $actual = @($actual | Sort-Object Path)
    Assert-QaCondition ($expected.Count -eq $actual.Count -and $expected.Count -gt 10) 'release-file-count'
    for ($index = 0; $index -lt $expected.Count; $index++) {
        Assert-QaCondition ($expected[$index].Path -ceq $actual[$index].Path -and $expected[$index].Length -eq $actual[$index].Length -and $expected[$index].Sha256 -eq $actual[$index].Sha256) 'release-file-integrity'
    }
    Assert-QaCondition (@($manifest.Migrations).Count -eq 37 -and @($manifest.RlsTables).Count -eq 54 -and @($manifest.Tables).Count -eq 59) 'expected-schema-inventory'
    foreach ($name in @($manifest.Tables) + @($manifest.RlsTables)) { Assert-QaCondition ($name -cmatch '^[a-z_][a-z0-9_]+$') 'schema-identifier' }
    foreach ($name in $manifest.Migrations) { Assert-QaCondition ($name -cmatch '^\d{14}_[A-Za-z0-9]+$') 'migration-identifier' }
    foreach ($list in @($manifest.Guards.Constraints,$manifest.Guards.Triggers,$manifest.Guards.Functions)) {
        Assert-QaCondition (@($list).Count -gt 0) 'empty-database-guard-inventory'
        foreach ($name in $list) { Assert-QaCondition ($name -cmatch '^[A-Za-z_][A-Za-z0-9_]{0,127}$') 'database-guard-identifier' }
    }
    $rls = @(Get-QaRlsInventory ([IO.File]::ReadAllText((Join-Path $Root 'schema.sql'))) $manifest.Tables)
    Assert-QaCondition (($rls -join ',') -ceq (@($manifest.RlsTables | Sort-Object) -join ',')) 'manifest-rls-inventory'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Root 'app\MediPOS.Api.dll')).ProductVersion
    Assert-QaCondition ($version.EndsWith('+' + $ApprovedCommit, [StringComparison]::Ordinal)) 'assembly-source-commit'
    $html = Get-Content -Raw -LiteralPath (Join-Path $Root 'web\index.html')
    Assert-QaCondition ($html.Contains('<meta name="medipos-source-commit" content="' + $ApprovedCommit + '">')) 'frontend-source-commit'
    return $manifest
}

function Assert-QaCertificates([hashtable]$Settings) {
    $thumbprints = @()
    foreach ($kind in @('Kestrel:Endpoints:Qa:Certificate', 'Authentication:Google:DataProtection')) {
        $path = if ($kind -like 'Kestrel*') { $Settings[$kind + ':Path'] } else { $Settings[$kind + ':CertificatePath'] }
        $password = if ($kind -like 'Kestrel*') { $Settings[$kind + ':Password'] } else { $Settings[$kind + ':CertificatePassword'] }
        $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($path, $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        try {
            Assert-QaCondition ($certificate.HasPrivateKey -and $certificate.NotBefore.ToUniversalTime() -le [DateTime]::UtcNow -and $certificate.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow) 'certificate-private-key-or-validity'
            $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
            try { Assert-QaCondition ($null -ne $rsa -and $rsa.KeySize -ge 2048) 'certificate-rsa-key' } finally { if ($rsa) { $rsa.Dispose() } }
            if ($kind -like 'Kestrel*') {
                Assert-QaCondition ($certificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false) -ceq 'localhost') 'tls-localhost-name'
                $chain = New-Object Security.Cryptography.X509Certificates.X509Chain
                try {
                    $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
                    Assert-QaCondition ($chain.Build($certificate)) 'tls-not-trusted'
                } finally { $chain.Dispose() }
            }
            $thumbprints += $certificate.Thumbprint
        } finally { $certificate.Dispose() }
    }
    Assert-QaCondition ($thumbprints[0] -cne $thumbprints[1]) 'separate-tls-and-data-protection-certificates-required'
}

function Assert-QaClusterRecord($Record, [string]$Root, [string]$ApprovedCommit, $Connection) {
    Assert-QaCondition ($Record.Format -eq 1 -and $Record.Discardable -eq $true -and $Record.SourceCommit -ceq $ApprovedCommit) 'cluster-record-source'
    Assert-QaCondition ($Record.Host -ceq 'localhost' -and $Record.Port -eq 55432 -and $Record.SystemIdentifier -cmatch '^\d{16,20}$') 'cluster-record-identity'
    Assert-QaCondition ($Record.Database -ceq [string]$Connection['Database'] -and $Record.RuntimeRole -ceq [string]$Connection['Username']) 'cluster-record-database'
    $data = Assert-QaChildPath $Record.DataDirectory $Root
    Assert-QaCondition ($data -ieq (Join-Path $Root 'postgres-data')) 'cluster-record-data-directory'
}

function Get-QaFailureCode($Failure, [string]$Phase) {
    $code = 'unexpected-error-details-withheld'
    if ($Failure.Exception.Message -cmatch '^QA NO-GO: ([a-z][a-z0-9_-]{0,100})$') { $code = $Matches[1] }
    return ('QA NO-GO [' + $Phase + '/' + $code + ']')
}
function Get-QaInheritedOverrideNames([string[]]$Names) {
    # PG* can contain inherited credentials or TLS defaults. Never pass those to a QA API child.
    return @($Names | Where-Object { $_ -match '^(PG|ASPNETCORE_|DOTNET_|ConnectionStrings__|Authentication__|PlatformAdministration__|PlatformApi__|TenantApi__|Kestrel__|LocalFrontend__|PublishedFrontend__|Notifications__|Logging__|AllowedHosts$)' })
}

function Invoke-QaRuntimeProbe($Connection, $Manifest, [string]$PsqlPath, [string]$ExpectedClusterIdentifier, [string]$ApprovedGoogleActor = '') {
    # Executed only by an explicitly approved -Start. No migrations, grants, DDL or business writes.
    Assert-QaCondition (Test-Path -LiteralPath $PsqlPath -PathType Leaf) 'psql18-required'
    Assert-QaCondition ([IO.Path]::GetFullPath($PsqlPath) -ieq 'C:\Program Files\PostgreSQL\18\bin\psql.exe') 'pinned-psql18-executable'
    Assert-QaCondition ($ExpectedClusterIdentifier -cmatch '^\d{16,20}$') 'expected-cluster-identifier-required'
    $actorCheck = 'true'
    if ($ApprovedGoogleActor) {
        $actor = [Guid]::Empty
        Assert-QaCondition ([Guid]::TryParseExact($ApprovedGoogleActor,'D',[ref]$actor) -and $actor -ne [Guid]::Empty) 'approved-actor-identifier'
        $actorCheck = "EXISTS(SELECT 1 FROM users WHERE id='$($actor.ToString('D'))'::uuid AND google_subject<>'')"
    }
    $database = [string]$Connection['Database']; $role = [string]$Connection['Username']
    Assert-QaCondition ($database -cmatch '^medipos_qa_[a-z0-9_]{1,25}$' -and $role -ceq ($database + '_runtime')) 'probe-qa-identity'
    foreach ($name in @($Manifest.Tables) + @($Manifest.RlsTables)) { Assert-QaCondition ($name -cmatch '^[a-z_][a-z0-9_]+$') 'probe-schema-identifier' }
    $tables = (@($Manifest.Tables) | ForEach-Object { "'" + $_ + "'" }) -join ','
    $rls = (@($Manifest.RlsTables) | ForEach-Object { "'" + $_ + "'" }) -join ','
    foreach ($name in @($Manifest.Guards.Constraints)+@($Manifest.Guards.Triggers)+@($Manifest.Guards.Functions)) { Assert-QaCondition ($name -cmatch '^[A-Za-z_][A-Za-z0-9_]{0,127}$') 'probe-guard-identifier' }
    $constraints = (@($Manifest.Guards.Constraints) | ForEach-Object { "'"+$_+"'" }) -join ','
    $triggers = (@($Manifest.Guards.Triggers) | ForEach-Object { "'"+$_+"'" }) -join ','
    $functions = (@($Manifest.Guards.Functions) | ForEach-Object { "'"+$_+"'" }) -join ','
    $sql = @"
BEGIN READ ONLY;
SET LOCAL statement_timeout = '10s';
SELECT json_build_object(
 'registered_google_actor', $actorCheck,
 'cluster', (SELECT system_identifier::text FROM pg_catalog.pg_control_system())='$ExpectedClusterIdentifier'
   AND inet_server_port()=55432 AND inet_server_addr() IN ('127.0.0.1'::inet,'::1'::inet),
 'identity', current_database()='$database' AND session_user='$role' AND current_user=session_user,
 'postgres18', current_setting('server_version_num')::int BETWEEN 180000 AND 189999,
 'restricted', NOT EXISTS(SELECT 1 FROM pg_roles r WHERE pg_has_role(session_user,r.oid,'MEMBER') AND
   (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolname IN ('medipos_catalog_importer','medipos_catalog_reviewer','medipos_catalog_publisher')))
   AND NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND pg_has_role(session_user,c.relowner,'MEMBER'))
   AND NOT has_schema_privilege(session_user,'public','CREATE') AND NOT has_database_privilege(session_user,current_database(),'CREATE'),
 'tables', (SELECT count(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ($tables)) = $(@($Manifest.Tables).Count),
 'guards', (SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE n.nspname='public' AND c.conname IN ($constraints) AND c.convalidated) = $(@($Manifest.Guards.Constraints).Count)
   AND (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND t.tgname IN ($triggers) AND NOT t.tgisinternal AND t.tgenabled IN ('O','A')) = $(@($Manifest.Guards.Triggers).Count)
   AND (SELECT count(DISTINCT p.proname) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname IN ($functions)) = $(@($Manifest.Guards.Functions).Count),
 'rls', (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname IN ($rls) AND c.relrowsecurity AND c.relforcerowsecurity AND EXISTS(SELECT 1 FROM pg_policy p WHERE p.polrelid=c.oid)) = $(@($Manifest.RlsTables).Count),
 'no_ddl', NOT EXISTS(SELECT 1 FROM pg_tables WHERE schemaname='public' AND has_table_privilege(session_user,quote_ident(schemaname)||'.'||quote_ident(tablename),'TRUNCATE,REFERENCES,TRIGGER')),
 'append_only', NOT EXISTS(SELECT 1 FROM pg_tables WHERE schemaname='public' AND tablename IN ('audit_logs','license_changes','transfer_events','electronic_document_attempts','commission_entries','stock_movements') AND
   (has_any_column_privilege(session_user,'public.'||tablename,'UPDATE') OR has_table_privilege(session_user,'public.'||tablename,'DELETE'))),
 'column_grants', NOT has_table_privilege(session_user,'public.platform_tenant_creation_requests','INSERT,UPDATE,DELETE')
   AND NOT has_table_privilege(session_user,'public.employee_invitations','UPDATE,DELETE')
   AND has_table_privilege(session_user,'public.employee_invitations','SELECT') AND has_table_privilege(session_user,'public.employee_invitations','INSERT')
   AND has_table_privilege(session_user,'public.platform_tenant_creation_requests','SELECT')
   AND NOT EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid='public.platform_tenant_creation_requests'::regclass AND a.attnum>0 AND NOT a.attisdropped AND
     (has_column_privilege(session_user,a.attrelid,a.attnum,'INSERT') IS DISTINCT FROM (a.attname IN ('actor_id','operation','request_key','payload_hash','created_at'))
      OR has_column_privilege(session_user,a.attrelid,a.attnum,'UPDATE') IS DISTINCT FROM (a.attname IN ('tenant_id','license_id','status','starts_at','expires_at','max_branches'))))
   AND NOT EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid='public.employee_invitations'::regclass AND a.attnum>0 AND NOT a.attisdropped
     AND has_column_privilege(session_user,a.attrelid,a.attnum,'UPDATE') IS DISTINCT FROM
       (a.attname IN ('accepted_user_id','accepted_at','status','approved_at','result_membership_id','approved_from_version','revoked_at','revoked_by_user_id','revocation_reason'))),
 'migrations', (SELECT json_agg("MigrationId" ORDER BY "MigrationId") FROM "__EFMigrationsHistory"));
ROLLBACK;
"@
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $PsqlPath; $info.Arguments = '-X -w -q -A -t -h localhost -p 55432 -U ' + $role + ' -d ' + $database + ' -v ON_ERROR_STOP=1'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($key in @($info.EnvironmentVariables.Keys)) { if ($key -like 'PG*') { $info.EnvironmentVariables.Remove($key) } }
    $info.EnvironmentVariables['PGPASSFILE'] = [string]$Connection['Passfile']
    $info.EnvironmentVariables['PGSSLMODE'] = 'verify-full'; $info.EnvironmentVariables['PGSSLROOTCERT'] = [string]$Connection['Root Certificate']
    $process = New-Object Diagnostics.Process; $process.StartInfo = $info
    try {
        $null = $process.Start(); $output = $process.StandardOutput.ReadToEndAsync(); $errors = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($sql); $process.StandardInput.Close()
        if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'QA NO-GO: runtime-probe-timeout' }
        Assert-QaCondition ($process.ExitCode -eq 0) 'runtime-probe-failed-details-withheld'
        $result = $output.Result | ConvertFrom-Json; $null = $errors.Result
        foreach ($key in @('registered_google_actor','cluster','identity','postgres18','restricted','tables','guards','rls','no_ddl','append_only','column_grants')) { Assert-QaCondition ($result.$key -eq $true) ('runtime-' + $key) }
        Assert-QaCondition ((@($result.migrations) -join ',') -ceq (@($Manifest.Migrations | Sort-Object) -join ',')) 'applied-migration-inventory'
    } finally { $process.Dispose() }
}

function Test-QaProcessIdentity($Record, $Process, [string]$AppPath) {
    return $null -ne $Process -and $Record.ProcessId -eq $Process.ProcessId -and $Process.Name -eq 'dotnet.exe' -and
        ([string]$Process.CommandLine).Trim() -match ('^(?:"[^"]*dotnet.exe"|[^ ]*dotnet.exe)\s+"?' + [regex]::Escape($AppPath) + '"?\s*$') -and
        ([DateTime]$Record.StartTimeUtc).ToUniversalTime().Ticks -eq ([DateTime]$Process.CreationDate).ToUniversalTime().Ticks
}

Export-ModuleMember -Function Assert-QaCondition, Assert-QaChildPath, Assert-QaPrivateMaterialPath, Assert-QaPrivateRoot, Assert-QaSettings, Assert-QaPrivateTree, Get-QaFileManifest, Get-QaRlsInventory, Get-QaDatabaseGuards, Assert-QaRelease, Assert-QaCertificates, Assert-QaClusterRecord, Get-QaFailureCode, Get-QaInheritedOverrideNames, Invoke-QaRuntimeProbe, Test-QaProcessIdentity
