$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $repoRoot `
    'Code\core\policy\KingdomPolicyProfileService.cs'
$rulesPath = Join-Path $repoRoot `
    'Code\core\policy\KingdomPolicyProfileRules.cs'
$keysPath = Join-Path $repoRoot 'Code\core\lineage\LineageKeys.cs'
$lineageServicePath = Join-Path $repoRoot `
    'Code\core\lineage\LineageService.cs'

foreach ($requiredPath in @($servicePath, $rulesPath, $keysPath,
        $lineageServicePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Missing Western policy profile source: $requiredPath"
    }
}

$serviceText = Get-Content -LiteralPath $servicePath -Raw -Encoding UTF8
$rulesText = Get-Content -LiteralPath $rulesPath -Raw -Encoding UTF8
$keysText = Get-Content -LiteralPath $keysPath -Raw -Encoding UTF8
$lineageServiceText = Get-Content -LiteralPath $lineageServicePath -Raw `
    -Encoding UTF8

function Require-Match {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

Require-Match $keysText `
    'POLICY_PROFILE_ID\s*=\s*"aw_policy_profile_id"' `
    'LineageKeys must define the canonical policy-profile persistence key.'
Require-Match $rulesText 'public enum KingdomPolicyProfileId' `
    'The policy profile id must be a public pure-rule enum.'
Require-Match $rulesText 'public readonly struct KingdomPolicyProfileAssignmentDecision' `
    'Profile assignment must be represented as a pure decision value.'

Require-Match $serviceText `
    'public static KingdomPolicyProfileId Resolve\(Kingdom pKingdom\)' `
    'The runtime service must expose a read-only Resolve method.'
Require-Match $serviceText `
    'public static bool TryGet\(Kingdom pKingdom,\s*out KingdomPolicyProfileId pProfileId\)' `
    'The runtime service must expose a non-writing TryGet method.'
Require-Match $serviceText `
    'public static KingdomPolicyProfileId EnsureAssigned\(Kingdom pKingdom\)' `
    'The runtime service must expose EnsureAssigned as its write authority.'
Require-Match $serviceText 'pKingdom\s*==\s*null' `
    'The runtime service must reject null kingdoms.'
Require-Match $serviceText 'pKingdom\.data\s*==\s*null' `
    'The runtime service must reject kingdoms without data.'
Require-Match $serviceText 'pKingdom\.isRekt\(\)' `
    'The runtime service must reject destroyed kingdoms.'
Require-Match $serviceText 'pKingdom\.isNeutral\(\)' `
    'The runtime service must reject neutral kingdoms.'
Require-Match $serviceText `
    'LineageService\.IsXiaKingdom\(\s*pKingdom,\s*resolvedActorAsset\)' `
    'Native Xia detection must reuse LineageService with the cached actor asset.'
Require-Match $lineageServiceText `
    'IsXiaKingdom\(Kingdom pKingdom,\s*ActorAsset pResolvedActorAsset\)' `
    'LineageService must accept a pre-resolved actor asset without resolving it again.'
Require-Match $serviceText `
    'CivMonkeyPolicyRules\.IsNativePolicySpecies\(' `
    'Monkey policy routing must reuse CivMonkeyPolicyRules.'
Require-Match $serviceText `
    'KingdomInstitutionalXiaizationRules\.ShouldUseXiaInstitutions\(' `
    'Institutional Xia entry must use the independent level-five kingdom authority.'
Require-Match $serviceText 'resolvedActorAsset\.civ' `
    'Civilization eligibility must use only ActorAsset.civ.'
Require-Match $serviceText `
    'KingdomPolicyProfileRules\.DecideAssignment\(' `
    'Stored-profile repair must delegate to the pure assignment rules.'
Require-Match $serviceText `
    'LineageKeys\.POLICY_PROFILE_ID' `
    'Runtime reads and writes must use the canonical lineage key.'

$actorAssetCalls = [regex]::Matches($serviceText, 'getActorAsset\(').Count
if ($actorAssetCalls -ne 1) {
    throw "KingdomPolicyProfileService must call getActorAsset exactly once; found $actorAssetCalls."
}

$writes = [regex]::Matches($serviceText, '\.data\.set\(').Count
if ($writes -ne 1) {
    throw "EnsureAssigned must be the unique profile write path; found $writes writes."
}

$resolveStart = $serviceText.IndexOf(
    'public static KingdomPolicyProfileId Resolve(Kingdom pKingdom)')
$tryGetStart = $serviceText.IndexOf(
    'public static bool TryGet(Kingdom pKingdom,')
$ensureStart = $serviceText.IndexOf(
    'public static KingdomPolicyProfileId EnsureAssigned(Kingdom pKingdom)')
if ($resolveStart -lt 0 -or $tryGetStart -le $resolveStart -or
    $ensureStart -le $tryGetStart) {
    throw 'Profile service method order is unavailable for write-scope validation.'
}

$resolveText = $serviceText.Substring($resolveStart,
    $tryGetStart - $resolveStart)
$tryGetText = $serviceText.Substring($tryGetStart,
    $ensureStart - $tryGetStart)
$ensureText = $serviceText.Substring($ensureStart)
if ($resolveText -match 'POLICY_PROFILE_ID|\.data\.set\(') {
    throw 'Resolve must use runtime facts only and never read or write persistence.'
}
if ($tryGetText -match '\.data\.set\(') {
    throw 'TryGet must not mutate kingdom profile persistence.'
}
if ($ensureText -notmatch '\.data\.set\(') {
    throw 'EnsureAssigned must own the single profile persistence write.'
}

$forbiddenPatterns = @(
    'AWNamingProfileRules',
    '\bNamingProfileId\b',
    'OrcNomadic',
    'WesternNamingTradition',
    'XiaizationService\s*\.\s*CanUsePolicySystem',
    'World\s*\.\s*world\s*\.\s*kingdoms',
    '\bunits\b',
    'SQLite',
    'OperatingDB'
)
foreach ($pattern in $forbiddenPatterns) {
    if ($serviceText -match $pattern) {
        throw "Policy profile service contains forbidden dependency: $pattern"
    }
}

Write-Output 'Western policy profile source guard passed.'
