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
Require-Present $interpolator `
    'ConditionalWeakTable<Actor, AWActorPresentationState>' `
    'Actor interpolation state must expire with the Actor reference.'
if ($interpolator.Contains('preparedFrame % 600') -or
    $interpolator.Contains('staleHandles')) {
    throw 'Actor interpolation must not periodically scan all historical state.'
}

Write-Output 'Presentation snapshot performance source guard passed.'
