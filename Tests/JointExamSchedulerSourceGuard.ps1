$ErrorActionPreference = 'Stop'
function Read-Source([string] $Path) {
    $full = Join-Path (Split-Path $PSScriptRoot -Parent) $Path
    if (-not (Test-Path -LiteralPath $full)) { throw "Missing source file: $Path" }
    Get-Content -LiteralPath $full -Raw
}
function Require-Text([string] $Source, [string] $Needle, [string] $Label) {
    if (-not $Source.Contains($Needle)) { throw "Missing ${Label}: $Needle" }
}
$cycle = Read-Source 'Code/core/court/ExamCycleService.cs'
$annual = Read-Source 'Code/core/policy/KingdomAnnualWorkService.cs'
Require-Text $cycle 'CivilServiceExamService.OnKingdomYear' 'civil exam dispatch'
Require-Text $cycle 'MilitaryExamService.OnKingdomYear' 'military exam dispatch'
Require-Text $cycle 'ExamCycleRules.IsCycleYear' 'shared cycle gate'
Require-Text $cycle 'ExamCycleRules.IdempotencyKey' 'shared idempotency key'
Require-Text $annual 'ExamCycleService.OnKingdomYear' 'annual shared entry point'
$run = [regex]::Match($annual, 'private static void RunStateGovernmentExam\(Kingdom pKingdom\)(?<body>[\s\S]*?)\n        }')
if (-not $run.Success) { throw 'RunStateGovernmentExam body not found' }
if ($run.Groups['body'].Value.Contains('CivilServiceExamService.OnKingdomYear')) {
    throw 'annual entry must not dispatch civil exam directly'
}
Write-Host 'Joint exam scheduler source guard passed.'
