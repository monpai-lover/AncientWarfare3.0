$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$rulesPath = Join-Path $repo 'Code/core/lineage/AccessionIdentityRules.cs'
$sourcePath = Join-Path $repo 'Code/core/lineage/AccessionIdentityService.cs'
if (-not [IO.File]::Exists($rulesPath)) { throw 'AccessionIdentityRules.cs is missing.' }
if (-not [IO.File]::Exists($sourcePath)) { throw 'AccessionIdentityService.cs is missing.' }

Add-Type -TypeDefinition ([IO.File]::ReadAllText($rulesPath)) -Language CSharp
$rules = [AncientWarfare3.core.lineage.AccessionIdentityRules]

function Assert-Equal([string]$name, $expected, $actual) {
    if ($expected -ne $actual) { throw "$name expected '$expected' but got '$actual'" }
}

Assert-Equal 'first retry is delayed by one frame' 1 ($rules::ResolveDeferredRetryDelay(1))
Assert-Equal 'second retry backs off' 2 ($rules::ResolveDeferredRetryDelay(2))
Assert-Equal 'third retry backs off exponentially' 4 ($rules::ResolveDeferredRetryDelay(3))
Assert-Equal 'late retries remain bounded' 32 ($rules::ResolveDeferredRetryDelay(8))

$source = [IO.File]::ReadAllText($sourcePath)
if (-not $source.Contains('NextEligibleFrame')) {
    throw 'Deferred installation does not have frame-level retry backoff.'
}
if (-not $source.Contains('existing.ActorId == actorId')) {
    throw 'Duplicate deferred king installation is still resetting retry state.'
}
if (-not $source.Contains('LastPrepareFailureReason')) {
    throw 'Deferred installation does not expose a classified prepare failure reason.'
}
if (-not $source.Contains('reason=')) {
    throw 'Exhausted deferred installation warning does not include its failure reason.'
}
foreach ($required in @(
    'TryRepairCapital',
    'ReleaseForAccession(pKingdom, pActor)',
    'HistoricalAffiliationService.EndService(pActor',
    'FormalAffiliationTransferScope.Open',
    'pActor.joinKingdom(pKingdom)')) {
    if (-not $source.Contains($required)) {
        throw "Deferred accession root repair is missing '$required'."
    }
}

$guardSource = [IO.File]::ReadAllText((Join-Path $repo `
    'Code/core/lineage/RoyalGuardService.cs'))
if (-not $guardSource.Contains('public static bool ReleaseForAccession(')) {
    throw 'Royal guard service has no forced accession cleanup entry point.'
}

Write-Output 'Accession identity retry rules tests passed.'
