$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$lifecyclePath = Join-Path $root 'Code\patch\AW_ActorBoatLifecyclePatch.cs'
$bridgePath = Join-Path $root 'Code\core\pathfinding\AWPathMovementBridge.cs'
$lifecycle = Get-Content -Raw -LiteralPath $lifecyclePath
$bridge = Get-Content -Raw -LiteralPath $bridgePath

function Require-Text([string]$source, [string]$text, [string]$message) {
    if (-not $source.Contains($text)) {
        throw $message
    }
}

function Reject-Text([string]$source, [string]$text, [string]$message) {
    if ($source.Contains($text)) {
        throw $message
    }
}

Require-Text $lifecycle 'HarmonyPatch(typeof(BehBoatTransportDoLoading)' `
    'ordinary Taxi loading is not intercepted on the boat side'
Require-Text $lifecycle 'LoadCommonPassengers(' `
    'ordinary Taxi loading does not use the Cultiway boat-side handoff'
Require-Text $lifecycle 'request.getActors()' `
    'ordinary Taxi loading does not enumerate the assigned request roster'
Require-Text $lifecycle 'passenger.data.transportID =' `
    'ordinary Taxi loading does not bind the passenger transport id'
Require-Text $lifecycle 'passenger.is_inside_boat = true' `
    'ordinary Taxi loading does not establish the passenger boat state'
Require-Text $lifecycle 'boat.addPassenger(passenger)' `
    'ordinary Taxi loading does not update the native boat passenger set'
Require-Text $lifecycle 'AWInsideBoatActorIndex.Notify(passenger, true)' `
    'ordinary Taxi loading does not update the large-step inside-boat index'
Reject-Text $lifecycle 'passenger.setTask("force_into_a_boat")' `
    'boat-side loading re-enters the passenger goTo chain'

Require-Text $bridge 'if (TransportContexts.TryGetValue' `
    'the AW passenger transport context is missing'
Require-Text $bridge 'CancelTransport(pActor);' `
    'the regression precondition changed: target replacement no longer cancels transport'

Write-Output 'Vanilla Taxi boat compatibility source guard passed.'
