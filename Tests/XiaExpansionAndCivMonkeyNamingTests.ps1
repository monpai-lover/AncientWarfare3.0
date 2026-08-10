param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Require-Present([string]$name, [string]$relativePath,
    [string]$needle) {
    $fullPath = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($fullPath)) {
        $failures.Add("${name}: missing source file $relativePath")
        return
    }
    $source = [IO.File]::ReadAllText($fullPath)
    if (-not $source.Contains($needle)) {
        $failures.Add("${name}: missing '$needle' in $relativePath")
    }
}

function Require-Absent([string]$name, [string]$relativePath,
    [string]$needle) {
    $fullPath = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($fullPath)) { return }
    $source = [IO.File]::ReadAllText($fullPath)
    if ($source.Contains($needle)) {
        $failures.Add("${name}: found forbidden '$needle' in $relativePath")
    }
}

function U([int[]]$codePoints) {
    return -join ($codePoints | ForEach-Object { [char]$_ })
}

$monkeySurnameKey = U @(0x7334,0x65CF,0x59D3,0x6C0F)
$monkeyGivenNameKey = U @(0x7334,0x65CF,0x540D)
$monkeyKingdomKey = U @(0x7334,0x65CF,0x56FD,0x5BB6)
$monkeyCityKey = U @(0x7334,0x65CF,0x57CE,0x5E02)

$decisionContent = 'Code/content/XiaExpansionDecisionContent.cs'
Require-Present 'foundation decision is modified at its weight source' `
    $decisionContent 'weight_calculate_custom'
Require-Present 'foundation decision chains the upstream calculator' `
    $decisionContent 'originalCalculator'
Require-Present 'foundation decision preserves the upstream static weight' `
    $decisionContent 'originalWeight'
Require-Present 'foundation decision enables custom-weight evaluation' `
    $decisionContent 'has_weight_custom = true'
Require-Present 'foundation decision uses Xia-only weight rules' `
    $decisionContent 'XiaExpansionDecisionRules.ApplyWeight'
Require-Absent 'foundation tuning does not replace vanilla launch gates' `
    $decisionContent 'action_check_launch ='
Require-Absent 'foundation tuning does not shorten the vanilla cooldown' `
    $decisionContent '.cooldown ='

$namingContent = 'Code/content/CivMonkeyNamingContent.cs'
Require-Present 'civilized monkeys receive a private name set' `
    $namingContent 'CivMonkeyNamingRules.NameSetId'
Require-Present 'only the civilized monkey actor is rerouted' `
    $namingContent 'CivMonkeyNamingRules.ActorAssetId'
Require-Present 'kingdoms use a generator separate from other meta objects' `
    $namingContent 'set.kingdom = CivMonkeyNamingRules.KingdomGeneratorId'
Require-Present 'cities use a generator separate from other meta objects' `
    $namingContent 'set.city = CivMonkeyNamingRules.CityGeneratorId'
Require-Present 'actors use their dedicated surname plus given-name generator' `
    $namingContent 'set.unit = CivMonkeyNamingRules.ActorGeneratorId'
Require-Present 'private name set copies the original civ_monkey metadata generators' `
    $namingContent 'NameSetAsset original = ResolveOriginalNameSet(pActor);'
Require-Present 'clan naming uses a surname-only civilized-monkey generator' `
    $namingContent 'set.clan = CivMonkeyNamingRules.ClanGeneratorId;'
Require-Present 'culture naming remains on the original monkey generator' `
    $namingContent 'set.culture = OriginalOrMonkey(original?.culture);'
Require-Present 'family naming remains on the original monkey generator' `
    $namingContent 'set.family = OriginalOrMonkey(original?.family);'
Require-Present 'language naming remains on the original monkey generator' `
    $namingContent 'set.language = OriginalOrMonkey(original?.language);'
Require-Present 'religion naming remains on the original monkey generator' `
    $namingContent 'set.religion = OriginalOrMonkey(original?.religion);'
Require-Present 'ChineseName clan generation receives the founder surname' `
    $namingContent 'ParameterGetters.PutClanParameterGetter('
Require-Present 'ChineseName has a dedicated civilized-monkey clan route' `
    $namingContent 'MonkeyNameKind.Clan'
Require-Absent 'given-name pool cannot leak into language naming' `
    $namingContent 'set.language = CivMonkeyNamingRules.CommonGeneratorId'
Require-Absent 'given-name pool cannot leak into religion naming' `
    $namingContent 'set.religion = CivMonkeyNamingRules.CommonGeneratorId'
