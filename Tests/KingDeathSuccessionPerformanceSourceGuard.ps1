$ErrorActionPreference = 'Stop'

$death = Get-Content -Raw 'Code/patch/AW_ActorDeathPatch.cs'
$mandate = Get-Content -Raw 'Code/patch/AW_MandateSuccessionPatch.cs'
$heir = Get-Content -Raw 'Code/patch/AW_HeirPatch.cs'
$dispute = Get-Content -Raw 'Code/core/lineage/SuccessionDisputeService.cs'

if ($death.Contains('PrepareSuccessionBeforeKingDeath')) {
    throw 'Actor.die must not synchronously prepare succession'
}
if (-not $death.Contains('SuccessionPreparationService.CaptureDeath')) {
    throw 'Actor.die must capture the scalar succession context'
}
foreach ($forbidden in @('SuccessionDisputeService.Prepare',
        'SQLiteCommand', 'BeginTransaction', 'LineageQuery',
        'World.world.units')) {
    if ($death.Contains($forbidden)) {
        throw "Actor.die contains forbidden king-death work: $forbidden"
    }
}
if ($mandate.Contains('PrepareSuccessionBeforeKingDeath')) {
    throw 'KingdomBehCheckKing must not repeat succession preparation'
}
if (-not $mandate.Contains(
        'SuccessionPreparationService.TryPublishForNativeSuccession')) {
    throw 'KingdomBehCheckKing must consume a revision-valid snapshot'
}
if ($heir.Contains('? HeirService.GetHeir(pKingdom)')) {
    throw 'SuccessionTool must not recompute an heir during installation'
}
if (-not $heir.Contains(
        'SuccessionPreparationService.TryGetPublishedCandidate')) {
    throw 'SuccessionTool must read only the published candidate'
}
if (-not $heir.Contains(
        'SuccessionPreparationService.OnSuccessorInstalled')) {
    throw 'successful native installation must consume the death context'
}
if ($dispute.Contains('public static void Prepare(')) {
    throw 'legacy synchronous succession dispute preparation remains callable'
}

Write-Host 'King death succession performance source guard passed.'
