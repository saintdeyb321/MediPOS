param(
    [switch]$SelfTest,
    [string]$Archive,
    [string]$ExpectedArchiveSha256,
    [string]$PrivateDirectory,
    [string]$PgRestore='C:\Program Files\PostgreSQL\18\bin\pg_restore.exe'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$templates=Join-Path $PSScriptRoot 'dev-reset'
$policy=Get-Content (Join-Path $templates 'policy.json') -Raw|ConvertFrom-Json
[string[]]$all=@($policy.keepTables)+@($policy.resetTables)
[Array]::Sort($all,[StringComparer]::Ordinal)
if($all.Count -ne 62 -or @($all|Select-Object -Unique).Count -ne 62 -or @($policy.resetTables).Count -ne 49){throw 'Unreviewed S4 table policy'}
function SqlNames($names){(@($names)|ForEach-Object{if($_ -notmatch '^[A-Za-z0-9_]+$'){throw 'Unsafe SQL literal'};"'"+$_+"'"}) -join ','}
function ApprovalGuard([string]$sha,[string]$condition){
    @'
DO $s4$ BEGIN IF (SELECT count(*) FROM pg_temp.s4_approved)<>1 OR (SELECT payload->>'archive' FROM pg_temp.s4_approved) IS DISTINCT FROM '{{SHA}}' OR {{CONDITION}} THEN RAISE EXCEPTION 'S4 approved backup/baseline mismatch'; END IF; END $s4$;
'@.Replace('{{SHA}}',$sha).Replace('{{CONDITION}}',$condition)
}
function Filter-Toc([string[]]$lines){
    $seen=@();$filtered=@()
    if(-not($lines -match '^;\s*dbname:\s*medipos_dev\s*$')){throw 'Archive is not medipos_dev'}
    foreach($line in $lines){
        if($line -match '^\d+;\s+\d+\s+\d+\s+(SEQUENCE|BLOB|BLOBS|LARGE OBJECT|FOREIGN TABLE|MATERIALIZED VIEW|PUBLICATION|SUBSCRIPTION|DATABASE|STATISTICS DATA)\b'){throw 'Unreviewed archive object; no plan generated'}
        if($line -match '^\d+;\s+\d+\s+\d+\s+TABLE DATA\s+(\S+)\s+(\S+)\s+'){
            $schema=$matches[1];$table=$matches[2].Trim('"')
            if($schema -cne 'public' -or $table -cnotin $all -or $table -cin $seen){throw 'Unknown/duplicate archive data table'}
            $seen+=$table
            if($table -cin @($policy.resetTables)){$filtered+='; S4 OMIT PRIVATE DATA '+$line;continue}
        }
        $filtered+=$line
    }
    if($seen.Count -ne 62){throw 'Archive must contain all 62 reviewed data entries'}
    return ,$filtered
}
function Expand([string]$text){
    $text.Replace('{{ALL_TABLES}}',(SqlNames $all)).Replace('{{KEEP_TABLES}}',(SqlNames $policy.keepTables)).Replace('{{RESET_TABLES}}',(SqlNames $policy.resetTables)).Replace('{{MIGRATIONS}}',(SqlNames $migrations)).Replace('{{RECOVERY_SCHEMA_SQL}}',$schemaSql)
}
$migrations=@(Get-ChildItem (Join-Path $repo 'backend\src\MediPOS.Infrastructure') -Recurse -File|Where-Object {$_.FullName -notmatch '\\(obj|bin)\\' -and $_.Name -match '^\d{14}_.+\.cs$' -and $_.Name -notmatch '\.Designer\.cs$'}|Select-Object -ExpandProperty BaseName|Sort-Object)
if($migrations.Count -ne 39 -or $migrations[-1] -cne '20261030041215_AddInitialOwnerEmailOnboarding'){throw 'Only reviewed migration39 is supported'}
$inspection=Join-Path $PSScriptRoot 'MediPOS.Recovery\RecoveryInspection.cs'
$match=[regex]::Match([IO.File]::ReadAllText($inspection),'(?s)private const string SchemaSql = """\s*(.*?)\s*""";')
if(-not $match.Success){throw 'Existing recovery schema inventory cannot be reused'}
$schemaSql=$match.Groups[1].Value
if($SelfTest){
    $sample=@('; dbname: medipos_dev');$n=1
    foreach($table in $all){$sample+=($n.ToString()+'; 0 1 TABLE DATA public '+$table+' postgres');$n++}
    $sample+='900; 0 1 TRIGGER public notifications notifications_no_truncate postgres'
    $sample+='901; 0 1 ROW SECURITY public initial_owner_invitations postgres'
    $sample+='902; 0 1 ACL public TABLE initial_owner_invitations postgres'
    $result=Filter-Toc $sample
    if(@($result|Where-Object {$_ -like '; S4 OMIT PRIVATE DATA *'}).Count -ne 49 -or @($result|Where-Object {$_ -match '^\d+;.*TABLE DATA'}).Count -ne 13 -or $result[-3] -cne $sample[-3] -or $result[-2] -cne $sample[-2] -or $result[-1] -cne $sample[-1]){throw 'S4 filter regression'}
    foreach($bad in @('999; 0 1 TABLE DATA public unreviewed postgres','999; 0 1 SEQUENCE SET public serial_id postgres','999; 0 1 DATABASE medipos_dev postgres','999; 0 1 STATISTICS DATA public users postgres')){
        $rejected=$false;try{$null=Filter-Toc ($sample+@($bad))}catch{$rejected=$true};if(-not $rejected){throw 'Unsafe TOC accepted'}
    }
    $rejected=$false;try{$null=Filter-Toc @($sample|Where-Object {$_ -notmatch 'TABLE DATA public users '})}catch{$rejected=$true};if(-not $rejected){throw 'Incomplete archive accepted'}
    $rendered=Expand ([IO.File]::ReadAllText((Join-Path $templates 'preflight.sql')))
    if($rendered -match '\{\{' -or -not $rendered.Contains("'20261030041215_AddInitialOwnerEmailOnboarding'")){throw 'Migration/template expansion regression'}
    $guard=ApprovalGuard ('A'*64) 'true'
    if(-not $guard.StartsWith('DO $s4$ BEGIN') -or -not $guard.EndsWith('END $s4$;') -or $guard -match '\{\{'){throw 'Approval guard quoting regression'}
    'PASS: 8 offline TOC/template cases; zero native tools, file writes or database connections.'
    exit 0
}
if(-not $Archive -or $ExpectedArchiveSha256 -cnotmatch '^[A-F0-9]{64}$' -or -not $PrivateDirectory){throw 'Archive, exact SHA-256 and a new private plan directory are mandatory'}
$archivePath=[IO.Path]::GetFullPath($Archive);$plan=[IO.Path]::GetFullPath($PrivateDirectory);$parent=Split-Path $plan -Parent
if($archivePath.Contains('"') -or $plan.Contains('"') -or $plan.Contains("'") -or $plan.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $plan) -or -not(Test-Path -LiteralPath $archivePath -PathType Leaf) -or (Split-Path $archivePath -Parent) -ine $parent){throw 'Use a fresh plan child of the private backup directory, outside the repository'}
Import-Module (Join-Path $PSScriptRoot 'LocalDev.Admin.psm1') -Force -DisableNameChecking
$module=Get-Module LocalDev.Admin
$physical=& $module {param($p) Get-QaPhysicalDirectoryPath $p} $parent
if($physical -ine $parent -or (Get-Item -LiteralPath $archivePath).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Redirected or linked private material'}
$allowed=@([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18')
$acl=Get-Acl -LiteralPath $parent
if(-not $acl.AreAccessRulesProtected -or @($acl.Access|Where-Object {$_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowed -or $_.AccessControlType -ne 'Allow'}).Count -gt 0){throw 'Private backup root must be protected, current Windows user/SYSTEM only'}
if((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -cne $ExpectedArchiveSha256){throw 'Backup SHA mismatch'}
$null=New-Item -ItemType Directory -Path $plan
function Write-PlanFile([string]$name,[string]$content){[IO.File]::WriteAllText((Join-Path $plan $name),$content,(New-Object Text.UTF8Encoding($false)))}
function Native([string[]]$arguments){
    $info=New-Object Diagnostics.ProcessStartInfo;$info.FileName=$PgRestore;$info.Arguments=($arguments|ForEach-Object{'"'+$_+'"'}) -join ' ';$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process;$process.StartInfo=$info
    try{$null=$process.Start();$output=$process.StandardOutput.ReadToEndAsync();$errorText=$process.StandardError.ReadToEndAsync();if(-not $process.WaitForExit(120000)){$process.Kill();throw 'Archive generation timeout'};if($process.ExitCode -ne 0){throw 'Archive generation failed; no database was connected'};return $output.Result}finally{$process.Dispose()}
}
# No --dbname/-d, --create, --disable-triggers or SQL execution exists in this generator.
$toc=(Native @('--list',$archivePath)) -split '\r?\n'
$filtered=Filter-Toc $toc
Write-PlanFile 'all.toc' ($toc -join "`r`n");Write-PlanFile 'reset.toc' ($filtered -join "`r`n")
$null=Native @('--clean','--if-exists','--no-statistics','--file',(Join-Path $plan 'full-restore.sql'),$archivePath)
$null=Native @('--clean','--if-exists','--no-statistics','--use-list',(Join-Path $plan 'reset.toc'),'--file',(Join-Path $plan 'reset-restore.sql'),$archivePath)
foreach($name in @('full-restore.sql','reset-restore.sql')){
    $sql=[IO.File]::ReadAllText((Join-Path $plan $name))
    if($sql -match '(?im)^\\connect\b|^\s*(?:CREATE|DROP)\s+DATABASE\b|^\s*ALTER\s+ROLE\b|^\s*ALTER\s+TABLE[^\r\n]*\bDISABLE\s+TRIGGER\b|^\s*SET\s+session_replication_role\b|^\s*(?:BEGIN|COMMIT|ROLLBACK)\s*;'){throw 'Unreviewed nontransactional/session/trigger command in archive; plan not executable'}
}
$capture=Expand ([IO.File]::ReadAllText((Join-Path $templates 'capture.sql')))
$preflight=Expand ([IO.File]::ReadAllText((Join-Path $templates 'preflight.sql')))
$verify=Expand ([IO.File]::ReadAllText((Join-Path $templates 'verify.sql')))
if(($capture+$preflight+$verify.Replace('{{MODE}}','full')) -match '\{\{'){throw 'Unexpanded maintenance template'}
$csv=(Join-Path $plan 'approved-baseline.csv').Replace('\','/')
$base="\set ON_ERROR_STOP on`r`n\set QUIET on`r`nBEGIN;`r`n"+$preflight+"`r`n"+$capture+"`r`n"
$rehearse=$base+"CREATE TEMP TABLE s4_baseline ON COMMIT DROP AS SELECT pg_temp.s4_capture() snapshot;`r`nSELECT snapshot::text AS s4_expected FROM pg_temp.s4_baseline \gset`r`n\ir full-restore.sql`r`n"+$verify.Replace('{{MODE}}','full')+"`r`nROLLBACK;`r`n"+$capture+"`r`nSELECT pg_temp.s4_capture() = :'s4_expected'::jsonb AS s4_rollback_ok \gset`r`n\if :s4_rollback_ok`r`nCREATE TEMP TABLE s4_export(payload jsonb);`r`nINSERT INTO pg_temp.s4_export VALUES(jsonb_build_object('archive','$ExpectedArchiveSha256','snapshot',:'s4_expected'::jsonb));`r`n\copy pg_temp.s4_export TO '$csv' WITH (FORMAT csv)`r`n\echo S4_REHEARSAL_PASS_ROLLBACK_VERIFIED`r`n\else`r`n\quit 3`r`n\endif`r`n"
Write-PlanFile 'rehearse.psql' $rehearse
foreach($mode in @('reset','rollback')){
    $approval=if($mode -ceq 'reset'){'RESET_MEDIPOS_DEV_39'}else{'RESTORE_MEDIPOS_DEV_39'}
    $script="\set ON_ERROR_STOP on`r`n\if :{?approved_action}`r`nSELECT :'approved_action' = '$approval' AS s4_approved \gset`r`n\else`r`n\quit 3`r`n\endif`r`n\if :s4_approved`r`n"+$base
    $script+="CREATE TEMP TABLE s4_approved(payload jsonb);`r`n\copy pg_temp.s4_approved FROM '$csv' WITH (FORMAT csv)`r`nCREATE TEMP TABLE s4_baseline ON COMMIT DROP AS SELECT payload->'snapshot' snapshot FROM pg_temp.s4_approved;`r`n"
    $condition=if($mode -ceq 'reset'){"pg_temp.s4_capture() IS DISTINCT FROM (SELECT snapshot FROM pg_temp.s4_baseline)"}else{"(pg_temp.s4_capture()-'tables') IS DISTINCT FROM (SELECT snapshot-'tables' FROM pg_temp.s4_baseline)"}
    $script+=(ApprovalGuard $ExpectedArchiveSha256 $condition)+"`r`n"
    $restoreFile=if($mode -ceq 'reset'){'reset-restore.sql'}else{'full-restore.sql'}
    $verifyMode=if($mode -ceq 'reset'){'reset'}else{'full'}
    $script+="\ir $restoreFile`r`n"+$verify.Replace('{{MODE}}',$verifyMode)+"`r`nCOMMIT;`r`n\echo S4_"+$mode.ToUpperInvariant()+"_PASS`r`n\else`r`n\quit 3`r`n\endif`r`n"
    Write-PlanFile ($mode+'.psql') $script
}
$inspect=@'
CREATE TEMP TABLE s4_approved(payload jsonb);
\copy pg_temp.s4_approved FROM '{{CSV}}' WITH (FORMAT csv)
CREATE TEMP TABLE s4_baseline ON COMMIT DROP AS SELECT payload->'snapshot' snapshot FROM pg_temp.s4_approved;
{{GUARD}}
WITH state AS (SELECT snapshot before,pg_temp.s4_capture() after FROM pg_temp.s4_baseline)
SELECT before=after AS s4_full,
  (before-'tables')=(after-'tables')
  AND NOT EXISTS(SELECT 1 FROM jsonb_each(after->'tables') t WHERE t.key=ANY(ARRAY[{{KEEP_TABLES}}]::text[]) AND before->'tables'->t.key IS DISTINCT FROM t.value)
  AND NOT EXISTS(SELECT 1 FROM jsonb_each(after->'tables') t WHERE t.key=ANY(ARRAY[{{RESET_TABLES}}]::text[]) AND (t.value->>'rows')::bigint<>0) AS s4_empty FROM state \gset
ROLLBACK;
\if :s4_full
\echo S4_STATE_FULL_SOURCE
\elif :s4_empty
\echo S4_STATE_EMPTY_COMMERCIAL
\else
\echo S4_STATE_DRIFT_NO_GO
\quit 3
\endif
'@
$inspect=Expand ($inspect.Replace('{{CSV}}',$csv).Replace('{{GUARD}}',(ApprovalGuard $ExpectedArchiveSha256 'false')))
Write-PlanFile 'inspect.psql' ($base+$inspect)
if((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -cne $ExpectedArchiveSha256){throw 'Archive changed during generation'}
$files=@(Get-ChildItem -LiteralPath $plan -File|ForEach-Object{[ordered]@{Name=$_.Name;Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
Write-PlanFile 'manifest.json' (([ordered]@{State='PREPARED_NOT_EXECUTED';ArchiveSha256=$ExpectedArchiveSha256;Database='medipos_dev';Port=5432;Migrations=39;SchemaSourceSha256=(Get-FileHash $inspection -Algorithm SHA256).Hash;RecoveryRehearsed=$false;ResetExecuted=$false;Files=$files})|ConvertTo-Json -Depth 6)
'S4 PLAN_PREPARED_NOT_EXECUTED: 49 omitted data entries; 13 preserved; no database connection.'