Require-Present 'ordinary monkey generator remains untouched' `
    $namingContent 'AssetManager.name_generator.add'
Require-Absent 'ordinary monkey generator is never overwritten' `
    $namingContent 'RegisterVanillaGenerator("monkey_name"'
Require-Present 'integrated source generators are registered in AW3' `
    $namingContent 'AWNameGeneratorLibrary.Submit'
Require-Present 'integrated naming uses deterministic civilized-monkey generators' `
    $namingContent 'CivMonkeyIntegratedNameGenerator'
Require-Present 'integrated naming loads AW3 editable monkey word libraries' `
    $namingContent 'AWNamingResourceLoader.LoadWordLibraries'
Require-Present 'integrated naming installs monkey libraries in the AW3 manager' `
    $namingContent 'AWWordLibraryManager.Instance.Submit'
Require-Present 'actor ChineseName template carries family_name back to ActorNamePatch' `
    $namingContent ('"{' + $monkeySurnameKey + ':family_name}{' +
        $monkeyGivenNameKey + ':given_name}"')
Require-Present 'city ChineseName template reads the monkey city library' `
    $namingContent ('"{' + $monkeyCityKey + ':city_name}"')
Require-Present 'kingdom ChineseName template reads the monkey kingdom library' `
    $namingContent ('"{' + $monkeyKingdomKey + ':kingdom_name}"')

$namingPatch = 'Code/patch/AW_CivMonkeyNamingPatch.cs'
Require-Present 'name generation is intercepted before display or persistence' `
    $namingPatch 'typeof(NameGenerator), nameof(NameGenerator.generateName)'
Require-Present 'name override is scoped to civ_monkey' `
    $namingPatch 'CivMonkeyNamingRules.IsCivilizedMonkey'
Require-Present 'name override is seed deterministic' `
    $namingPatch 'CivMonkeyNamingRules.PickKingdom(pSeed, (int)pType)'
Require-Present 'actor fallback preserves the inherited paternal surname' `
    $namingPatch 'CivMonkeyNamingRules.BuildActorName'
Require-Present 'kingdom pool selection follows the runtime enum' `
    $namingPatch 'pType == MetaType.Kingdom'
Require-Present 'clan fallback preserves the founder surname' `
    $namingPatch 'pType == MetaType.Clan'
Require-Absent 'AW3 cannot overwrite civilized-monkey language names' `
    $namingPatch 'LanguageGenerateNamePostfix'
Require-Absent 'AW3 cannot overwrite civilized-monkey religion names' `
    $namingPatch 'ReligionGenerateNamePostfix'
Require-Present 'non-target metadata falls through to original monkey naming' `
    $namingPatch 'return true;'
Require-Absent 'non-target metadata cannot use the monkey given-name fallback' `
    $namingPatch 'CivMonkeyNamingRules.Pick(pSeed'

$lineageService = 'Code/core/lineage/LineageService.cs'
Require-Present 'monkey lineage initialization preserves the ChineseName surname' `
    $lineageService 'string inheritedOrExistingShi = CivMonkeyNamingRules.ResolveLineageSurname('
Require-Present 'monkey lineage surname selection receives both ChineseName family fields' `
    $lineageService 'existingShiId >= 0, existingClan, chineseFamily, existingFamily);'
Require-Present 'civilized monkey births enter native Xia-culture lineage initialization' `
    'Code/patch/AW_BirthPatch.cs' 'LineageService.IsNativeXiaCultureActor(__instance)'
Require-Present 'civilized monkey clan changes refresh lineage archives' `
    'Code/patch/AW_ClanEventPatch.cs' 'LineageService.IsNativeXiaCultureActor(__instance)'
Require-Present 'native Xia-culture clan members can be archived before ennoblement' `
    'Code/core/lineage/LineageArchiveWriter.cs' 'LineageService.HasOriginalClan(pActor)'
Require-Present 'trace-only archive updates include civilized monkeys' `
    'Code/core/lineage/LineageArchiveWriter.cs' 'LineageService.IsNativeXiaCultureActor(pActor)'
Require-Present 'detached family snapshots retain archived species identity' `
    'Code/core/lineage/LineageBulkQuery.cs' 'AssetId = strings.Take(actor?.asset_id);'
Require-Present 'family tree DTO retains archived species identity' `
    'Code/core/lineage/LineageDTO.cs' 'public string asset_id = "";'
Require-Present 'family tree materialization forwards archived species identity' `
    'Code/ui/windows/FamilyTreeWindow.cs' 'asset_id = node.AssetId,'
