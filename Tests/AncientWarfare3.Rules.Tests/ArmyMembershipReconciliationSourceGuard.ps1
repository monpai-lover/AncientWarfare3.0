$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$servicePath = Join-Path $repo `
    'Code/core/lineage/ArmyMembershipReconciliationService.cs'
if (-not [IO.File]::Exists($servicePath)) {
    throw 'ArmyMembershipReconciliationService.cs is missing.'
}

$service = Get-Content -Raw -LiteralPath $servicePath
$actorPatch = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code/patch/AW_SlaveryPatch.cs')
$cityPatch = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code/patch/AW_ArmySafetyPatch.cs')
$deferredPatch = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code/patch/AW_DeferredRuntimeWorkPatch.cs')
$armyService = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code/core/lineage/AWArmyService.cs')
$dirtyPatch = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code/patch/AW_DirtyMetaActorIndexPatch.cs')

if ($actorPatch -notmatch 'ArmyMembershipReconciliationService\.Enqueue') {
    throw 'Actor.setKingdom must enqueue its affected army.'
}
if ($cityPatch -notmatch 'ArmyMembershipReconciliationService\.Enqueue') {
    throw 'City.setKingdom must enqueue its anchor army after transfer.'
}
if ($deferredPatch -notmatch 'ArmyMembershipReconciliationService\.ProcessFrame') {
    throw 'The main-thread deferred runtime host must process army reconciliation.'
}
if ($armyService -notmatch 'AddToArmy[\s\S]*ArmyMembershipOwnershipRules\.Decide') {
    throw 'AddToArmy must reject stable known foreign membership.'
}
if ($dirtyPatch -notmatch 'HarmonyPostfix[\s\S]*ArmyManager[\s\S]*ArmyMembershipReconciliationService\.EnqueueAll') {
    throw 'ArmyManager dirty rebuilds in Native and Large modes must enqueue reconciliation.'
}

$requiredCleanup = @(
    'ArmyMembershipOwnershipRules.Decide',
    'ArmyMembershipOwnershipRules.ShouldReleaseRosterEntry',
    'removeFromArmy',
    'units.Remove',
    'if (ownedByNewArmy) return changed;',
    'ArmyCaptainDisposalScope.Open',
    'ArmyRtsControllerService.ReleaseActor',
    'ArmyDeploymentService.ReleaseActor',
    'TemporaryLevyService.OnActorInvalidated',
    'WartimeGarrisonService.OnActorInvalidated',
    'MandateMilitaryPhaseService.Clear',
    'ArmyStrategicIndexService.OnArmyRosterChanged'
)
foreach ($token in $requiredCleanup) {
    if (-not $service.Contains($token)) {
        throw "Army reconciliation is missing required cleanup: $token"
    }
}
if (-not $service.Contains('MaxUnknownOwnerRetries') -or
    -not $service.Contains('ScheduleUnknownOwnerRetry') -or
    -not $service.Contains('PromoteDelayedRetries')) {
    throw 'Unknown army ownership must use bounded delayed retries instead of per-frame self-enqueue.'
}

Write-Output 'Army membership reconciliation source guard passed.'
