$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateSuccessionService.cs'
if (-not (Test-Path -LiteralPath $servicePath)) {
    throw 'Missing MilitaryGovernorateSuccessionService.'
}
$service = Get-Content -Raw -LiteralPath $servicePath
$death = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\patch\AW_ActorDeathPatch.cs')
$store = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateStore.cs')
$annual = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateAiService.cs')
$vassal = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\VassalService.cs')
$chronicle = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\ChronicleEvents.cs')
$locale = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Locales\aw3_mandate.csv')
foreach ($token in @(
    'DeferredRuntimeWorkService.EnqueueCoalesced(',
    'DeathQueuePrefix + subjectId + ":" + pRulerActorId',
    'RecoveryQueuePrefix + subjectId',
    'OnRulerDied(Kingdom pSubject, long pRulerActorId)',
    'TryDesignate(Kingdom pSuzerain,',
    'EnqueueRecovery(Kingdom pSubject)',
    'OnKingdomYear(',
    'MilitaryGovernorateSuccessionRules.CandidateLimit',
    'GeneralService.GetActiveGeneralsForReadModel(',
    'pAllowUnitFallback: false',
    'GeneralService.RetireForSuccession(',
    'pSuccessor.setProfession(UnitProfession.King)',
    'pSubject.setKing(pSuccessor)',
    'MilitaryGovernorateStore.CommitSuccession(',
    'ChronicleEvents.TryRecordMilitaryGovernorateSucceeded('
)) {
    if (-not $service.Contains($token)) { throw "Missing succession token: $token" }
}
if ($service.Contains('Actor pSuccessor,`r`n            out string pReason') -or
    $service.Contains('Actor pSuccessor,`n            out string pReason')) {
    throw 'Governor replacement still exposes combined successor designation.'
}
$stableStart = $service.IndexOf(
    'private static bool IsSuzerainStable(', [StringComparison]::Ordinal)
$stableEnd = $service.IndexOf(
    'private static bool IsLivingRuler(', [StringComparison]::Ordinal)
if ($stableStart -lt 0 -or $stableEnd -le $stableStart) {
    throw 'Cannot isolate stable-suzerain method.'
}
$stableMethod = $service.Substring($stableStart, $stableEnd - $stableStart)
if (-not $stableMethod.Contains(
        'VassalService.GetSuzerain(pSubject) != pSuzerain')) {
    throw 'Stable suzerain check does not require direct control.'
}
foreach ($token in @(
    'TryDesignateSuccessor(',
    'ResolveIndependenceOutcome('
)) {
    if ($service.Contains($token)) {
        throw "Out-of-scope succession API remains: $token"
    }
}
foreach ($token in @(
    'public static bool CommitSuccession(',
    'BeginTransaction(IsolationLevel.Serializable)',
    'GOVERNOR_ACTOR_ID=@governor,SUCCESSOR_ACTOR_ID=-1,',
    'SUCCESSION_STATE=0,REPLACEMENT_ALLOWED=0'
)) {
    if (-not $store.Contains($token)) { throw "Missing store token: $token" }
}
if ($service -match 'foreach\s*\([^)]*\bin\s+World\.world\.units' -or
    $service -match 'World\.world\.units\s*\.\s*(ToList|ToArray|GetEnumerator)') {
    throw 'Military governorate succession contains a global actor scan.'
}
if (-not $death.Contains(
        'MilitaryGovernorateSuccessionService.OnRulerDied(') -or
    -not $death.Contains('__state.DyingKingdom,') -or
    -not $death.Contains('__state.DyingKingActorId')) {
    throw 'Ruler death does not enqueue governorate succession.'
}
$allowedVassalSuccessionCall =
    'MilitaryGovernorateSuccessionService.CanReplaceGovernorForReadModel'
$vassalSuccessionCalls = [regex]::Matches($vassal,
    'MilitaryGovernorateSuccessionService\.[A-Za-z0-9_]+') |
    ForEach-Object { $_.Value } | Sort-Object -Unique
foreach ($call in $vassalSuccessionCalls) {
    if ($call -ne $allowedVassalSuccessionCall) {
        throw "VassalService contains disallowed succession call: $call"
    }
}
$deathStart = $service.IndexOf(
    'public static void OnRulerDied(', [StringComparison]::Ordinal)
$deathEnd = $service.IndexOf(
    'public static void OnKingdomYear(', [StringComparison]::Ordinal)
if ($deathStart -lt 0 -or $deathEnd -le $deathStart) {
    throw 'Cannot isolate ruler-death succession method.'
}
$deathMethod = $service.Substring($deathStart, $deathEnd - $deathStart)
if (-not $deathMethod.Contains(
        'DeferredRuntimeWorkService.EnqueueCoalesced(')) {
    throw 'Ruler-death succession does not enqueue deferred work.'
}
foreach ($token in @(
    'GetActiveGeneralsForReadModel(',
    'SelectLocalGeneral(',
    'World.world.units'
)) {
    if ($deathMethod.Contains($token)) {
        throw "Ruler-death succession performs synchronous candidate work: $token"
    }
}
$commitStart = $service.IndexOf(
    'private static bool Commit(', [StringComparison]::Ordinal)
$commitEnd = $service.IndexOf(
    'private static bool TryReadManagedState(', [StringComparison]::Ordinal)
if ($commitStart -lt 0 -or $commitEnd -le $commitStart) {
    throw 'Cannot isolate succession commit method.'
}
$commit = $service.Substring($commitStart, $commitEnd - $commitStart)
if ($commit.Contains('pSuccessor.kingdom = null')) {
    throw 'Succession creates an unrecoverable unaffiliated migration window.'
}
$lastPosition = -1
foreach ($token in @(
    'pSubject.setKing(pSuccessor)',
    'pSuccessor.setProfession(UnitProfession.King)',
    'GeneralService.RetireForSuccession(',
    'ChronicleEvents.TryRecordMilitaryGovernorateSucceeded(',
    'MilitaryGovernorateStore.CommitSuccession('
)) {
    $position = $commit.IndexOf($token, [StringComparison]::Ordinal)
    if ($position -le $lastPosition) {
        throw "Succession commit order is invalid at: $token"
    }
    $lastPosition = $position
}
if (-not $annual.Contains(
        'MilitaryGovernorateSuccessionService.OnKingdomYear(')) {
    throw 'Annual governorate service does not progress succession.'
}
if (-not $chronicle.Contains(
        'HistoryLocalizationRules.H("aw_hist_military_governorate_succession_mid")')) {
    throw 'Governorate succession chronicle is not localized.'
}
foreach ($token in @(
    'public static bool TryRecordMilitaryGovernorateSucceeded(',
    'HistoryWriter.TryRecordKingdom(',
    'HistoryWriter.TryRecordPerson(',
    'MilitaryGovernorateSuccessionRules.ChronicleProjectionKey('
)) {
    if (-not $chronicle.Contains($token)) {
        throw "Governorate succession chronicle is not retry-safe: $token"
    }
}
if (-not $locale.Contains(
        'aw_hist_military_governorate_succession_mid,')) {
    throw 'Governorate succession locale is missing.'
}
Write-Output 'Military governorate succession source guard passed.'
