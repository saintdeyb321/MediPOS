# Native cluster ownership guards. Import has no side effects; no initdb/provisioning runs implicitly.
Set-StrictMode -Version 2.0
function Assert-QaNativeClusterRecord($Record, [string]$Root, [string]$Commit) {
    Assert-QaCondition ($Commit -cmatch '^[0-9a-f]{40}$' -and $Record.SourceCommit -ceq $Commit -and $Record.Format -eq 1 -and $Record.Discardable -eq $true) 'native-cluster-approved-source'
    Assert-QaCondition ($Record.Database -cmatch '^medipos_qa_[a-z0-9_]{1,25}$' -and $Record.RuntimeRole -ceq ($Record.Database+'_runtime') -and $Record.ProvisionerRole -ceq ($Record.Database+'_provisioner') -and $Record.Host -ceq 'localhost' -and $Record.Port -eq 55432) 'native-cluster-destination'
    Assert-QaCondition ($Record.SystemIdentifier -cmatch '^\d{16,20}$' -and [IO.Path]::GetFullPath($Record.DataDirectory).TrimEnd('\') -ieq (Join-Path $Root 'postgres-data')) 'native-cluster-control-identity'
    foreach ($hash in @($Record.PostgresConfSha256,$Record.HbaConfSha256)) { Assert-QaCondition ($hash -cmatch '^[A-Fa-f0-9]{64}$') 'native-cluster-config-hash' }
}
function Get-QaPostgresConfiguration([string]$Root, [string]$Database) {
    Assert-QaCondition ($Database -cmatch '^medipos_qa_[a-z0-9_]{1,25}$') 'native-configuration-database'
    $prefix=$Root.Replace('\','/').Replace("'","''")
    $conf=@"
listen_addresses = '127.0.0.1,::1'
port = 55432
ssl = on
ssl_cert_file = '$prefix/postgres-server.crt'
ssl_key_file = '$prefix/postgres-server.key'
ssl_ca_file = '$prefix/pg-root.crt'
ssl_min_protocol_version = 'TLSv1.2'
password_encryption = 'scram-sha-256'
max_connections = 30
shared_buffers = '128MB'
logging_collector = off
log_statement = 'none'
log_min_error_statement = 'panic'
log_min_messages = warning
log_connections = off
log_disconnections = off
"@
    $hba=@"
hostssl postgres ${Database}_provisioner 127.0.0.1/32 scram-sha-256
hostssl $Database ${Database}_provisioner 127.0.0.1/32 scram-sha-256
hostssl $Database ${Database}_runtime 127.0.0.1/32 scram-sha-256
hostssl postgres ${Database}_provisioner ::1/128 scram-sha-256
hostssl $Database ${Database}_provisioner ::1/128 scram-sha-256
hostssl $Database ${Database}_runtime ::1/128 scram-sha-256
host all all 0.0.0.0/0 reject
host all all ::/0 reject
"@
    return [pscustomobject]@{Conf=$conf;Hba=$hba}
}
function Invoke-QaNativeControl([string]$Executable, [string]$Arguments) {
    Assert-QaCondition ($Executable -in @('pg_controldata.exe','pg_ctl.exe')) 'native-control-command'
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=Join-Path 'C:\Program Files\PostgreSQL\18\bin' $Executable
    $info.Arguments=$Arguments; $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    foreach ($key in @($info.EnvironmentVariables.Keys)) { if ($key -like 'PG*') { $info.EnvironmentVariables.Remove($key) } }
    $info.EnvironmentVariables['LC_ALL']='C'
    $process=New-Object Diagnostics.Process; $process.StartInfo=$info
    try {
        $null=$process.Start(); $output=$process.StandardOutput.ReadToEndAsync(); $errors=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(45000)) {
            # Only this helper's pinned handle is ours; never stop a postmaster by name/PID here.
            $process.Kill(); $process.WaitForExit()
            throw 'QA NO-GO: native-control-timeout-reconcile-cluster-before-retry'
        }
        Assert-QaCondition ($process.ExitCode -eq 0) 'native-control-failed-details-withheld'
        $null=$errors.Result; return $output.Result
    } finally { $process.Dispose() }
}
function Assert-QaNativeControlIdentity($Record, [string]$Root) {
    $data=Assert-QaChildPath $Record.DataDirectory $Root
    Assert-QaCondition ((Get-Content -LiteralPath (Join-Path $data 'PG_VERSION') -Raw).Trim() -ceq '18') 'native-data-version'
    foreach ($entry in @(@('postgresql.conf',$Record.PostgresConfSha256),@('pg_hba.conf',$Record.HbaConfSha256))) {
        Assert-QaCondition ((Get-FileHash -LiteralPath (Join-Path $data $entry[0]) -Algorithm SHA256).Hash -ceq $entry[1]) 'native-config-changed'
    }
    $expected=Get-QaPostgresConfiguration $Root $Record.Database
    Assert-QaCondition ((Get-Content -LiteralPath (Join-Path $data 'postgresql.conf') -Raw).Replace("`r`n","`n").TrimEnd() -ceq $expected.Conf.Replace("`r`n","`n").TrimEnd() -and (Get-Content -LiteralPath (Join-Path $data 'pg_hba.conf') -Raw).Replace("`r`n","`n").TrimEnd() -ceq $expected.Hba.Replace("`r`n","`n").TrimEnd()) 'native-reviewed-loopback-tls-configuration'
    $auto=Join-Path $data 'postgresql.auto.conf'
    Assert-QaCondition (@(Get-Content -LiteralPath $auto | Where-Object { $_.Trim().Length -gt 0 -and -not $_.TrimStart().StartsWith('#') }).Count -eq 0) 'native-unreviewed-auto-configuration'
    $output=Invoke-QaNativeControl 'pg_controldata.exe' ('-D "'+$data+'"')
    Assert-QaCondition ($output -match '(?m)^Database system identifier:\s*(\d+)\s*$' -and $Matches[1] -ceq $Record.SystemIdentifier) 'native-control-system-identifier'
}
function Test-QaPostmasterIdentity($Record, $Process, [string]$DataDirectory) {
    return $null -ne $Process -and $Record.ProcessId -eq $Process.ProcessId -and $Process.Name -ceq 'postgres.exe' -and
        $Process.ExecutablePath -ieq 'C:\Program Files\PostgreSQL\18\bin\postgres.exe' -and
        $Process.CommandLine -match ('(?:^|\s)-D\s+"?'+[regex]::Escape($DataDirectory)+'"?(?:\s|$)') -and
        ([DateTime]$Record.StartTimeUtc).ToUniversalTime().Ticks -eq ([DateTime]$Process.CreationDate).ToUniversalTime().Ticks
}
Export-ModuleMember -Function Assert-QaNativeClusterRecord, Get-QaPostgresConfiguration, Invoke-QaNativeControl, Assert-QaNativeControlIdentity, Test-QaPostmasterIdentity
