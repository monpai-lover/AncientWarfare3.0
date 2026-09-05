$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$windowPath = Join-Path $root 'Code\ui\windows\HistoricalFigureDrawWindow.cs'
$storePath = Join-Path $root 'Code\core\lineage\HistoricalFigureCardCollectionStore.cs'
$window = Get-Content -Raw -Encoding UTF8 -LiteralPath $windowPath
$store = Get-Content -Raw -Encoding UTF8 -LiteralPath $storePath

if ($window.Contains('[AW3 cards layout]')) {
    throw 'Card inventory layout still writes synchronous diagnostic logs.'
}

$updateStart = $window.IndexOf('private void UpdateInventory()')
$updateEnd = $window.IndexOf('private void UpdateCrateContents(', $updateStart)
if ($updateStart -lt 0 -or $updateEnd -le $updateStart) {
    throw 'Could not isolate UpdateInventory.'
}
$updateInventory = $window.Substring($updateStart, $updateEnd - $updateStart)
if ($updateInventory.Contains('Store.GetOwnedCount')) {
    throw 'UpdateInventory still enters the collection lock once per card.'
}
if ($window.Contains('.Sum(p => Store.GetOwnedCount(p.CardId))')) {
    throw 'Inventory rarity statistics still enter the store lock per card.'
}

$refreshStart = $window.IndexOf('private void Refresh()')
$refreshEnd = $window.IndexOf('private void Draw()', $refreshStart)
if ($refreshStart -lt 0 -or $refreshEnd -le $refreshStart) {
    throw 'Could not isolate Refresh.'
}
$refresh = $window.Substring($refreshStart, $refreshEnd - $refreshStart)
if (-not $refresh.Contains('bool needsInitialLayout = !_built')) {
    throw 'Refresh does not distinguish initial layout from ordinary tab switches.'
}
if (-not $refresh.Contains('if (needsInitialLayout) ApplyLayout();')) {
    throw 'Refresh no longer primes geometry only for the initial build.'
}

$requiredWindow = @(
    'Store.Revision',
    'Store.CopyInventorySnapshot(',
    '_inventoryCacheRevision',
    '_inventoryOwnedCounts',
    'private const int MaxTileBuildsPerFrame = 2'
)
foreach ($needle in $requiredWindow) {
    if (-not $window.Contains($needle)) {
        throw "Historical card inventory performance guard missing: $needle"
    }
}

$requiredStore = @(
    'public int Revision',
    'public int CopyInventorySnapshot(',
    '_revision++'
)
foreach ($needle in $requiredStore) {
    if (-not $store.Contains($needle)) {
        throw "Historical card collection revision guard missing: $needle"
    }
}

Write-Output 'Historical figure inventory open performance guard passed.'
