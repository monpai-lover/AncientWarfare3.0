$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$relativeTargets = @(
    'Code/core/policy/KingdomPolicyService.cs',
    'Code/core/policy/KingdomPolicyAI.cs',
    'Code/core/policy/KingdomPolicyInheritanceService.cs',
    'Code/core/policy/CityTechService.cs',
    'Code/core/lineage/WarTerritoryService.cs',
    'Code/core/policy/TechMapModeService.cs',
    'Code/core/policy/DevelopmentMapModeService.cs',
    'Code/core/policy/AWMapModeMetaLibrary.cs',
    'Code/ui/windows/KingdomPolicyWindow.cs',
    'Code/ui/windows/KingdomWindowAddition.cs'
)

function Assert-ProfileAwareGetCalls([string] $path, [string] $source) {
    $marker = 'KingdomPolicyDefs.Get('
    $offset = 0
    while (($index = $source.IndexOf($marker, $offset,
            [StringComparison]::Ordinal)) -ge 0) {
        $cursor = $index + $marker.Length
        $depth = 0
        $commas = 0
        $inString = $false
        $escaped = $false
        for (; $cursor -lt $source.Length; $cursor++) {
            $ch = $source[$cursor]
            if ($inString) {
                if ($escaped) { $escaped = $false; continue }
                if ($ch -eq '\') { $escaped = $true; continue }
                if ($ch -eq '"') { $inString = $false }
                continue
            }
            if ($ch -eq '"') { $inString = $true; continue }
            if ($ch -eq '(') { $depth++; continue }
            if ($ch -eq ')') {
                if ($depth -eq 0) { break }
                $depth--
                continue
            }
            if ($ch -eq ',' -and $depth -eq 0) { $commas++ }
        }
        if ($commas -lt 1) {
            throw "Profile-blind KingdomPolicyDefs.Get call in $path"
        }
        $offset = [Math]::Max($cursor + 1, $index + $marker.Length)
    }
}

foreach ($relative in $relativeTargets) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $source = Get-Content -Raw -LiteralPath $path
    if ($source -match 'KingdomPolicyDefs\.(Techs|SocialPolicies|Decisions|ResearchPolicies)\b') {
        throw "Global policy catalog enumeration remains in $relative"
    }
    if ($source -match 'KingdomPolicyDefs\.GetAny\(') {
        throw "Migration-only GetAny leaks into active consumer $relative"
    }
    Assert-ProfileAwareGetCalls $relative $source
}

$servicePath = Join-Path $root 'Code/core/policy/KingdomPolicyService.cs'
$service = Get-Content -Raw -LiteralPath $servicePath
if ($service -notmatch 'KingdomPolicyProfileService') {
    throw 'KingdomPolicyService does not resolve an authoritative policy profile.'
}

$aiPath = Join-Path $root 'Code/core/policy/KingdomPolicyAI.cs'
$ai = Get-Content -Raw -LiteralPath $aiPath
foreach ($required in @(
        'KingdomPolicyProfileId.WesternGeneral',
        'BuildWesternPolicyNeedFacts(',
        'new WesternPolicyCandidate(',
        'WesternPolicyAiRules.SelectBest('
    )) {
    if (-not $ai.Contains($required)) {
        throw "Western need-driven AI runtime integration missing: $required"
    }
}

$westernMethodStart = $ai.IndexOf(
    'private static KingdomPolicyDef PickWesternResearch(',
    [StringComparison]::Ordinal)
if ($westernMethodStart -lt 0) {
    throw 'Western research is not isolated from the legacy Xia ordering path.'
}
$westernMethodEnd = $ai.IndexOf(
    'private static ', $westernMethodStart + 20,
    [StringComparison]::Ordinal)
if ($westernMethodEnd -lt 0) { $westernMethodEnd = $ai.Length }
$westernMethod = $ai.Substring(
    $westernMethodStart, $westernMethodEnd - $westernMethodStart)
if ($westernMethod.Contains('PreferredIndex(') -or
    $westernMethod.Contains('SocialOrder')) {
    throw 'Western AI still uses fixed layout/order as primary scoring.'
}

Write-Output 'Western policy catalog source guard passed.'
