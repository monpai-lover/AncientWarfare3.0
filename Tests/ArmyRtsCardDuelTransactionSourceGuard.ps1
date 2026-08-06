$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ArmyAbstractBattleService.cs')
$keys = Get-Content -Raw (Join-Path $root 'Code\core\lineage\LineageKeys.cs')

function Require([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) { throw $message }
}

Require $keys 'AW_RTS_ABSTRACT_BATTLE_TRANSACTION' `
    'Abstract duel transaction must have a persisted snapshot key.'
Require $service 'ArmyAbstractBattleTransactionRules.Prepare' `
    'Runtime must persist the complete transaction before transfer.'
Require $service 'ArmyAbstractBattleTransactionRules.Decode' `
    'Runtime must decode a saved transaction after reload.'
Require $service 'ArmyAbstractBattleTransactionRules.Advance' `
    'Runtime must advance phases monotonically.'
Require $service 'LoserArmyIds' `
    'Demobilization must use the persisted loser army roster.'
Require $service 'CollectDefenderArmies' `
    'Abstract duels must collect all defenders anchored to the target city.'
Require $service 'AWArmyService.GetRoleArmies' `
    'Abstract duels must include special-role garrisons in the defender roster.'
Require $service 'AWArmyRole.FeudatoryGarrison' `
    'Abstract duels must include feudatory garrisons in the defender roster.'
Require $service 'FindLoserArmy' `
    'Demobilization must resolve actor ownership from the persisted loser roster.'
Require $service 'ArmyAbstractBattlePhase.Transferred' `
    'Runtime must persist the transfer phase before demobilization.'
Require $service 'ArmyAbstractBattlePhase.Complete' `
    'Runtime must mark the transaction complete after all losers are handled.'
$releaseIndex = $service.IndexOf('ReleasePersistedParticipants(snapshot)')
$transferIndex = $service.IndexOf('TryTransferCity(transferredCity, receiver)')
if ($releaseIndex -lt 0 -or $transferIndex -lt 0 -or
    $releaseIndex -gt $transferIndex) {
    throw 'Prepared transactions must release strategic control before transfer.'
}
if ($service -notmatch 'foreach\s*\(City\s+.*World\.world\.cities') {
    throw 'Runtime must scan persisted city transactions for save/load recovery.'
}
Write-Output 'RTS card duel transaction source guard passed.'