Require-Present 'archived portraits select the archived actor asset' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'ResolveArchivedActorAsset(pNode)'
Require-Absent 'native culture admission cannot replace biological sprite gates' `
    'Code/patch/AW_ActorMainSpritePatch.cs' 'IsNativeXiaCultureActor'

$rulesPath = Join-Path $root 'Code/content/CivMonkeyNamingRules.cs'
if (-not [IO.File]::Exists($rulesPath)) {
    $failures.Add('civ monkey naming rules: missing source file Code/content/CivMonkeyNamingRules.cs')
}
else {
    $rulesSource = [IO.File]::ReadAllText($rulesPath)
    if (-not $rulesSource.Contains('Surnames')) {
        $failures.Add('civ monkey naming pool: missing Surnames')
    }
    if (-not $rulesSource.Contains('GivenNames')) {
        $failures.Add('civ monkey naming pool: missing GivenNames')
    }
    if (-not $rulesSource.Contains('CityNames')) {
        $failures.Add('civ monkey city pool: missing CityNames')
    }
    if (-not $rulesSource.Contains('KingdomNames')) {
        $failures.Add('civ monkey kingdom pool: missing KingdomNames')
    }
}

$expectedLibraries = @(
    ('name_generators/lib/' + $monkeySurnameKey + '.txt'),
    ('name_generators/lib/' + $monkeyGivenNameKey + '.txt'),
    ('name_generators/lib/' + $monkeyKingdomKey + '.txt'),
    ('name_generators/lib/' + $monkeyCityKey + '.txt'))
foreach ($relativePath in $expectedLibraries) {
    $fullPath = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($fullPath)) {
        $failures.Add("editable monkey word library missing: $relativePath")
        continue
    }
    try {
        $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
        $text = $strictUtf8.GetString([IO.File]::ReadAllBytes($fullPath))
    }
    catch {
        $failures.Add("editable monkey word library is not valid UTF-8: $relativePath")
        continue
    }
    $actual = @($text.Replace("`r", '').Split("`n") |
        ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    if ($actual.Count -eq 0) {
        $failures.Add("editable monkey word library is empty: $relativePath")
    }
}

$policyRules = 'Code/core/policy/CivMonkeyPolicyRules.cs'
Require-Present 'civilized monkey policy eligibility is isolated in a pure rule' `
    $policyRules 'IsNativePolicySpecies'
Require-Present 'policy eligibility names only civ_monkey' `
    $policyRules '"civ_monkey"'
Require-Absent 'wild monkey never gains native AW3 policy eligibility' `
    $policyRules '"monkey"'

$xiaizationService = 'Code/core/lineage/XiaizationService.cs'
Require-Present 'Xiaization exposes one shared native policy kingdom gate' `
    $xiaizationService 'IsNativePolicyKingdom'
Require-Present 'native policy gate delegates civ_monkey classification to pure rules' `
    $xiaizationService 'CivMonkeyPolicyRules.IsNativePolicySpecies'
Require-Present 'policy support uses the shared native policy gate' `
    $xiaizationService 'XiaizationEligibilityRules.CanUsePolicySystem('
Require-Present 'policy defaults enable native policy kingdoms' `
    $xiaizationService 'return IsNativePolicyKingdom(pKingdom) ||'

Require-Present 'policy node access treats civ_monkey as a native policy kingdom' `
    'Code/core/policy/KingdomPolicyService.cs' `
    'XiaizationService.IsNativePolicyKingdom(pKingdom),'
Require-Present 'new kingdoms already use the one-shot policy inheritance initializer' `
    'Code/patch/AW_KingdomPolicyPatch.cs' `
    'KingdomPolicyInheritanceService.InheritForNewKingdom(__result,'

Require-Present 'AI research reads explicit nine-rank completion' `
    'Code/core/policy/KingdomPolicyAI.cs' `
    'bool nineRankCompleted = pKind != PolicyNodeKind.Tech ||'
Require-Present 'AI passes nine-rank completion into tech ordering' `
    'Code/core/policy/KingdomPolicyAI.cs' `
    'officialCourtCompleted, ritesMusicCompleted, nineRankCompleted))'
Require-Present 'three departments consideration explicitly requires nine-rank' `
    'Code/core/policy/KingdomPolicyTechOrderRules.cs' `
    'if (pId == "aw_tech_three_departments") return pNineRankCompleted;'

if ($failures.Count -gt 0) {
    Write-Host "Xia expansion/civ monkey failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Xia expansion and civ monkey naming guards passed.'
