$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$traitPath = Join-Path $repo 'Code/content/XiaCultureTraits.cs'
$rulesPath = Join-Path $repo 'Code/core/policy/KingdomPolicySplitInheritanceRules.cs'
$servicePath = Join-Path $repo 'Code/core/policy/KingdomPolicyInheritanceService.cs'
$patchPath = Join-Path $repo 'Code/patch/AW_KingdomPolicyPatch.cs'
$xiaPath = Join-Path $repo 'Code/core/lineage/XiaizationService.cs'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "ASSERT: $Message"
    }
}

Assert-True (Test-Path -LiteralPath $traitPath) `
    'a persisted culture trait defines the authoritative Xia integration marker'
Assert-True (Test-Path -LiteralPath $rulesPath) `
    'split inheritance routing is expressed as pure rules'

$traitSource = Get-Content -Raw -LiteralPath $traitPath
$rulesSource = Get-Content -Raw -LiteralPath $rulesPath
$serviceSource = Get-Content -Raw -LiteralPath $servicePath
$patchSource = Get-Content -Raw -LiteralPath $patchPath
$xiaSource = Get-Content -Raw -LiteralPath $xiaPath

Assert-True ($traitSource.Contains('public const string IntegratedTraitId')) `
    'the culture marker has one stable persisted trait id'
Assert-True ($traitSource.Contains('public const string FullyIntegratedTraitId')) `
    'full Xia entry has a separate stable persisted culture trait id'
Assert-True ($traitSource.Contains('saved_traits')) `
    'the marker documents vanilla CultureData.saved_traits persistence'
Assert-True ($rulesSource.Contains('ShouldCaptureSplitSource')) `
    'pure rules distinguish rebellion/collapse splits from ordinary creation'
Assert-True ($rulesSource.Contains('ShouldInheritFromSplit')) `
    'pure rules require an authoritative child policy profile'
Assert-True ($rulesSource.Contains('ShouldMarkCultureFullyIntegrated')) `
    'pure rules distinguish level-four institutions from level-five full entry'
Assert-True ($patchSource.Contains('bool pRebellion') -and
             $patchSource.Contains('bool pFellApart')) `
    'the makeOwnKingdom prefix observes both vanilla split flags'
Assert-True (-not $patchSource.Contains('MakeNewCivKingdom_PolicyPostfix')) `
    'ordinary makeNewCivKingdom creation cannot trigger inheritance'
Assert-True (-not $serviceSource.Contains('FindRegionalSource')) `
    'inheritance never guesses a regional source kingdom'
Assert-True (-not $serviceSource.Contains(
        'bool integratedCulture = XiaCultureIntegrationService.IsIntegrated')) `
    'ordinary western cultures must not be rejected before policy inheritance'
Assert-True ($serviceSource.Contains(
        'KingdomPolicyProfileService.Resolve(pNewKingdom)')) `
    'split inheritance resolves the child profile from its actual culture'
Assert-True ($serviceSource.Contains(
        'IsResolvableKingdomProfile(childProfile)')) `
    'both Xia and WesternGeneral profiles may inherit compatible progress'
Assert-True ($serviceSource.Contains('XiaizationService.InheritForSplit')) `
    'Xiaization is projected before policy eligibility is checked'
$xiaInheritanceIndex = $serviceSource.IndexOf(
    'XiaizationService.InheritForSplit')
$newPolicyGateIndex = $serviceSource.IndexOf(
    'KingdomPolicyService.CanUsePolicySystem(pNewKingdom)',
    $xiaInheritanceIndex)
Assert-True ($xiaInheritanceIndex -ge 0 -and
             $newPolicyGateIndex -gt $xiaInheritanceIndex) `
    'the inherited Xia level enables the non-Xia policy system before its gate'
Assert-True ($serviceSource.Contains('locked_nodes = ""')) `
    'new realms do not inherit policy UI locks or queued preferences'
Assert-True ($serviceSource.Contains('pIncludeDecision: false')) `
    'current decisions, queues, targets and one-time completions stay local'
Assert-True ($serviceSource.Contains(
        'ResolveInheritedGovernmentState')) `
    'split government state must be sanitized for the child profile'
Assert-True ($serviceSource.Contains(
        'ResolveInheritedRoyalAuthority')) `
    'split royal authority inheritance must be explicit and bounded'
Assert-True ($serviceSource.Contains(
        'XiaizationService.UsesXiaizedInstitutionSystem')) `
    'inherited name integration is projected for non-Xia institutional realms'
Assert-True ($xiaSource.Contains(
        'XiaCultureIntegrationService.IsNativeXiaCulture')) `
    'native migration is based on the culture itself, not its current kingdom'
Assert-True ($xiaSource.Contains(
        'RestorePersistedCultureIntegrations')) `
    'old saves restore authoritative culture ids from persisted Xiaization state'
Assert-True ($xiaSource.Contains(
        'SELECT DISTINCT COURT_CULTURE_ID')) `
    'old-save migration does not infer culture identity from the current kingdom'
Assert-True ($xiaSource.Contains('XiaCultureIntegrationService.MarkIntegrated')) `
    'reaching permanent Xia institutions projects the culture marker'
Assert-True ($xiaSource.Contains('XiaCultureIntegrationService.MarkFullyIntegrated')) `
    'reaching level-five full entry projects the independent full marker'

Write-Host 'Kingdom policy split inheritance source guard passed.'
