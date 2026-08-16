$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$modelPath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdState.cs'
$storePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStateStore.cs'
$keysPath = Join-Path $root 'Code/core/lineage/LineageKeys.cs'

if (-not (Test-Path -LiteralPath $modelPath)) {
    throw 'Missing PeasantRebelBanditStrongholdState.cs'
}
if (-not (Test-Path -LiteralPath $storePath)) {
    throw 'Missing PeasantRebelBanditStateStore.cs'
}

$model = Get-Content -Raw -Encoding UTF8 $modelPath
$store = Get-Content -Raw -Encoding UTF8 $storePath
$keys = Get-Content -Raw -Encoding UTF8 $keysPath

$requiredModelTokens = @(
    'CurrentSchemaVersion = 4',
    'SchemaVersion', 'Phase', 'StrongholdCityId', 'MotherCityId',
    'OriginKingdomId', 'FixedZoneKeys', 'WallPoints', 'Raid',
    'BanditStrongholdTower', 'TowerBuildingId', 'AssetId',
    'LastHostileKillerKingdomId',
    'Stage', 'MemberActorIds', 'TargetCityId', 'CarriedFood',
    'CooldownUntilYear', 'SuppressionExpiryByKingdomId'
)
foreach ($token in $requiredModelTokens) {
    if ($model -notmatch [regex]::Escape($token)) {
        throw "Persistent bandit model is missing $token"
    }
}

if ($keys -notmatch 'MANDATE_REBEL_BANDIT_STRONGHOLD_STATE') {
    throw 'Missing stronghold state lineage key'
}
foreach ($token in @('TryRead(', 'Write(', 'Clear(', 'TryResolveActive(',
        'JsonConvert.DeserializeObject', 'catch')) {
    if ($store -notmatch [regex]::Escape($token)) {
        throw "Bandit state store is missing $token"
    }
}

Write-Output 'Bandit stronghold persistence source guard passed.'
