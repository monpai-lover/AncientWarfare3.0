$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$content = Get-Content -Raw (Join-Path $root 'Code\content\DiplomacyContent.cs')
$rules = Get-Content -Raw (Join-Path $root 'Code\core\lineage\DiplomaticWarDeclarationLedgerRules.cs')
$notice = Get-Content -Raw (Join-Path $root 'Code\core\lineage\WarNoticeService.cs')
$warPatch = Get-Content -Raw (Join-Path $root 'Code\patch\AW_WarPatch.cs')

if (-not $content.Contains('AddWarType("de_jure_war"')) {
    throw 'De jure declarations must register the de_jure_war asset.'
}
if (-not $rules.Contains('"missing_war_type"') -or
    -not $rules.Contains('"unknown_goal"')) {
    throw 'Permanent declaration failures must be terminal and not retried.'
}
if ($rules -match 'ShouldRetryExecutionFailure[\s\S]{0,500}return !string\.Equals\(pReason, "invalid_participants"') {
    throw 'Declaration retry policy still retries every failure by default.'
}
if (-not $notice.Contains('if (Index(state))') -or
    -not $notice.Contains('ArmyDeploymentService.ActivateNotice(state);')) {
    throw 'Notice activation must be gated to the first indexed state change.'
}
if (-not $warPatch.Contains('TemporaryLevyService.OnWarStarted(__result')) {
    throw 'War start must activate the temporary levy/conscript lifecycle.'
}
Write-Output 'War preparation convergence source guard passed.'
