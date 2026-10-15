param(
    [Parameter(Mandatory)][string]$ScriptPath,
    [switch]$ExpectSyntaxError,
    [string]$PsqlPath = 'C:/Program Files/PostgreSQL/18/bin/psql.exe'
)
$ErrorActionPreference = 'Stop'
# Compile the actual EF-emitted IF operand; no schema changes or commercial records.
$script = Get-Content -LiteralPath $ScriptPath -Raw
$function = [regex]::Match($script, '(?s)CREATE FUNCTION official_refresh_history_guard\(\).*?END \$guard\$;').Value
$match = [regex]::Match($function, 'NEW\.approved_snapshot IS DISTINCT FROM (.+?) OR NEW\.changed_fields')
if (-not $match.Success) { throw 'Expected generated refresh history guard was not found.' }
$operand = $match.Groups[1].Value.Replace('r.status', 'stage').Replace('r.proposed_snapshot', 'proposed')
$file = Join-Path $env:APPDATA 'Microsoft/UserSecrets/MediPOS.Api-9201279d-d03b-4880-b593-a653527b4533/secrets.json'
$settings = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection.set_ConnectionString($settings['ConnectionStrings:MediPosDatabase'])
if ($connection['Database'] -ne 'medipos_dev' -or $connection['Username'] -ne 'medipos_runtime' -or
    [string]$connection['Host'] -notin @('localhost', '127.0.0.1', '::1')) { throw 'Only local medipos_dev runtime is authorized.' }
$probe = @'
\set VERBOSITY verbose
BEGIN READ ONLY;
DO $probe$
DECLARE stage text; actual jsonb; proposed jsonb := '{}'::jsonb; observed boolean; expected boolean; missing boolean;
BEGIN
  FOREACH stage IN ARRAY ARRAY['pending_review','approved','applied'] LOOP
    FOREACH missing IN ARRAY ARRAY[false,true] LOOP
      actual := CASE WHEN missing THEN NULL ELSE proposed END;
      expected := CASE WHEN stage IN ('approved','applied') THEN missing ELSE NOT missing END;
      observed := false;
      IF actual IS DISTINCT FROM __OPERAND__ OR false THEN observed := true; END IF;
      IF observed IS DISTINCT FROM expected THEN RAISE EXCEPTION 'Refresh snapshot comparison changed'; END IF;
    END LOOP;
  END LOOP;
END $probe$;
ROLLBACK;
'@
$priorPassword = $env:PGPASSWORD
try {
    $env:PGPASSWORD = [string]$connection['Password']
    $port = if ($connection.ContainsKey('Port')) { [string]$connection['Port'] } else { '5432' }
    $output = $probe.Replace('__OPERAND__', $operand) | & $PsqlPath -X -w -h ([string]$connection['Host']) -p $port -U medipos_runtime -d medipos_dev -v ON_ERROR_STOP=1 2>&1
    $code = $LASTEXITCODE
    if ($ExpectSyntaxError) {
        if ($code -eq 0 -or ($output -join "`n") -notmatch '42601') { throw 'Expected SQLSTATE 42601 was not reproduced.' }
        'PASS: original EF-emitted CASE/IF reproduces SQLSTATE 42601.'
    } else {
        if ($code -ne 0) { $output | Write-Output; throw 'Generated refresh guard regression failed.' }
        'PASS: generated CASE/IF compiles; six null/approved/applied comparisons preserve semantics; read-only transaction rolled back.'
    }
} finally {
    $env:PGPASSWORD = $priorPassword
    $connection = $null; $settings = $null
}
