$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$settings = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\AWPerformanceSettings.cs')
$snapshot = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\AWActorPresentationSnapshot.cs')
$renderer = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\AWWorldObjectPresentationRenderer.cs')
$actorRenderer = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\AWActorPresentationRenderer.cs')
$patch = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\patch\AW_FramePrioritySchedulerPatch.cs')
$runner = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\AWCooperativeSimulationRunner.cs')
$interpolator = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\AWPresentationInterpolator.cs')

function Require-Present([string] $source, [string] $needle,
    [string] $message) {
    if (-not $source.Contains($needle)) { throw $message }
}

Require-Present $settings `
    'EnableWorldObjectPresentationSnapshots => false' `
    'World-object snapshots must remain disabled on the performance path.'
Require-Present $settings `
    'EnableActorOverlaySnapshots => false' `
    'Actor overlays must retain the native sparse-list rendering path.'
Require-Present $settings `
    'EnableActorPresentationSnapshots => false' `
    'Actor animation must remain on the vanilla presentation path.'
Require-Present $settings `
    'EnableStatusSimulationScheduler => false' `
    'Status animation and lifecycle must remain on the vanilla update path.'
Require-Present $snapshot `
    'AWPerformanceSettings.EnableWorldObjectPresentationSnapshots' `
    'Snapshot capture must gate world-scale object work.'
Require-Present $snapshot 'CopyStableDataFrom(source,' `
    'Dynamic snapshots must distinguish Actor data from world-object data.'
Require-Present $renderer `
    '!AWPerformanceSettings.EnableWorldObjectPresentationSnapshots' `
    'The custom world-object renderer must reject disabled snapshots.'
$overlays = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\AWActorPresentationOverlays.cs')
Require-Present $overlays `
    'AWPerformanceSettings.EnableActorOverlaySnapshots' `
    'Custom Actor overlays must be explicitly gated off.'
foreach ($list in @(
    'visible_units_avatars',
    'visible_units_with_status',
    'visible_units_with_favorite',
    'visible_units_with_banner',
    'visible_units_just_ate',
    'visible_units_socialize')) {
    $pattern = [regex]::Escape("manager.$list.array") +
        '\[\s*' + [regex]::Escape("manager.$list.count") +
        '\+\+\]\s*=\s*actor'
    if ($actorRenderer -notmatch $pattern) {
        throw "Native sparse Actor overlay list is not rebuilt: $list"
    }
}
Require-Present $patch '"quantum.buildings.native"' `
    'Native building drawing must wait for mutating simulation work.'
Require-Present $patch '"quantum.projectiles.native"' `
    'Native projectile drawing must wait for mutating simulation work.'
Require-Present $patch '"quantum.resource_throws.native"' `
    'Native resource-throw drawing must wait for mutating simulation work.'
Require-Present $patch `
    'if (!AWPerformanceSettings.EnableActorPresentationSnapshots)' `
    'Native Actor rendering must bypass the custom presentation snapshot path.'
Require-Present $patch `
    'AWPerformanceSettings.EnableActorPresentationSnapshots &&' `
    'Actor snapshot capture must remain disabled in vanilla animation mode.'
if ($runner -notmatch
    'if \(!AWPerformanceSettings\.EnableActorPresentationSnapshots\)\s+return false;') {
    throw 'Vanilla Actor rendering must never dispatch deferred Actor presentation work.'
}
if ($runner -match
    'if \(!AWPerformanceSettings\.EnableActorPresentationSnapshots\)\s*\{\s*return _actorRunner\.RunDeferredParallelWorkSynchronously\(\);') {
    throw 'Disabling Actor snapshots must not force the Actor parallel stage onto the main thread.'
}
if ($runner -notmatch
    'if \(!AWPerformanceSettings\.EnableActorPresentationSnapshots\)\s*\{\s*return _actorRunner\.BeginParallelPresentationWork\(\);') {
    throw 'Actor simulation must retain a background ticket when presentation snapshots are disabled.'
}
if ($runner -notmatch
    'if \(!AWPerformanceSettings\.EnableWorldObjectPresentationSnapshots\)\s+return false;') {
    throw 'Vanilla building rendering must never dispatch deferred building presentation work.'
}
if ($runner -notmatch
    'bool actorBackgroundPending\s*=\s*_actorRunner\.WaitingForBackgroundWork\s*&&\s*!_actorRunner\.IsBackgroundWorkCompleted') {
    throw 'Completed Actor post work must be allowed back into the main-thread commit stage.'
}
if ($runner -notmatch
    'bool buildingBackgroundPending\s*=\s*_buildingRunner\.WaitingForBackgroundWork\s*&&\s*!_buildingRunner\.IsBackgroundWorkCompleted') {
    throw 'Completed Building post work must be allowed back into the main-thread commit stage.'
}
Require-Present $interpolator `
    'ConditionalWeakTable<Actor, AWActorPresentationState>' `
    'Actor interpolation state must expire with the Actor reference.'
if ($interpolator.Contains('preparedFrame % 600') -or
    $interpolator.Contains('staleHandles')) {
    throw 'Actor interpolation must not periodically scan all historical state.'
}

Write-Output 'Presentation snapshot performance source guard passed.'
