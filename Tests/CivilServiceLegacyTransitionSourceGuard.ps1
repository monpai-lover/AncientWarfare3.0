$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$paths = @{
    Keys = Join-Path $root 'Code/core/lineage/LineageKeys.cs'
    Policy = Join-Path $root 'Code/core/policy/KingdomPolicyService.cs'
    Exam = Join-Path $root 'Code/core/court/CivilServiceExamService.cs'
    Qualification = Join-Path $root 'Code/core/court/CivilServiceQualificationService.cs'
    Court = Join-Path $root 'Code/core/court/CourtService.cs'
    Transition = Join-Path $root 'Code/core/court/CivilServiceLegacyTransitionService.cs'
}
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$path, [string]$label) {
    if (-not (Test-Path -LiteralPath $path)) {
        $failures.Add("${label}: missing file '$path'")
        return ''
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$source, [string]$needle, [string]$label) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${label}: missing '$needle'")
    }
}

function Require-Match([string]$source, [string]$pattern, [string]$label) {
    if (-not [regex]::IsMatch($source, $pattern)) {
        $failures.Add("${label}: missing pattern '$pattern'")
    }
}

$keys = Read-Source $paths.Keys 'lineage keys'
$policy = Read-Source $paths.Policy 'policy completion'
$exam = Read-Source $paths.Exam 'civil-service authority cycle'
$qualification = Read-Source $paths.Qualification 'civil-service qualification gate'
$court = Read-Source $paths.Court 'court appointment projection'
$transition = Read-Source $paths.Transition 'legacy transition service'

Require-Text $keys 'CIVIL_SERVICE_LEGACY_TRANSITION_VERSION' 'kingdom transition marker'
Require-Text $keys 'CIVIL_SERVICE_LEGACY_CREDENTIAL_KINGDOM_ID' 'credential issuer key'
Require-Text $keys 'CIVIL_SERVICE_LEGACY_CREDENTIAL_REMAINING' 'credential remaining key'
Require-Match $policy 'CivilServiceLegacyTransitionService\.OnTechnologyCompleted\(\s*pKingdom,\s*pDef\.Id\s*\);' 'technology completion snapshot'
Require-Text $exam 'CivilServiceLegacyTransitionService.ProcessVersionedBackfill();' 'old-save backfill'
Require-Text $qualification 'HasUsableCredential(pActor, pKingdom, pLayer, pOfficeId)' 'shared credential qualification fallback'
Require-Match $court 'CivilServiceLegacyTransitionService\.AppendEligibleCandidates\(\s*pKingdom,\s*result\s*\);' 'central legacy candidate roster'
Require-Text $court 'CivilServiceLegacyTransitionService.ConsumeAfterCommittedAppointment(' 'post-commit credential consumption'
Require-Text $transition 'CivilServiceLegacyTransitionRules.TransitionVersion' 'versioned migration implementation'
Require-Text $transition 'HistoricalSchoolEducationService.CanAppoint(' 'pre-examination education predicate'
Require-Text $transition 'CourtManualAppointmentRules.CanListCandidate(' 'pre-examination court predicate'
Require-Text $transition 'TryGrantCredentialIfEligible(actor, pKingdom);' 'individual migration failures are isolated'
Require-Text $transition 'private static void TryGrantCredentialIfEligible(' 'credential migration has an actor-safe wrapper'
Require-Text $transition 'TryAppendEligibleCandidate(actor, pKingdom, pRoster,' 'individual roster projection failures are isolated'
Require-Text $transition 'private static void TryAppendEligibleCandidate(' 'legacy roster projection has an actor-safe wrapper'

if ($failures.Count -gt 0) {
    throw "Civil-service legacy transition source guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Civil-service legacy transition source guards passed.'
