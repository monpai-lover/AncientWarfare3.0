$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$name = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\lineage\NameIntegrationMaterializationService.cs')
$culture = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\lineage\IntegratedCultureNamingMigrationService.cs')
$institution = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\policy\KingdomInstitutionalXiaizationService.cs')

function Require-Present([string] $source, [string] $needle,
    [string] $message) {
    if (-not $source.Contains($needle)) { throw $message }
}

function Require-Absent([string] $source, [string] $needle,
    [string] $message) {
    if ($source.Contains($needle)) { throw $message }
}

foreach ($service in @($name, $culture, $institution)) {
    Require-Present $service 'Queue<long> PendingOrder' `
        'Migration services must use a persistent round-robin queue.'
    Require-Absent $service 'Pending.Keys.ToArray()' `
        'Migration services must not allocate a key snapshot every cycle.'
}

Require-Present $name 'Dictionary<long, int> CandidateCursors' `
    'Name migration must resume candidates by index instead of rescanning.'
Require-Present $culture 'Dictionary<long, int> CandidateCursors' `
    'Culture migration must resume candidates by index instead of rescanning.'
Require-Present $culture 'BuildCandidateIndex()' `
    'Culture migrations must share one world Actor index.'
if ($culture -notmatch '(?s)internal static void Request\(Culture pCulture\).*?_candidateIndexBuilt = false;\s+CandidateCursors.Clear\(\);\s+Enqueue') {
    throw 'Invalidating the shared Actor index must also invalidate list cursors.'
}
Require-Present $institution 'MaxKingdomStagesPerCycle = 1' `
    'Institution migration must have a strict per-cycle kingdom budget.'
foreach ($service in @($name, $institution)) {
    Require-Present $service 'MaxRestoreKingdomsPerCycle = 4' `
        'World-load migration restore must be bounded per authority cycle.'
    Require-Present $service 'IEnumerator _restoreEnumerator' `
        'World-load migration restore must resume instead of rescanning kingdoms.'
}

Write-Output 'Migration performance source guard passed.'
