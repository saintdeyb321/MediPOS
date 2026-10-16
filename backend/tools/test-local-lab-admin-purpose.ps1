# Pure approval-scope regression; no private configuration, processes or database access.
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$tree = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'start-local-lab-admin.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Administrative launcher syntax failed.' }
$function = $tree.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-LabAdministrativePurpose' }, $true)
if ($null -eq $function) { throw 'Administrative purpose policy is missing.' }
. ([scriptblock]::Create($function.Extent.Text))
$cases = @(
    @{ Approval = @{ Purpose = 'MediPOS LAB initial onboarding' }; Expected = $true },
    @{ Approval = @{ Purpose = 'MediPOS LAB medicine category'; CategoryName = 'Medicamentos' }; Expected = $true },
    @{ Approval = @{ Purpose = 'MediPOS LAB medicine category'; CategoryName = 'Other' }; Expected = $false },
    @{ Approval = @{ Purpose = 'MediPOS LAB medicine category' }; Expected = $false },
    @{ Approval = @{ Purpose = 'Unknown'; CategoryName = 'Medicamentos' }; Expected = $false }
)
foreach ($case in $cases) {
    if ((Test-LabAdministrativePurpose $case.Approval) -ne $case.Expected) { throw 'Administrative purpose regression failed.' }
}
Write-Output 'PASS: 5 administrative-purpose regressions; no authority activated.'
